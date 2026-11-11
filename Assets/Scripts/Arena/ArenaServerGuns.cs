using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Gallery;
using FriendlyPersonShooter.Core.Progression;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The guns half of the server: every player's gun (Core.Arsenal), firing each frame's
    /// way (hitscan, burst, beam, launcher shells that burst on landing), ricochet and self-share,
    /// loot boxes (one personal roll per player per box), items on the ground, and raid-clear loot.
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>A box roll or raid reward waits this long for its owner.</summary>
        private const float PersonalItemLife = 180f;
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
        private readonly LootBoxLedger _boxLedger = new LootBoxLedger();
        private IReadOnlyList<LootBox> _boxes;
        private int _boxesLayer;
        private readonly Dictionary<int, Entity> _connections = new Dictionary<int, Entity>();
        private int _boxesOpened, _itemsTaken, _blastCount, _ricochets, _reportedHits, _unreported, _ownShots;
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
        /// A new player's kit: a starting gallery of three guns (each with a payload mod, no frame or payload twice),
        /// with no time limit. They hold the first one until they shoot a card.
        /// </summary>
        private void EnsureKit(PlayerState s)
        {
            if (s.Loadout != null) return;
            var guns = LootRolls.StartingChoices(SeedHash.Of(_lootSeed, 0x6A1, s.Vitals.Id));
            var items = new LootItem[guns.Length];
            for (int i = 0; i < guns.Length; i++) items[i] = LootItem.Of(guns[i]);
            s.Loadout = new Loadout(guns[0]);
            Refit(s);
            OpenGallery(s, GalleryKind.Starter, 0f, 0f, true, items, SeedHash.Of(_lootSeed, 0x6A2, s.Vitals.Id));
        }

        /// <summary>After a loadout change: new stats; a new gun comes with a full capsule, a re-modded one keeps its charge.</summary>
        private void Refit(PlayerState s)
        {
            var gun = s.Loadout.InHand;
            ulong packed = gun.Pack();
            if (packed != s.Packed || (s.Stats == null && !gun.IsEmpty))
            {
                bool newGun = gun.Frame != Gun.Unpack(s.Packed).Frame;
                s.Stats = GunStats.For(gun);
                if (newGun) s.Gun = GunState.Full(s.Stats);
                else FireControl.Refit(ref s.Gun, s.Stats);
                s.Packed = packed;
            }
            // A Relay bullet hands on the gun's other payload (a Split gun's second), else Might.
            var gun0 = gun;
            var other = gun0.Payload == PayloadKind.Relay ? gun0.SecondPayload : gun0.Payload;
            s.Vitals.RelayCarry = other == PayloadKind.None || other == PayloadKind.Relay ? PayloadKind.Might : other;
        }

        private ArenaLoadout LoadoutGhost(PlayerState s)
        {
            var l = s.Loadout;
            var st = s.Gun;
            var ghost = new ArenaLoadout
            {
                Payload = l.InHand.Payload, Gun = l.InHand.Pack(),
                Charge = st.Charge, Capsule = (byte)st.Capsule, Grown = st.CapsuleGrown(_time),
            };
            return ghost;
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
                // Rewind everyone else to the tick this player was looking at when they fired.
                int delay = 0;
                if (SystemAPI.HasComponent<CommandDataInterpolationDelay>(entity))
                {
                    delay = (int)SystemAPI.GetComponent<CommandDataInterpolationDelay>(entity).Delay;
                }
                int seenTick = tick - delay;
                shooter.AimDelay = delay;
                shooter.LastYaw = input.ValueRO.Yaw;
                bool trigger = input.ValueRO.Fire != 0 && !shooter.Vitals.IsDown;
                // In a shooting gallery, shots go at its targets.
                if (shooter.Gallery.Open)
                {
                    GalleryInput(shooter, input.ValueRO, trigger, seenTick, ecb);
                    continue;
                }
                shooter.FireHeld = trigger;
                var stats = shooter.Stats;
                if (stats == null) continue;
                int ranks = shooter.Progress.PackedRanks;
                // Frenzy speeds fire and capsule regrowth; Overdrive (Haste + Frenzy) stops the payload running out.
                var step = FireControl.Step(ref shooter.Gun, stats, _time, trigger,
                    PlayerProgress.FireRateFor(ranks) * shooter.Vitals.FireRateMultiplier, shooter.Vitals.ReloadSpeedMultiplier,
                    shooter.Vitals.NoReload);
                int shots = step.Shots;
                if (shots <= 0) continue;
                _shots += shots;
                if (!shooter.BotKit) _usage.Shots(stats.Frame.Kind, shots);

                var eye = transform.ValueRO.Position + new float3(0f, ArenaSettings.EyeHeight, 0f);
                var aim = math.normalize(ArenaSettings.Forward(input.ValueRO.Yaw, input.ValueRO.Pitch));
                bool zoomed = input.ValueRO.Zoom != 0;
                float reach = stats.Range * PlayerProgress.RangeFor(ranks);
                BuildTargets(seenTick, shooter.Vitals.Id, eye, input.ValueRO.SeenFraction);
                var payloadFx = EffectsOf(stats);
                // Shots after the capsule runs dry are plain: damage, and the plain heal on allies.
                var plainFx = payloadFx;
                plainFx.Payload = PayloadKind.None;
                plainFx.Second = PayloadKind.None;
                int shooterId = owner.ValueRO.NetworkId;
                // Spread from a seed the player's game knows too, so its own hit test (LocalShots) fired the same shots.
                var rng = new CombatRandom(LocalShots.Seed(shooterId, tick));
                // Players decide their own hits; bots, and a tick the player didn't report, use the host's test.
                bool reported = !shooter.BotKit && input.ValueRO.ShotTick == tick;
                double seenTime = _time - GalleryClock.Seconds(delay);
                if (step.Launched) LaunchCapsule(shooter, shooterId, eye, input.ValueRO.Yaw, input.ValueRO.Pitch, payloadFx.Payload, ecb);

                for (int n = 0; n < shots; n++)
                {
                    var fx = n < step.Payloaded ? payloadFx : plainFx;
                    var d = FireControl.Spread(ToVec(aim), stats.SpreadFor(zoomed), ref rng);
                    var dir = ToFloat3(d);
                    float range = math.min(reach, ArenaLayout.RaycastCover(eye, dir, reach));
                    // Raid Color Locks: a crystal in front stops the shot.
                    bool ownTest = reported && n < LocalShots.Claims;
                    if (!shooter.BotKit) { if (ownTest) _ownShots++; else _unreported++; }
                    int claimKind = LocalShots.Miss, claimId = 0;
                    float claimDist = 0f;
                    if (ownTest)
                    {
                        // The player's own terrain and cover decided how far the shot went; the host only checks reach.
                        input.ValueRO.GetShot(n, out claimKind, out claimId, out claimDist);
                        range = math.min(math.max(0f, claimDist), reach);
                    }
                    int crystal = CrystalOnShot(shooter.Vitals.Id, eye, dir, range, out float crystalAt);
                    if (crystal >= 0) range = crystalAt;
                    // Loot boxes, items and reward chests are opened and taken by shooting them; the capsule is caught.
                    var obj = shooter.BotKit || _time < shooter.NextObjectShot ? default : WorldObjectOnShot(shooter, eye, dir, range, seenTime);
                    if (obj.Kind != ShotObjectKind.None) range = obj.At;
                    var end = eye + dir * range;
                    // A crystal or a world object in front of what the player hit takes the shot instead.
                    bool landed = ownTest
                        ? Reported(shooterId, claimKind, claimId, claimDist, range, eye, d, out var hit)
                        : HitScan.Cast(ToVec(eye), d, range, _targets, out hit, shooter.Vitals.Id);
                    if (landed) end = ToFloat3(hit.Point);
                    if (!landed && obj.Kind != ShotObjectKind.None)
                    {
                        SendShot(shooterId, eye - new float3(0f, 0.2f, 0f), end, fx.Payload, stats.Frame.Kind, ecb);
                        ShootObject(shooter, obj, input.ValueRO.Yaw, input.ValueRO.Pitch, ecb);
                        break;
                    }
                    else if (!landed && crystal >= 0)
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
                            ? FireControl.BeamMultiplier(ref shooter.Gun, stats, landed && !hit.IsPlayer ? hit.TargetId : -1, _time)
                            : 1f;
                        if (landed)
                        {
                            var confirm = new HitConfirm { Point = end };
                            float damage = stats.Damage * stats.FalloffAt(hit.Distance) * mult;
                            if (ApplyHit(shooter, stats, fx, hit.IsPlayer, hit.TargetId, hit.WeakPoint, damage, hit.Point, ref confirm, ecb) &&
                                hit.Index >= 0)
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

        /// <summary>
        /// A shot the player's own game reported (LocalShots) as a hit. The host takes their word for it, checking only
        /// that it's within the shot's reach (and in front of any crystal or world object) and isn't the shooter.
        /// Dead or missing targets are dropped later by ApplyHit.
        /// </summary>
        private bool Reported(int shooterId, int kind, int id, float distance, float range, float3 eye, Vector3 dir, out ScanHit hit)
        {
            hit = default;
            if (kind == LocalShots.Miss || distance > range + 0.05f) return false;
            bool player = kind == LocalShots.Player;
            if (player && id == shooterId) return false;
            if (kind == LocalShots.Fragment && id < FragmentIdBase) return false;
            hit = new ScanHit
            {
                Index = -1, TargetId = id, IsPlayer = player, Distance = distance, WeakPoint = kind == LocalShots.EnemyWeak,
                Point = ToVec(eye) + dir * distance,
            };
            _reportedHits++;
            return true;
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
                baseDamage *= enemy.Vitals.Armor(enemy.Colossus.DamageMultiplier(_colossus));
                weakPoint = weakPoint && enemy.Colossus.Exposed;
            }
            else if (enemy.Family == EnemyFamily.Sentinel)
            {
                // Armored all over but for its eye, and the eye only counts while it is open.
                enemy.Sentinel.HitOutcome(weakPoint, SentinelOf(enemy), out float armor, out weakPoint);
                baseDamage *= enemy.Vitals.Armor(armor);
            }
            else if (enemy.Family == EnemyFamily.Bulwark)
            {
                enemy.Boss.HitOutcome(weakPoint, BulwarkOf(enemy), out float armor, out weakPoint);
                // Brittle weakens the shell; Shatter breaks it.
                baseDamage *= enemy.Vitals.Armor(armor);
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
                ps.Gallery.Round = null;
                ps.Loadout = new Loadout(LootRolls.BotGun(payload, SeedHash.Of(_lootSeed, 0xB07, id)));
                Refit(ps);
            }

            if (ExpeditionWorld.Active && (_boxes == null || _boxesLayer != ExpeditionWorld.Layer))
            {
                _boxesLayer = ExpeditionWorld.Layer;
                _boxes = LootBoxSites.For(ExpeditionWorld.Map, _boxesLayer);
            }

            _itemScratch.Clear();
            foreach (var kv in _items) if (_time >= kv.Value.Expires) _itemScratch.Add(kv.Key);
            foreach (var e in _itemScratch)
            {
                _items.Remove(e);
                ecb.DestroyEntity(e);
            }
        }

        /// <summary>
        /// Equips an item (from the ground or a gallery): what it replaces drops at the player's feet.
        /// False if it can't be equipped.
        /// </summary>
        private bool Take(PlayerState state, LootItem item, float3 feet, EntityCommandBuffer ecb)
        {
            var result = state.Loadout.Equip(item);
            if (!result.Equipped) return false;
            _itemsTaken++;
            if (!state.BotKit) _usage.Took(item);
            Refit(state);
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

        /// <summary>
        /// Raid clear or world boss: three rare-or-better items for this player, in a reward chest that lands beside
        /// them once they're out of the raid (<see cref="PlaceChest"/>). Bots keep their role's kit.
        /// </summary>
        private void GiveBossLoot(int playerId)
        {
            if (!_playerById.TryGetValue(playerId, out var e) || !_players.TryGetValue(e, out var s) || s.BotKit) return;
            s.Rewards.Enqueue(LootRolls.BossChoices(SeedHash.Of(_lootSeed, 0xB055, playerId, (int)NextSeed())));
        }
    }
}
