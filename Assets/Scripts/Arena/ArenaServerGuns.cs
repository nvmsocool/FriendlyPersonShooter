using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Progression;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The guns half of the server: every player's two found guns (Core.Arsenal), firing each frame's
    /// way (hitscan, burst, beam, launcher shells that burst on landing), ricochet and self-share,
    /// loot boxes (one personal roll per player per box), items on the ground, and raid-clear loot.
    /// </summary>
    public partial class ArenaServerSystem
    {
        private const float InteractRange = 2.5f;
        /// <summary>A box roll or raid reward waits this long for its owner.</summary>
        private const float PersonalItemLife = 180f;
        /// <summary>A box's pick closes when its player walks this far from where they opened it.</summary>
        private const float BoxPickRange = 6f;
        /// <summary>An item someone swapped out stays on the ground this long, for anyone.</summary>
        private const float DroppedItemLife = 120f;

        private sealed class Blast
        {
            public double At;
            public Vector3 Point;
            public Entity Shooter;
            public GunStats Stats;
            public ShotEffects Fx;
            public int DirectId;
        }

        private sealed class GroundItem
        {
            public LootItem Item;
            public int Owner;
            public double Expires;
            public float3 Position;
        }

        private readonly uint _lootSeed = (uint)System.Environment.TickCount | 1u;
        private readonly List<Blast> _blasts = new List<Blast>();
        private readonly Dictionary<Entity, GroundItem> _items = new Dictionary<Entity, GroundItem>();
        private readonly List<Entity> _itemScratch = new List<Entity>();
        private readonly List<(PlayerState State, float3 Feet)> _interacts = new List<(PlayerState, float3)>();
        private readonly LootBoxLedger _boxLedger = new LootBoxLedger();
        private IReadOnlyList<LootBox> _boxes;
        private readonly Dictionary<int, Entity> _connections = new Dictionary<int, Entity>();
        private int _boxesOpened, _itemsTaken, _blastCount, _ricochets;
        /// <summary>Playtest tuning numbers (humans only), logged as a [Guns] line.</summary>
        private readonly GunUsage _usage = new GunUsage();
        private double _nextGunLog = 30;

        /// <summary>Every 30 s: the [Guns] tuning line.</summary>
        private void LogGuns()
        {
            if (_time < _nextGunLog) return;
            _nextGunLog = _time + 30;
            UnityEngine.Debug.Log(_usage.Report());
        }

        // ---------- Loadouts ----------

        /// <summary>
        /// A new player's kit: a choice of starting guns (each with a payload mod, no frame or payload twice).
        /// They hold the first one until they pick (<see cref="PickStartingGun"/>).
        /// </summary>
        private void EnsureKit(PlayerState s)
        {
            if (s.Loadout != null) return;
            var guns = LootRolls.StartingChoices(SeedHash.Of(_lootSeed, 0x6A1, s.Vitals.Id));
            var items = new LootItem[guns.Length];
            for (int i = 0; i < guns.Length; i++) items[i] = LootItem.Of(guns[i]);
            OpenPick(s, PickKind.Start, items);
            s.Loadout = new Loadout(guns[0]);
            Refit(s);
        }

        private static void OpenPick(PlayerState s, PickKind kind, LootItem[] items)
        {
            s.Pick = kind;
            s.Choices = items;
            s.PickSerial++;
        }

        private static void ClosePick(PlayerState s)
        {
            s.Pick = PickKind.None;
            s.Choices = null;
            s.PickBox = -1;
        }

        /// <summary>The player answered their open pick: took one of the choices, or left a box for later.</summary>
        private void AnswerPick(PlayerState s, PickRequest request, EntityCommandBuffer ecb)
        {
            if (s.Pick == PickKind.None || s.Choices == null || request.Serial != s.PickSerial) return;
            if (request.Index == PickRequest.Leave)
            {
                if (s.Pick == PickKind.Box) ClosePick(s);
                return;
            }
            int index = request.Index;
            if (index >= s.Choices.Length) return;
            var item = s.Choices[index];
            if (s.Pick == PickKind.Start)
            {
                ClosePick(s);
                s.Loadout = new Loadout(item.Gun);
                Refit(s);
                UnityEngine.Debug.Log($"[Guns] player {s.Vitals.Id} picked {item.Gun.Frame} with {item.Gun.Payload}");
                return;
            }
            int box = s.PickBox;
            var at = s.PickAt;
            ClosePick(s);
            if (s.Vitals.IsDown || !_boxLedger.TryOpen(box, s.Vitals.Id)) return;
            _boxesOpened++;
            if (!s.BotKit) _usage.BoxOpened();
            if (!Take(s, item, at, ecb)) SpawnItem(item, s.Vitals.Id, ExpeditionWorld.Wrap(at + new float3(0f, 1.1f, 0f)), PersonalItemLife);
            if (_connections.TryGetValue(s.Vitals.Id, out var connection))
            {
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new LootBoxOpened { BoxId = box });
                ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        /// <summary>A box pick closes if its player goes down or walks away from the box.</summary>
        private void StepPicks()
        {
            foreach (var kv in _players)
            {
                var s = kv.Value;
                if (s.Pick != PickKind.Box) continue;
                var pos = new float3(s.Position.X, s.Position.Y, s.Position.Z);
                if (s.Vitals.IsDown || math.distancesq(ExpeditionWorld.Near(s.PickAt, pos), pos) > BoxPickRange * BoxPickRange) ClosePick(s);
            }
        }

        /// <summary>After a loadout change: new stats; a new gun comes loaded, a re-modded one keeps its ammo.</summary>
        private void Refit(PlayerState s)
        {
            for (int i = 0; i < 2; i++)
            {
                var gun = s.Loadout.Get(i);
                ulong packed = gun.Pack();
                if (packed == s.Packed[i] && (s.Stats[i] != null || gun.IsEmpty)) continue;
                bool newGun = gun.Frame != Gun.Unpack(s.Packed[i]).Frame;
                s.Stats[i] = GunStats.For(gun);
                if (newGun) s.Guns[i] = GunState.Full(s.Stats[i]);
                else FireControl.Refit(ref s.Guns[i], s.Stats[i]);
                s.Packed[i] = packed;
            }
            // A Relay bullet hands on the other gun's payload.
            var other = s.Loadout.Other.Payload;
            s.Vitals.RelayCarry = other == PayloadKind.None || other == PayloadKind.Relay ? PayloadKind.Might : other;
        }

        private ArenaLoadout LoadoutGhost(PlayerState s)
        {
            var l = s.Loadout;
            var st = s.Guns[l.Active];
            var ghost = new ArenaLoadout
            {
                Payload = l.InHand.Payload, GunA = l.GunA.Pack(), GunB = l.GunB.Pack(), Active = (byte)l.Active,
                Ammo = st.Ammo, Reload = st.ReloadProgress(_time), Pick = s.Pick, PickSerial = s.PickSerial,
            };
            if (s.Pick != PickKind.None && s.Choices != null)
            {
                ghost.Choice0 = s.Choices.Length > 0 ? s.Choices[0].Pack() : 0;
                ghost.Choice1 = s.Choices.Length > 1 ? s.Choices[1].Pack() : 0;
                ghost.Choice2 = s.Choices.Length > 2 ? s.Choices[2].Pack() : 0;
            }
            return ghost;
        }

        private void SwapGun(PlayerState s)
        {
            if (s.Loadout.Swap()) FireControl.SwappedIn(ref s.Guns[s.Loadout.Active], _time);
        }

        private ShotEffects EffectsOf(GunStats stats)
        {
            var fx = ShotEffects.Of(stats.Payload);
            fx.Second = stats.SecondPayload;
            fx.Strength = stats.PayloadStrength;
            fx.SecondStrength = stats.SecondStrength;
            fx.HealScale = stats.HealScale;
            fx.PlainHeal = _combat.PlainHeal * stats.HealScale;
            return fx;
        }

        // ---------- Shooting ----------

        private void Shoot(int tick, EntityCommandBuffer ecb)
        {
            _connections.Clear();
            foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithEntityAccess())
            {
                _connections[id.ValueRO.Value] = connection;
            }
            StepBlasts(tick, ecb);

            foreach (var (input, transform, owner, entity) in SystemAPI
                         .Query<RefRO<ArenaInput>, RefRO<LocalTransform>, RefRO<GhostOwner>>()
                         .WithAll<ArenaPlayer>().WithEntityAccess())
            {
                if (!_players.TryGetValue(entity, out var shooter) || shooter.Loadout == null) continue;
                int slot = shooter.Loadout.Active;
                var stats = shooter.Stats[slot];
                if (stats == null) continue;
                int ranks = shooter.Progress.PackedRanks;
                bool trigger = input.ValueRO.Fire != 0 && !shooter.Vitals.IsDown;
                int shots = FireControl.Step(ref shooter.Guns[slot], stats, _time, trigger, input.ValueRO.Reload.IsSet,
                    PlayerProgress.FireRateFor(ranks));
                if (shots <= 0) continue;
                _shots += shots;
                if (!shooter.BotKit) _usage.Shots(stats.Frame.Kind, shots);

                // Rewind everyone else to the tick this player was looking at when they fired.
                int delay = 0;
                if (SystemAPI.HasComponent<CommandDataInterpolationDelay>(entity))
                {
                    delay = (int)SystemAPI.GetComponent<CommandDataInterpolationDelay>(entity).Delay;
                }
                int seenTick = tick - delay;

                var eye = transform.ValueRO.Position + new float3(0f, ArenaSettings.EyeHeight, 0f);
                var aim = math.normalize(ArenaSettings.Forward(input.ValueRO.Yaw, input.ValueRO.Pitch));
                bool zoomed = input.ValueRO.Zoom != 0;
                float reach = stats.Range * PlayerProgress.RangeFor(ranks);
                BuildTargets(seenTick, shooter.Vitals.Id, eye);
                var fx = EffectsOf(stats);
                var rng = new CombatRandom(NextSeed() | 1u);
                int shooterId = owner.ValueRO.NetworkId;

                for (int n = 0; n < shots; n++)
                {
                    var d = FireControl.Spread(ToVec(aim), stats.SpreadFor(zoomed), ref rng);
                    var dir = ToFloat3(d);
                    float range = math.min(reach, ArenaLayout.RaycastCover(eye, dir, reach));
                    // Raid Color Locks: a crystal in front stops the shot.
                    int crystal = CrystalOnShot(shooter.Vitals.Id, eye, dir, range, out float crystalAt);
                    if (crystal >= 0) range = crystalAt;
                    var end = eye + dir * range;
                    bool landed = HitScan.Cast(ToVec(eye), d, range, _targets, out var hit, shooter.Vitals.Id);
                    if (landed) end = ToFloat3(hit.Point);
                    else if (crystal >= 0)
                    {
                        int marker = CrystalHit(crystal, shooter.Vitals.Id);
                        if (marker != 0)
                        {
                            Confirm(shooterId, new HitConfirm
                            {
                                Point = end, WeakPoint = (byte)(marker == 1 ? 1 : 0), Refused = (byte)(marker == 2 ? 1 : 0),
                                TargetIsPlayer = (byte)(marker == 2 ? 1 : 0),
                            }, ecb);
                        }
                    }

                    if (stats.Mode == FireMode.Projectile)
                    {
                        // The shell flies at the frame's speed and bursts where the line stopped.
                        float travel = math.distance(eye, end) / math.max(1f, stats.Frame.ProjectileSpeed);
                        _blasts.Add(new Blast
                        {
                            At = _time + travel, Point = ToVec(end), Shooter = entity, Stats = stats, Fx = fx,
                            DirectId = landed ? hit.TargetId : int.MinValue,
                        });
                    }
                    else
                    {
                        float mult = stats.Mode == FireMode.Beam
                            ? FireControl.BeamMultiplier(ref shooter.Guns[slot], stats, landed && !hit.IsPlayer ? hit.TargetId : -1, _time)
                            : 1f;
                        if (landed)
                        {
                            var confirm = new HitConfirm { Point = end };
                            float damage = stats.Damage * stats.FalloffAt(hit.Distance) * mult;
                            if (ApplyHit(shooter, stats, fx, hit.IsPlayer, hit.TargetId, hit.WeakPoint, damage, hit.Point, ref confirm, ecb))
                            {
                                var t = _targets[hit.Index];
                                t.Hittable = false;
                                _targets[hit.Index] = t;
                            }
                            QueueCombos();
                            Confirm(shooterId, confirm, ecb);
                        }
                    }
                    SendShot(shooterId, eye - new float3(0f, 0.2f, 0f), end, fx.Payload, stats.Frame.Kind, ecb);
                }
            }
        }

        /// <summary>Launcher shells landing: everything within the burst is hit (enemies take less further out).</summary>
        private void StepBlasts(int tick, EntityCommandBuffer ecb)
        {
            for (int i = _blasts.Count - 1; i >= 0; i--)
            {
                var b = _blasts[i];
                if (_time < b.At) continue;
                _blasts.RemoveAt(i);
                if (!_players.TryGetValue(b.Shooter, out var shooter)) continue;
                _blastCount++;
                BuildTargets(tick, shooter.Vitals.Id, ToFloat3(b.Point));
                float radius = b.Stats.Frame.SplashRadius;
                var confirm = new HitConfirm { Point = ToFloat3(b.Point) };
                bool any = false;
                for (int t = 0; t < _targets.Count; t++)
                {
                    var target = _targets[t];
                    if (!target.Hittable) continue;
                    float d = Vector3.Distance(target.BodyCenter, b.Point) - target.BodyRadius;
                    if (d > radius) continue;
                    bool direct = target.Id == b.DirectId;
                    float share = direct ? 1f : b.Stats.Frame.SplashShare * (1f - 0.5f * math.saturate(d / radius));
                    any = true;
                    if (ApplyHit(shooter, b.Stats, b.Fx, target.IsPlayer, target.Id, false, b.Stats.Damage * share, target.BodyCenter, ref confirm, ecb))
                    {
                        target.Hittable = false;
                        _targets[t] = target;
                    }
                }
                QueueCombos();
                if (any) Confirm(shooter.Vitals.Id, confirm, ecb);
            }
        }

        /// <summary>
        /// One bullet (or burst) landing on a target. Totals add into <paramref name="confirm"/>.
        /// Returns true if it killed an enemy (so later bullets this tick pass through).
        /// </summary>
        private bool ApplyHit(PlayerState shooter, GunStats stats, ShotEffects fx, bool isPlayer, int targetId, bool weakPoint,
            float damage, Vector3 point, ref HitConfirm confirm, EntityCommandBuffer ecb)
        {
            if (isPlayer)
            {
                if (!_playerById.TryGetValue(targetId, out var te) || !_players.TryGetValue(te, out var target)) return false;
                var result = _lobby.HitPlayer(shooter.Vitals, target.Vitals, fx, _time, _combos, stats.Conductor);
                // A plain bullet only patches a scratch: no XP, no kudos.
                bool helped = result.Refused == RefuseReason.None && (fx.HasPayload || result.Relayed);
                if (helped)
                {
                    LanternLit(target.Vitals.Id);
                    SocialBuff(shooter.Vitals.Id, target.Vitals.Id, result);
                    AddXp(shooter.Vitals.Id, XpRewards.AllyBuff);
                    _ledger?.RecordBuff(shooter.Vitals.Id, target.Vitals.Id, _time);
                }
                _allyHits++;
                float given = result.Healed + result.ShieldGained;
                if (helped && stats.SelfShare)
                {
                    var self = new HitResult();
                    _lobby.Hits.GiveSelf(shooter.Vitals, fx.Payload, GunMods.SelfShare, fx.HealScale, ref self);
                    _lobby.Hits.GiveSelf(shooter.Vitals, fx.Second, GunMods.SelfShare, fx.HealScale, ref self);
                    given += self.Healed + self.ShieldGained;
                }
                if (helped && stats.Ricochet) given += Ricochet(shooter, target, stats, fx, point, ecb);
                _healing += given;
                if (!shooter.BotKit) _usage.AllyHit(stats.Frame.Kind, given);
                confirm.TargetIsPlayer = 1;
                confirm.Refused = (byte)(result.Refused != RefuseReason.None ? 1 : 0);
                confirm.Healed += given;
                bool chained = result.ChainEvent == ChainEvent.Started || result.ChainEvent == ChainEvent.Grew;
                if (chained) confirm.ChainLinks = (byte)result.ChainLinks;
                return false;
            }

            if (targetId >= FragmentIdBase)
            {
                int ownerId = (targetId - FragmentIdBase) / FragmentIdsPerPlayer;
                int fragmentId = (targetId - FragmentIdBase) % FragmentIdsPerPlayer;
                confirm.Fragment = 1;
                confirm.TargetIsPlayer = 1;
                if (_playerById.TryGetValue(ownerId, out var ownerEntity) && _players.TryGetValue(ownerEntity, out var downed) &&
                    downed.Revive != null)
                {
                    var revive = downed.Revive;
                    var r = _lobby.HitFragment(shooter.Vitals, revive, fragmentId, fx.Payload);
                    confirm.Refused = (byte)(r.Accepted ? 0 : 1);
                    confirm.ReviveProgress = revive.ProgressFraction;
                    if (r.Accepted)
                    {
                        _fragmentHits++;
                        SocialFragment(shooter.Vitals.Id, ownerId, revive, r.Revived);
                    }
                    if (r.Revived)
                    {
                        _revives++;
                        downed.Revive = null;
                        AddXp(shooter.Vitals.Id, XpRewards.Revive);
                        _announce.Add((AnnounceKind.Revived, 0, ownerId, shooter.Vitals.Id));
                    }
                }
                else confirm.Refused = 1;
                return false;
            }

            if (!_enemyById.TryGetValue(targetId, out var enemyEntity) || !_enemies.TryGetValue(enemyEntity, out var enemy) ||
                enemy.Vitals.IsDead)
            {
                return false;
            }
            // Lanterns: a shade out of the light can't be hurt.
            if (ShadeInDark(enemyEntity, enemy))
            {
                confirm.Refused = 1;
                return false;
            }
            // The raid boss's shell shrugs off most damage; only its open plate takes full hits.
            float baseDamage = damage;
            if (enemy.Family == EnemyFamily.Bulwark && enemy.WorldBoss)
            {
                // The Colossus: armored everywhere until a slam stuns it; then its core is a weak point.
                baseDamage *= enemy.Colossus.DamageMultiplier(_colossus);
                weakPoint = weakPoint && enemy.Colossus.Exposed;
            }
            else if (enemy.Family == EnemyFamily.Bulwark)
            {
                enemy.Boss.HitOutcome(weakPoint, _bulwark, out float armor, out weakPoint);
                baseDamage *= armor;
                // The Weaver's threads shield it.
                if (enemy.Raid && _run?.Weaver != null) baseDamage *= _run.Weaver.DamageMultiplier;
            }
            var hitResult = _lobby.HitEnemy(shooter.Vitals, enemy.Vitals, fx, baseDamage, weakPoint, _time, _combos);
            _ledger?.RecordHit(enemy.Vitals.Id, shooter.Vitals.Id, _time);
            _enemyHits++;
            if (hitResult.WeakPoint) _weakHits++;
            _damageDealt += hitResult.DamageDealt;
            if (!shooter.BotKit) _usage.EnemyHit(stats.Frame.Kind, hitResult.DamageDealt, hitResult.Killed);
            if (hitResult.ChainEvent == ChainEvent.Cashed && hitResult.ChainLinks >= 2)
            {
                _chainsCashed++;
                _chainLinksCashed += hitResult.ChainLinks;
                _chainDamage += hitResult.DamageDealt * hitResult.ChainBonus / (1f + hitResult.ChainBonus);
                confirm.ChainCashed = 1;
                confirm.ChainLinks = (byte)hitResult.ChainLinks;
                confirm.ChainBonus = hitResult.ChainBonus;
                _announce.Add((AnnounceKind.ChainCashed, hitResult.ChainLinks, shooter.Vitals.Id, 0));
            }
            if (hitResult.SplashDamage > 0f) Splash(enemy, targetId, hitResult.SplashDamage, ecb);
            if (hitResult.Killed)
            {
                KillEnemy(enemyEntity, ecb);
                _kills++;
            }
            if (hitResult.WeakPoint) confirm.WeakPoint = 1;
            if (hitResult.Killed) confirm.Killed = 1;
            confirm.Damage += hitResult.DamageDealt;
            return hitResult.Killed;
        }

        /// <summary>Ricochet: a helpful hit bounces on to the nearest other ally close to the first.</summary>
        private float Ricochet(PlayerState shooter, PlayerState first, GunStats stats, ShotEffects fx, Vector3 from, EntityCommandBuffer ecb)
        {
            PlayerState best = null;
            Vector3 bestCenter = default;
            float bestD = GunMods.RicochetRange * GunMods.RicochetRange;
            foreach (var p in _players.Values)
            {
                if (p == shooter || p == first || p.Vitals.IsDown) continue;
                var center = NearVec(p.Position, ToFloat3(from)) + new Vector3(0f, ArenaWeapon.PlayerHitHeight, 0f);
                float d = Vector3.DistanceSquared(center, from);
                if (d >= bestD) continue;
                bestD = d;
                best = p;
                bestCenter = center;
            }
            if (best == null) return 0f;
            var r = _lobby.HitPlayer(shooter.Vitals, best.Vitals, fx, _time, _combos, stats.Conductor);
            if (r.Refused != RefuseReason.None) return 0f;
            _ricochets++;
            LanternLit(best.Vitals.Id);
            SocialBuff(shooter.Vitals.Id, best.Vitals.Id, r);
            _ledger?.RecordBuff(shooter.Vitals.Id, best.Vitals.Id, _time);
            SendShot(0, ToFloat3(from), ToFloat3(bestCenter), fx.Payload, stats.Frame.Kind, ecb);
            return r.Healed + r.ShieldGained;
        }

        private void Confirm(int playerId, HitConfirm confirm, EntityCommandBuffer ecb)
        {
            if (!_connections.TryGetValue(playerId, out var connection)) return;
            var rpc = ecb.CreateEntity();
            ecb.AddComponent(rpc, confirm);
            ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }

        /// <summary>Everyone else draws the tracer; the shooter already drew their own (ricochets, id 0, are drawn by all).</summary>
        private static void SendShot(int shooterId, float3 from, float3 to, PayloadKind payload, FrameKind frame, EntityCommandBuffer ecb)
        {
            var fx = ecb.CreateEntity();
            ecb.AddComponent(fx, new ShotFx { ShooterNetworkId = shooterId, From = from, To = to, Payload = payload, Frame = (byte)frame });
            ecb.AddComponent(fx, new SendRpcCommandRequest { TargetConnection = Entity.Null });
        }

        // ---------- Loot ----------

        private void StepLoot(EntityCommandBuffer ecb)
        {
            foreach (var (request, receive, rpc) in SystemAPI.Query<RefRO<BotKitRequest>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                ecb.DestroyEntity(rpc);
                if (!SystemAPI.HasComponent<NetworkId>(receive.ValueRO.SourceConnection)) continue;
                int id = SystemAPI.GetComponent<NetworkId>(receive.ValueRO.SourceConnection).Value;
                var payload = request.ValueRO.Payload;
                if (payload == PayloadKind.None || !_playerById.TryGetValue(id, out var pe) || !_players.TryGetValue(pe, out var ps) ||
                    ps.BotKit)
                {
                    continue;
                }
                ps.BotKit = true;
                ClosePick(ps);
                ps.Loadout = new Loadout(LootRolls.BotGun(payload, SeedHash.Of(_lootSeed, 0xB07, id)));
                Refit(ps);
            }

            foreach (var (request, receive, rpc) in SystemAPI.Query<RefRO<PickRequest>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                ecb.DestroyEntity(rpc);
                if (!SystemAPI.HasComponent<NetworkId>(receive.ValueRO.SourceConnection)) continue;
                int id = SystemAPI.GetComponent<NetworkId>(receive.ValueRO.SourceConnection).Value;
                if (_playerById.TryGetValue(id, out var pe) && _players.TryGetValue(pe, out var ps)) AnswerPick(ps, request.ValueRO, ecb);
            }
            StepPicks();

            if (ExpeditionWorld.Active && _boxes == null) _boxes = LootBoxSites.For(ExpeditionWorld.Map);

            _itemScratch.Clear();
            foreach (var kv in _items) if (_time >= kv.Value.Expires) _itemScratch.Add(kv.Key);
            foreach (var e in _itemScratch)
            {
                _items.Remove(e);
                ecb.DestroyEntity(e);
            }

            // Collected first: taking or opening spawns items, which can't happen mid-query.
            _interacts.Clear();
            foreach (var (input, transform, entity) in SystemAPI.Query<RefRO<ArenaInput>, RefRO<LocalTransform>>()
                         .WithAll<ArenaPlayer>().WithEntityAccess())
            {
                if (!input.ValueRO.Interact.IsSet || !_players.TryGetValue(entity, out var state)) continue;
                if (state.Loadout == null || state.Vitals.IsDown) continue;
                _interacts.Add((state, transform.ValueRO.Position));
            }
            foreach (var (state, feet) in _interacts) Interact(state, feet, ecb);
        }

        /// <summary>
        /// E: take the nearest item you may take; failing that, start the raid countdown if you're at the door
        /// with a full key; failing that, open the nearest box you haven't opened (its pick of three).
        /// E again while a box's pick is open leaves it for later.
        /// </summary>
        private void Interact(PlayerState state, float3 feet, EntityCommandBuffer ecb)
        {
            int id = state.Vitals.Id;
            if (state.Pick == PickKind.Box)
            {
                ClosePick(state);
                return;
            }
            Entity bestItem = Entity.Null;
            float bestD = InteractRange * InteractRange;
            foreach (var kv in _items)
            {
                if (kv.Value.Owner != 0 && kv.Value.Owner != id) continue;
                float d = math.distancesq(ExpeditionWorld.Near(kv.Value.Position, feet), feet + new float3(0f, 0.8f, 0f));
                if (d >= bestD) continue;
                bestD = d;
                bestItem = kv.Key;
            }
            if (bestItem != Entity.Null)
            {
                if (!Take(state, _items[bestItem].Item, feet, ecb)) return;
                _items.Remove(bestItem);
                ecb.DestroyEntity(bestItem);
                return;
            }

            if (TryStartRaid(id, feet)) return;
            if (_boxes == null) return;
            int box = -1;
            bestD = InteractRange * InteractRange;
            foreach (var b in _boxes)
            {
                if (_boxLedger.HasOpened(b.Id, id)) continue;
                var p = ExpeditionWorld.Near(BoxPosition(b), feet);
                float d = math.distancesq(p.xz, feet.xz);
                if (d >= bestD || math.abs(p.y - feet.y) > 2.5f) continue;
                bestD = d;
                box = b.Id;
            }
            if (box < 0 || state.Pick != PickKind.None) return;
            // The box opens for good when one is picked (AnswerPick); until then it can be left and come back to,
            // with the same three on offer.
            OpenPick(state, PickKind.Box, LootRolls.BoxChoices(LootBoxLedger.RollSeed(_lootSeed, box, id)));
            state.PickBox = box;
            state.PickAt = feet;
        }

        /// <summary>
        /// Equips an item (from the ground or a box pick): what it replaces drops at the player's feet.
        /// False if it can't be equipped. Taking loot settles the kit, so a starting pick still open is gone.
        /// </summary>
        private bool Take(PlayerState state, LootItem item, float3 feet, EntityCommandBuffer ecb)
        {
            int before = state.Loadout.Active;
            var result = state.Loadout.Equip(item);
            if (!result.Equipped) return false;
            if (state.Pick == PickKind.Start) ClosePick(state);
            _itemsTaken++;
            if (!state.BotKit) _usage.Took(item);
            Refit(state);
            if (state.Loadout.Active != before) FireControl.SwappedIn(ref state.Guns[state.Loadout.Active], _time);
            var at = feet + new float3(0f, 0.6f, 0f);
            if (!result.Dropped.IsNone) SpawnItem(result.Dropped, 0, at, DroppedItemLife);
            if (!result.Dropped2.IsNone) SpawnItem(result.Dropped2, 0, at + new float3(0.5f, 0f, 0f), DroppedItemLife);
            return true;
        }

        /// <summary>Where a box stands: on the ground at its spot (the client draws it at the same place).</summary>
        public static float3 BoxPosition(LootBox b) => new float3(b.World.X, ExpeditionTerrain.GroundY(b.World.X, b.World.Y), b.World.Y);

        private void SpawnItem(LootItem item, int owner, float3 position, float life)
        {
            var prefab = SystemAPI.GetSingleton<ArenaGroundItemPrefab>().Value;
            var e = EntityManager.Instantiate(prefab);
            EntityManager.SetComponentData(e, LocalTransform.FromPosition(position));
            EntityManager.SetComponentData(e, new ArenaGroundItem { Item = item.Pack(), Owner = owner });
            _items[e] = new GroundItem { Item = item, Owner = owner, Expires = _time + life, Position = position };
        }

        /// <summary>Raid clear: a rare-or-better item for this raider, at their feet.</summary>
        private void GiveBossLoot(int playerId, float3 at, EntityCommandBuffer ecb)
        {
            var item = LootRolls.Roll(LootSource.Boss, SeedHash.Of(_lootSeed, 0xB055, playerId, (int)NextSeed()));
            SpawnItem(item, playerId, at + new float3(1.2f, 0.6f, 0f), PersonalItemLife);
        }
    }
}
