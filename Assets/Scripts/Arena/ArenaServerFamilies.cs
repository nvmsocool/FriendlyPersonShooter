using FriendlyPersonShooter.Core.Combat;
using Unity.Entities;
using Vector3 = System.Numerics.Vector3;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The open-world families on the server: Sentinels (stand and snipe, armored but for the eye), Divers
    /// (circle high and dive), roaming Bulwarks (a smaller raid boss), difficulty tiers (one per layer) and
    /// elites (two families mixed).
    /// </summary>
    public partial class ArenaServerSystem
    {
        /// <summary>Per-tier settings, indexed by tier (1 to 3; index 0 is unused).</summary>
        private readonly WispSettings[] _wispTier = { null, EnemyTiers.Wisp(1), EnemyTiers.Wisp(2), EnemyTiers.Wisp(3) };
        private readonly SkitterSettings[] _skitterTier = { null, EnemyTiers.Skitter(1), EnemyTiers.Skitter(2), EnemyTiers.Skitter(3) };
        private readonly SentinelSettings[] _sentinelTier = { null, EnemyTiers.Sentinel(1), EnemyTiers.Sentinel(2), EnemyTiers.Sentinel(3) };
        private readonly DiverSettings[] _diverTier = { null, EnemyTiers.Diver(1), EnemyTiers.Diver(2), EnemyTiers.Diver(3) };
        private readonly BulwarkSettings[] _bulwarkWorldTier = { null, EnemyTiers.Bulwark(1), EnemyTiers.Bulwark(2), EnemyTiers.Bulwark(3) };
        private readonly MiteSwarmSettings[] _mitesTier = { null, EnemyTiers.Mites(1), EnemyTiers.Mites(2), EnemyTiers.Mites(3) };

        /// <summary>A Blinker elite blinks this often (seconds) and this far (metres).</summary>
        private const float BlinkerInterval = 2.2f;
        private const float BlinkerDistance = 4.5f;

        /// <summary>
        /// Debug: set to a tier (1 to 3) to spawn one of each open-world family and its elite in front of the first
        /// player. Set it from the Editor.
        /// </summary>
        public static int DebugSpawnFamilies;
        /// <summary>Debug: which way (degrees, 0 = +z) from the player the debug families appear.</summary>
        public static float DebugSpawnYaw;

        private void StepDebugFamilies()
        {
            if (DebugSpawnFamilies <= 0 || _players.Count == 0) return;
            int tier = DebugSpawnFamilies;
            DebugSpawnFamilies = 0;
            Unity.Entities.Entity first = default;
            foreach (var kv in _players) { first = kv.Key; break; }
            var feet = EntityManager.GetComponentData<Unity.Transforms.LocalTransform>(first).Position;
            float yaw = DebugSpawnYaw * (float)System.Math.PI / 180f;
            var f = new Vector3((float)System.Math.Sin(yaw), 0f, (float)System.Math.Cos(yaw));
            var r = new Vector3(f.Z, 0f, -f.X);
            var at = new Vector3(feet.x, 0f, feet.z) + f * 18f;
            Vector3 Spot(float side, float ahead, float lift)
            {
                var q = at + r * side + f * ahead;
                return new Vector3(q.X, SpawnGround(q.X, q.Z) + lift, q.Z);
            }
            SpawnSentinel(Spot(-8f, 0f, 0f), tier);
            SpawnSentinel(Spot(-8f, 10f, 0f), tier, EliteKind.Soaring);
            SpawnDiver(Spot(0f, 0f, 0f), tier);
            SpawnDiver(Spot(0f, 10f, 0f), tier, EliteKind.Phasing);
            SpawnWorldBulwark(Spot(8f, 0f, 0f), tier);
            SpawnWorldBulwark(Spot(8f, 12f, 0f), tier, EliteKind.Guarded);
            SpawnSkitter(Spot(3f, -4f, _skitter.BodyRadius), tier, EliteKind.Blinker);
            UnityEngine.Debug.Log($"[Families] spawned tier {tier} families near ({at.X:0},{at.Z:0})");
        }

        /// <summary>Tier-1 enemies keep the shared settings (arena and raid tuning reads those).</summary>
        private WispSettings WispOf(EnemyState s) => s.Tier <= 1 ? _wisp : _wispTier[EnemyTiers.Clamp(s.Tier)];
        private SkitterSettings SkitterOf(EnemyState s) => s.Tier <= 1 ? _skitter : _skitterTier[EnemyTiers.Clamp(s.Tier)];
        private SentinelSettings SentinelOf(EnemyState s) => _sentinelTier[EnemyTiers.Clamp(s.Tier)];
        private DiverSettings DiverOf(EnemyState s) => _diverTier[EnemyTiers.Clamp(s.Tier)];

        /// <summary>The raid boss uses the raid tuning; a roaming Bulwark its tier of the world tuning.</summary>
        private BulwarkSettings BulwarkOf(EnemyState s) => s.Roaming ? _bulwarkWorldTier[EnemyTiers.Clamp(s.Tier)] : _bulwark;

        private static bool IsRaidBoss(EnemyState s) => s.Family == EnemyFamily.Bulwark && s.Raid && !s.WorldBoss;

        private EnemyAttackSettings AttackFor(EnemyState s)
        {
            if (s.Family == EnemyFamily.Bulwark && s.Roaming) return EnemyAttackSettings.ForWorldBulwark();
            var a = EnemyAttackSettings.For(s.Family);
            // The Sentinel's wind-up is its eye opening, so a deeper tier's shorter window is a shorter wind-up.
            if (s.Family == EnemyFamily.Sentinel) a.TelegraphTime = SentinelOf(s).EyeOpenTime;
            return a;
        }

        private static float EnemyYaw(EnemyState s)
        {
            if (s.WorldBoss) return s.Colossus.Yaw;
            switch (s.Family)
            {
                case EnemyFamily.Sentinel: return s.Sentinel.Yaw;
                case EnemyFamily.Diver:
                {
                    var h = s.Diver.Heading;
                    return (float)(System.Math.Atan2(h.X, h.Z) * 180.0 / System.Math.PI);
                }
                default: return s.Boss.Yaw;
            }
        }

        /// <summary>Nose up (+) or down (-) in degrees; only Divers tilt.</summary>
        private static float EnemyPitch(EnemyState s)
        {
            if (s.Family != EnemyFamily.Diver) return 0f;
            var h = s.Diver.Heading;
            float flat = (float)System.Math.Sqrt(h.X * h.X + h.Z * h.Z);
            return (float)(System.Math.Atan2(h.Y, flat) * 180.0 / System.Math.PI);
        }

        /// <summary>A Blinker elite jumps a few metres sideways now and then. Returns true when it jumped.</summary>
        private bool BlinkSkitter(EnemyState s, float dt)
        {
            s.BlinkTime -= dt;
            if (s.BlinkTime > 0f) return false;
            s.BlinkTime += BlinkerInterval * EnemyTiers.Rhythm(s.Tier);
            float a = s.Skitter.Random.Angle();
            s.Skitter.Position += new Vector3((float)System.Math.Cos(a), 0f, (float)System.Math.Sin(a)) * BlinkerDistance;
            return true;
        }

        private Vector3 StepSentinel(EnemyState s, float dt)
        {
            Vector3? face = null;
            if (ExpeditionWorld.Active) NearCandidates(s.Sentinel.Position);
            int nearest = EnemyAttack.Nearest(s.Sentinel.Position, _candidates, s.AttackSettings.Range * 1.3f);
            if (nearest >= 0) face = _candidates[nearest].Position;
            s.Sentinel.Step(dt, s.Telegraph, false, face, SentinelOf(s), s.Vitals.MoveMultiplier);
            if (ExpeditionTerrain.Active && s.Sentinel.Soaring) KeepAboveGround(ref s.Sentinel.Position, 2f);
            return s.Sentinel.Position;
        }

        private Vector3 StepDiver(EntityCommandBuffer ecb, EnemyState s, float dt, out bool teleported)
        {
            var d = DiverOf(s);
            if (ExpeditionWorld.Active) NearCandidates(s.Diver.Position);
            var ev = s.Diver.Step(dt, _candidates, d, out teleported, SightTest, s.Vitals.MoveMultiplier);
            if (ev.Kind == DiverEventKind.Struck) HurtPlayer(ecb, ev.TargetId, EnemyFamily.Diver, d.Damage, s.Diver.Position, 0);
            // A dive into a hill stops at the ground and climbs back up.
            if (ExpeditionTerrain.Active)
            {
                var before = s.Diver.Position;
                KeepAboveGround(ref s.Diver.Position, 0.6f);
                if (s.Diver.Position.Y > before.Y) s.Diver.PullUp();
            }
            return s.Diver.Position;
        }

        private Vector3 StepRoamingBulwark(EnemyState s, float dt)
        {
            float fraction = s.Vitals.Health / System.Math.Max(1f, s.Vitals.MaxHealth);
            s.Boss.Step(dt, fraction, s.Vitals.MoveMultiplier, BulwarkOf(s));
            return s.Boss.Position;
        }

        private void SpawnSentinel(Vector3 home, int tier = 1, EliteKind elite = EliteKind.None)
        {
            var state = new EnemyState { Family = EnemyFamily.Sentinel, Tier = EnemyTiers.Clamp(tier), Elite = elite };
            state.Sentinel = SentinelBehavior.Create(home, NextSeed(), SentinelOf(state), elite == EliteKind.Soaring);
            SpawnEnemy(state, state.Sentinel.Position);
        }

        private void SpawnDiver(Vector3 home, int tier = 1, EliteKind elite = EliteKind.None)
        {
            var state = new EnemyState { Family = EnemyFamily.Diver, Tier = EnemyTiers.Clamp(tier), Elite = elite };
            state.Diver = DiverBehavior.Create(home, NextSeed(), DiverOf(state), elite == EliteKind.Phasing);
            SpawnEnemy(state, state.Diver.Position);
        }

        /// <summary>A Bulwark roaming the world; the Guarded elite brings a Mite swarm with it.</summary>
        private void SpawnWorldBulwark(Vector3 home, int tier = 1, EliteKind elite = EliteKind.None)
        {
            var state = new EnemyState { Family = EnemyFamily.Bulwark, Tier = EnemyTiers.Clamp(tier), Elite = elite, Roaming = true };
            var random = new CombatRandom(NextSeed() | 1u);
            state.Boss = BulwarkBehavior.Create(home, random.Range(0f, 360f), BulwarkOf(state));
            SpawnEnemy(state, state.Boss.Position);
            if (elite == EliteKind.Guarded) SpawnSwarm(home + new Vector3(0f, 2.5f, 0f), tier);
        }

        private HitTarget SentinelTarget(EnemyState s, HistorySample past, float grow)
        {
            var k = SentinelOf(s);
            var t = HitTarget.WithWeakPoint(s.Vitals.Id, past.Position, k.BodyRadius * grow,
                SentinelBehavior.EyeCenterOf(past.Position, s.Sentinel.Yaw, k), k.EyeRadius * grow);
            t.WeakPointOnSurface = true;
            return t;
        }

        private HitTarget DiverTarget(EnemyState s, HistorySample past, float grow)
        {
            var d = DiverOf(s);
            var b = s.Diver;
            b.Position = past.Position;
            var t = HitTarget.WithWeakPoint(s.Vitals.Id, past.Position, d.BodyRadius * grow, b.Wing(1, d), d.WingRadius * grow);
            t.HasSecondWeakPoint = true;
            t.SecondWeakPointCenter = b.Wing(-1, d);
            return t;
        }
    }
}
