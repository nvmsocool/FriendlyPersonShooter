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
    /// enemies and players a human would. The first bot carries Vigor and heals hurt allies; the others
    /// carry Might and shoot enemies (and buff allies now and then). All of them dodge with ADAD
    /// strafing and jumps, so healing them is a tracking drill. Aim uses the Core.Combat aim model,
    /// with strong and weak aimers mixed.
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ArenaBotSystem : SystemBase
    {
        private const float RetargetInterval = 0.5f;
        private const float HealBelow = 0.8f;
        private const float HealRange = 35f;
        private const float BuffChance = 0.25f;

        private BotAim _aim;
        private BotMover _mover;
        private readonly BotMoveSettings _moveSettings = new BotMoveSettings { MinDistance = 6f, MaxDistance = 14f };
        private int _seat = -1;
        private PayloadKind _wanted;
        private float _retargetIn;
        private float _swapCooldown;
        private Entity _target;
        private bool _targetIsPlayer;
        private Unity.Mathematics.Random _random;
        private EntityQuery _local, _players, _enemies;

        protected override void OnCreate()
        {
            RequireForUpdate<ArenaBotSeat>();
            RequireForUpdate<ArenaPlayer>();
            _local = GetEntityQuery(ComponentType.ReadOnly<ArenaPlayer>(), ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<ArenaVitals>(), ComponentType.ReadOnly<ArenaLoadout>());
            _players = GetEntityQuery(ComponentType.ReadOnly<ArenaPlayer>(), ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<ArenaVitals>());
            _enemies = GetEntityQuery(ComponentType.ReadOnly<ArenaEnemy>(), ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnUpdate()
        {
            if (_seat < 0)
            {
                _seat = SystemAPI.GetSingleton<ArenaBotSeat>().Index;
                uint seed = (uint)(_seat * 7919 + 101);
                _aim = new BotAim(_seat % 2 == 0 ? BotAimSettings.Strong() : BotAimSettings.Weak(), seed);
                _mover = BotMover.Create(seed + 1);
                _wanted = _seat == 0 ? PayloadKind.Vigor : PayloadKind.Might;
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
                _swapCooldown = 0.5f;
            }

            _retargetIn -= dt;
            if (_retargetIn <= 0f || !EntityManager.Exists(_target))
            {
                _retargetIn = RetargetInterval;
                ChooseTarget(me, pos, payload);
            }

            var eye = pos + new float3(0f, ArenaSettings.EyeHeight, 0f);
            float3 anchor = float3.zero;
            if (_target != Entity.Null && EntityManager.Exists(_target))
            {
                var tpos = EntityManager.GetComponentData<LocalTransform>(_target).Position;
                float radius;
                float3 aimPoint;
                if (_targetIsPlayer)
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
                anchor = _targetIsPlayer ? tpos : tpos;
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

        /// <summary>
        /// A Vigor bot heals the most hurt ally in range. Otherwise shoot the nearest visible enemy,
        /// sometimes buffing a nearby ally instead.
        /// </summary>
        private void ChooseTarget(Entity me, float3 pos, PayloadKind payload)
        {
            _target = Entity.Null;
            var eye = pos + new float3(0f, ArenaSettings.EyeHeight, 0f);

            using var players = _players.ToEntityArray(Allocator.Temp);
            using var playerPos = _players.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var playerVitals = _players.ToComponentDataArray<ArenaVitals>(Allocator.Temp);
            float worst = 2f;
            Entity hurt = Entity.Null, nearAlly = Entity.Null;
            float nearAllyDist = float.MaxValue;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == me || playerVitals[i].Health <= 0f) continue;
                float d = math.distance(pos, playerPos[i].Position);
                if (d > HealRange) continue;
                float frac = playerVitals[i].Health / math.max(playerVitals[i].MaxHealth, 1f);
                if (frac < worst)
                {
                    worst = frac;
                    hurt = players[i];
                }
                if (d < nearAllyDist)
                {
                    nearAllyDist = d;
                    nearAlly = players[i];
                }
            }
            if (payload == PayloadKind.Vigor && hurt != Entity.Null && worst < HealBelow)
            {
                _target = hurt;
                _targetIsPlayer = true;
                return;
            }
            if (payload == PayloadKind.Might && nearAlly != Entity.Null && _random.NextFloat() < BuffChance)
            {
                _target = nearAlly;
                _targetIsPlayer = true;
                return;
            }

            using var enemies = _enemies.ToEntityArray(Allocator.Temp);
            using var enemyPos = _enemies.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var enemyData = _enemies.ToComponentDataArray<ArenaEnemy>(Allocator.Temp);
            float best = float.MaxValue;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemyData[i].Hittable && enemyData[i].Family != EnemyFamily.Wisp) continue;
                var p = enemyPos[i].Position;
                float d = math.distance(eye, p);
                if (d > ArenaWeapon.Range) continue;
                if (ArenaLayout.RaycastCover(eye, math.normalize(p - eye), d) != float.MaxValue) continue;
                if (d < best)
                {
                    best = d;
                    _target = enemies[i];
                    _targetIsPlayer = false;
                }
            }
        }

        private static Vector3 ToVec(float3 v) => new Vector3(v.x, v.y, v.z);
    }
}
