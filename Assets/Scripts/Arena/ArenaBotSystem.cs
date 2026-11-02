using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// A bot ally in the arena. It plays through a full client, so it sees the same interpolated
    /// enemies, players and soul fragments a human would. Three profiles:
    /// - Medic (the first NetTestSettings.ArenaHealers seats): Vigor. Uses <see cref="MedicBrain"/>:
    ///   shoot a downed ally's fragments, heal the most hurt, keep buffs up, then shoot enemies.
    /// - Haste support (odd seats): buffs allies now and then and grows chains it holds.
    /// - Might damage (even seats): shoots enemies (cashing any chain it holds), sometimes buffs.
    /// Every bot helps revive a downed ally. All of them dodge with ADAD strafing and jumps, so
    /// buffing them is a tracking drill. Aim uses the Core.Combat aim model, strong and weak mixed.
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ArenaBotSystem : SystemBase
    {
        private enum Profile { Medic, Haste, Might }

        private const float RetargetInterval = 0.5f;
        private const float AllyRange = 30f;
        private const float BuffChance = 0.25f;
        private const float ReviveHelpChance = 0.7f;
        private const float FragmentRadius = 0.2f;

        private BotAim _aim;
        private BotMover _mover;
        private readonly BotMoveSettings _moveSettings = new BotMoveSettings { MinDistance = 6f, MaxDistance = 14f };
        private readonly CombatSettings _combat = new CombatSettings();
        private readonly MedicSettings _medic = new MedicSettings { Range = AllyRange };
        private int _seat = -1;
        private Profile _profile;
        private PayloadKind _wanted;
        private float _retargetIn;
        private float _swapCooldown;
        private Entity _target;
        private bool _targetIsPlayer;
        private Unity.Mathematics.Random _random;
        private EntityQuery _local, _players, _enemies, _fragments;

        private readonly List<MedicAlly> _allies = new List<MedicAlly>();
        private readonly List<MedicEnemy> _enemyList = new List<MedicEnemy>();
        private readonly Dictionary<int, Entity> _entityById = new Dictionary<int, Entity>();
        private readonly Dictionary<int, List<SoulFragment>> _fragmentsByOwner = new Dictionary<int, List<SoulFragment>>();

        protected override void OnCreate()
        {
            RequireForUpdate<ArenaBotSeat>();
            RequireForUpdate<ArenaPlayer>();
            _local = GetEntityQuery(ComponentType.ReadOnly<ArenaPlayer>(), ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<ArenaVitals>(), ComponentType.ReadOnly<ArenaLoadout>(),
                ComponentType.ReadOnly<ArenaBuffs>());
            _players = GetEntityQuery(ComponentType.ReadOnly<ArenaPlayer>(), ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<ArenaVitals>(), ComponentType.ReadOnly<ArenaBuffs>(), ComponentType.ReadOnly<GhostOwner>());
            _enemies = GetEntityQuery(ComponentType.ReadOnly<ArenaEnemy>(), ComponentType.ReadOnly<LocalTransform>());
            _fragments = GetEntityQuery(ComponentType.ReadOnly<ArenaFragment>(), ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnUpdate()
        {
            if (_seat < 0)
            {
                _seat = SystemAPI.GetSingleton<ArenaBotSeat>().Index;
                uint seed = (uint)(_seat * 7919 + 101);
                _aim = new BotAim(_seat % 2 == 0 ? BotAimSettings.Strong() : BotAimSettings.Weak(), seed);
                _mover = BotMover.Create(seed + 1);
                _profile = _seat < NetTestSettings.ArenaHealers ? Profile.Medic
                    : (_seat - NetTestSettings.ArenaHealers) % 2 == 0 ? Profile.Haste : Profile.Might;
                _wanted = _profile == Profile.Medic ? PayloadKind.Vigor : _profile == Profile.Haste ? PayloadKind.Haste : PayloadKind.Might;
                _random = new Unity.Mathematics.Random(seed + 2);
            }
            if (_local.CalculateEntityCount() != 1) return;

            float dt = SystemAPI.Time.DeltaTime;
            Entity me;
            using (var locals = _local.ToEntityArray(Allocator.Temp)) me = locals[0];
            var pos = EntityManager.GetComponentData<LocalTransform>(me).Position;
            var vitals = EntityManager.GetComponentData<ArenaVitals>(me);
            var payload = EntityManager.GetComponentData<ArenaLoadout>(me).Payload;
            var input = new ArenaInput();

            if (vitals.Health <= 0f)
            {
                SetInput(me, input);
                return;
            }

            _swapCooldown -= dt;
            if (payload != _wanted && _swapCooldown <= 0f)
            {
                input.SwapPayload.Set();
                _swapCooldown = 0.3f;
            }

            _retargetIn -= dt;
            if (_retargetIn <= 0f || !EntityManager.Exists(_target))
            {
                _retargetIn = RetargetInterval;
                ChooseTarget(me, pos, payload);
            }

            var eye = pos + new float3(0f, ArenaSettings.EyeHeight, 0f);
            float3 anchor;
            if (_target != Entity.Null && EntityManager.Exists(_target))
            {
                var tpos = EntityManager.GetComponentData<LocalTransform>(_target).Position;
                float radius;
                float3 aimPoint;
                if (EntityManager.HasComponent<ArenaFragment>(_target))
                {
                    aimPoint = tpos;
                    radius = FragmentRadius;
                }
                else if (_targetIsPlayer)
                {
                    aimPoint = tpos + new float3(0f, ArenaWeapon.PlayerHitHeight, 0f);
                    radius = ArenaWeapon.PlayerHitRadius;
                }
                else
                {
                    var enemy = EntityManager.GetComponentData<ArenaEnemy>(_target);
                    aimPoint = tpos;
                    radius = enemy.Family == EnemyFamily.Wisp ? 0.35f : enemy.Family == EnemyFamily.Skitter ? 0.6f : 0.15f;
                }
                var aim = _aim.Step(dt, ToVec(eye), _target.Index, ToVec(aimPoint), radius);
                input.Yaw = aim.Yaw;
                input.Pitch = aim.Pitch;
                bool clear = ArenaLayout.RaycastCover(eye, math.normalize(aimPoint - eye), math.distance(eye, aimPoint)) == float.MaxValue;
                input.Fire = aim.Fire && clear ? 1 : 0;
                anchor = tpos;
            }
            else
            {
                var look = EntityManager.GetComponentData<ArenaLook>(me);
                input.Yaw = look.Yaw;
                input.Pitch = 0f;
                anchor = new float3(0f, 0f, 8f);
            }

            var move = _mover.Step(dt, ToVec(pos), input.Yaw, ToVec(anchor), _moveSettings);
            input.Horizontal = move.Horizontal;
            input.Vertical = move.Vertical;
            if (move.Jump) input.Jump.Set();
            SetInput(me, input);
        }

        private void SetInput(Entity me, ArenaInput input)
        {
            EntityManager.SetComponentData(me, input);
        }

        /// <summary>Builds the bot's view of allies, fragments and enemies from replicated ghosts, then picks a target.</summary>
        private void ChooseTarget(Entity me, float3 pos, PayloadKind payload)
        {
            _target = Entity.Null;
            _targetIsPlayer = false;
            var eye = pos + new float3(0f, ArenaSettings.EyeHeight, 0f);
            int myId = EntityManager.GetComponentData<GhostOwner>(me).NetworkId;
            var myBuffs = EntityManager.GetComponentData<ArenaBuffs>(me);
            Gather(eye);

            // Anyone helps revive: the Medic always, the others most of the time.
            if (_profile == Profile.Medic || _random.NextFloat() < ReviveHelpChance)
            {
                var d = MedicBrain.Decide(myId, ToVec(eye), payload, _allies, System.Array.Empty<MedicEnemy>(), null, 0, _medic, _combat);
                if (d.Kind == MedicTargetKind.Fragment && Visible(eye, d.AimPoint))
                {
                    Pick(new Entity { Index = d.TargetId, Version = FragmentVersion(d.TargetId) }, true);
                    return;
                }
            }

            switch (_profile)
            {
                case Profile.Medic:
                {
                    var d = MedicBrain.Decide(myId, ToVec(eye), payload, _allies, _enemyList, null, 0, _medic, _combat);
                    // Our own chain: pass it to an ally who isn't holding one (approximates "not in it yet").
                    if (myBuffs.ChainLinks > 0 && d.Kind != MedicTargetKind.Fragment && d.Kind != MedicTargetKind.Heal &&
                        TryNearestAlly(myId, eye, a => !a.HoldsChain, out var allyEntity))
                    {
                        Pick(allyEntity, true);
                        return;
                    }
                    if (d.Kind == MedicTargetKind.Enemy) Pick(EnemyEntity(d.TargetId), false);
                    else if (d.Kind != MedicTargetKind.None && _entityById.TryGetValue(d.TargetId, out var ally)) Pick(ally, true);
                    return;
                }
                case Profile.Haste:
                {
                    // Grow a short chain we hold; cash a long one.
                    if (myBuffs.ChainLinks > 0 && myBuffs.ChainLinks < 3 && TryNearestAlly(myId, eye, a => !a.HoldsChain, out var next))
                    {
                        Pick(next, true);
                        return;
                    }
                    if (_random.NextFloat() < 0.4f && TryNearestAlly(myId, eye, a => a.HasteLeft < 1.5f, out var ally))
                    {
                        Pick(ally, true);
                        return;
                    }
                    break;
                }
                default:
                {
                    // Hold a chain: cash it on an enemy. Otherwise mostly shoot, sometimes buff.
                    if (myBuffs.ChainLinks == 0 && _random.NextFloat() < BuffChance && TryNearestAlly(myId, eye, a => true, out var ally))
                    {
                        Pick(ally, true);
                        return;
                    }
                    break;
                }
            }

            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _enemyList.Count; i++)
            {
                if (!_enemyList[i].Hittable) continue;
                float d = Vector3.Distance(ToVec(eye), _enemyList[i].Position);
                if (d < best)
                {
                    best = d;
                    nearest = i;
                }
            }
            if (nearest >= 0) Pick(EnemyEntity(nearest), false);
        }

        private struct AllyView
        {
            public Entity Entity;
            public bool HoldsChain;
            public float HasteLeft;
            public Vector3 Position;
        }

        private readonly List<AllyView> _allyViews = new List<AllyView>();
        private readonly List<Entity> _enemyEntities = new List<Entity>();
        private readonly Dictionary<int, int> _fragmentVersions = new Dictionary<int, int>();

        private void Gather(float3 eye)
        {
            _allies.Clear();
            _allyViews.Clear();
            _entityById.Clear();
            _enemyList.Clear();
            _enemyEntities.Clear();
            _fragmentVersions.Clear();
            foreach (var list in _fragmentsByOwner.Values) list.Clear();

            using (var frags = _fragments.ToEntityArray(Allocator.Temp))
            using (var fragData = _fragments.ToComponentDataArray<ArenaFragment>(Allocator.Temp))
            using (var fragPos = _fragments.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < frags.Length; i++)
                {
                    if (!_fragmentsByOwner.TryGetValue(fragData[i].OwnerNetworkId, out var list))
                    {
                        list = new List<SoulFragment>();
                        _fragmentsByOwner[fragData[i].OwnerNetworkId] = list;
                    }
                    // Opacity stands in for time left: fragments about to fade are skipped by the brain.
                    float opacity = math.saturate(fragData[i].Opacity);
                    list.Add(new SoulFragment { Id = frags[i].Index, Position = ToVec(fragPos[i].Position), Lifetime = 5f, Age = 5f - opacity });
                    _fragmentVersions[frags[i].Index] = frags[i].Version;
                }
            }

            using (var players = _players.ToEntityArray(Allocator.Temp))
            using (var transforms = _players.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var vitals = _players.ToComponentDataArray<ArenaVitals>(Allocator.Temp))
            using (var buffs = _players.ToComponentDataArray<ArenaBuffs>(Allocator.Temp))
            using (var owners = _players.ToComponentDataArray<GhostOwner>(Allocator.Temp))
            {
                for (int i = 0; i < players.Length; i++)
                {
                    int id = owners[i].NetworkId;
                    var p = ToVec(transforms[i].Position + new float3(0f, ArenaWeapon.PlayerHitHeight, 0f));
                    var b = buffs[i];
                    var effects = new EffectSet(_combat.MaxBuffSlots);
                    if (b.ShieldRemaining > 0f) effects.Apply(EffectKind.Shield, 0, 1f, b.ShieldRemaining);
                    if (b.EmpowerRemaining > 0f) effects.Apply(EffectKind.Empower, 0, b.Empower, b.EmpowerRemaining);
                    if (b.HasteRemaining > 0f) effects.Apply(EffectKind.Hasted, 0, b.MoveMultiplier - 1f, b.HasteRemaining);
                    bool down = vitals[i].Health <= 0f;
                    _fragmentsByOwner.TryGetValue(id, out var frags);
                    _allies.Add(new MedicAlly
                    {
                        Id = id, Position = p, HealthFraction = vitals[i].Health / math.max(vitals[i].MaxHealth, 1f),
                        IsDown = down, Fragments = down ? frags : null, Buffs = effects,
                    });
                    _entityById[id] = players[i];
                    if (!down && Visible(eye, p))
                    {
                        _allyViews.Add(new AllyView { Entity = players[i], HoldsChain = b.ChainLinks > 0, HasteLeft = b.HasteRemaining, Position = p, });
                    }
                }
            }

            using (var enemies = _enemies.ToEntityArray(Allocator.Temp))
            using (var enemyPos = _enemies.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var enemyData = _enemies.ToComponentDataArray<ArenaEnemy>(Allocator.Temp))
            {
                for (int i = 0; i < enemies.Length; i++)
                {
                    var p = enemyPos[i].Position;
                    float d = math.distance(eye, p);
                    bool hittable = (enemyData[i].Hittable || enemyData[i].Family == EnemyFamily.Wisp) && d <= ArenaWeapon.Range &&
                                    Visible(eye, ToVec(p));
                    _enemyList.Add(new MedicEnemy { Id = _enemyList.Count, Position = ToVec(p), Hittable = hittable });
                    _enemyEntities.Add(enemies[i]);
                }
            }
        }

        private bool TryNearestAlly(int myId, float3 eye, System.Func<AllyView, bool> want, out Entity ally)
        {
            ally = Entity.Null;
            float best = AllyRange;
            foreach (var a in _allyViews)
            {
                if (_entityById.TryGetValue(myId, out var me) && a.Entity == me) continue;
                if (!want(a)) continue;
                float d = Vector3.Distance(ToVec(eye), a.Position);
                if (d >= best) continue;
                best = d;
                ally = a.Entity;
            }
            return ally != Entity.Null;
        }

        private Entity EnemyEntity(int index) => index >= 0 && index < _enemyEntities.Count ? _enemyEntities[index] : Entity.Null;

        private int FragmentVersion(int index) => _fragmentVersions.TryGetValue(index, out int v) ? v : 1;

        private void Pick(Entity target, bool friendly)
        {
            _target = target;
            _targetIsPlayer = friendly;
        }

        private static bool Visible(float3 eye, Vector3 point)
        {
            var p = new float3(point.X, point.Y, point.Z);
            float d = math.distance(eye, p);
            if (d < 0.01f) return true;
            return ArenaLayout.RaycastCover(eye, (p - eye) / d, d) == float.MaxValue;
        }

        private static Vector3 ToVec(float3 v) => new Vector3(v.x, v.y, v.z);
    }
}
