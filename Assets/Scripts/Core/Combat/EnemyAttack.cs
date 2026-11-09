using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for one enemy's telegraphed attack. Metres, seconds, hit points.</summary>
    public sealed class EnemyAttackSettings
    {
        public float MinCooldown { get; set; } = 3f;
        public float MaxCooldown { get; set; } = 5f;
        /// <summary>Warning time (glow and sound) before the attack lands.</summary>
        public float TelegraphTime { get; set; } = 0.8f;
        public float Range { get; set; } = 15f;
        public float Damage { get; set; } = 8f;
        /// <summary>
        /// Speed of the bolt the attack fires (m/s), aimed where the target stood when the windup ended, so
        /// moving dodges it. 0 = a melee hit that lands at once (Mites).
        /// </summary>
        public float BoltSpeed { get; set; }
        /// <summary>Radius of the bolt; it hits a player whose body it passes this close to.</summary>
        public float BoltRadius { get; set; } = 0.3f;

        public bool Ranged => BoltSpeed > 0f;

        public static EnemyAttackSettings ForWisp() => new EnemyAttackSettings { Range = 20f, Damage = 6f, BoltSpeed = 16f, BoltRadius = 0.3f };
        public static EnemyAttackSettings ForSkitter() => new EnemyAttackSettings { Range = 15f, Damage = 8f, BoltSpeed = 18f, BoltRadius = 0.35f };
        public static EnemyAttackSettings ForMite() => new EnemyAttackSettings { Range = 2f, Damage = 2f };
        /// <summary>Glimmers never attack.</summary>
        public static EnemyAttackSettings ForGlimmer() => new EnemyAttackSettings { Range = 0f, Damage = 0f, MinCooldown = 1e6f, MaxCooldown = 1e6f };

        /// <summary>The raid boss: a long, slow wind-up, then a heavy bolt at anyone on the platform.</summary>
        public static EnemyAttackSettings ForBulwark() => new EnemyAttackSettings
        {
            Range = 60f, Damage = 18f, TelegraphTime = 1.2f, MinCooldown = 1.8f, MaxCooldown = 3f,
            BoltSpeed = 22f, BoltRadius = 0.7f,
        };

        /// <summary>The Sentinel: a long look (its eye is open the whole wind-up), then a fast bolt from far away.</summary>
        public static EnemyAttackSettings ForSentinel() => new EnemyAttackSettings
        {
            Range = 55f, Damage = 12f, TelegraphTime = new SentinelSettings().EyeOpenTime, MinCooldown = 3.2f, MaxCooldown = 4.8f,
            BoltSpeed = 30f, BoltRadius = 0.3f,
        };

        /// <summary>A Bulwark roaming the world: a slow, heavy bolt at short range.</summary>
        public static EnemyAttackSettings ForWorldBulwark() => new EnemyAttackSettings
        {
            Range = 25f, Damage = 12f, TelegraphTime = 1f, MinCooldown = 3f, MaxCooldown = 4.5f, BoltSpeed = 15f, BoltRadius = 0.5f,
        };

        /// <summary>Divers attack by diving (see <see cref="DiverBehavior"/>), not with this.</summary>
        public static EnemyAttackSettings ForDiver() => ForGlimmer();

        public static EnemyAttackSettings For(EnemyFamily family)
        {
            switch (family)
            {
                case EnemyFamily.Wisp: return ForWisp();
                case EnemyFamily.Sentinel: return ForSentinel();
                case EnemyFamily.Diver: return ForDiver();
                case EnemyFamily.Bulwark: return ForBulwark();
                case EnemyFamily.Skitter: return ForSkitter();
                case EnemyFamily.Glimmer: return ForGlimmer();
                default: return ForMite();
            }
        }
    }

    /// <summary>A player as an enemy sees it when choosing whom to attack.</summary>
    public struct AttackCandidate
    {
        public int Id;
        public Vector3 Position;
        /// <summary>Down players aren't attacked.</summary>
        public bool CanBeAttacked;

        public AttackCandidate(int id, Vector3 position, bool canBeAttacked = true)
        {
            Id = id;
            Position = position;
            CanBeAttacked = canBeAttacked;
        }
    }

    public enum AttackPhase : byte
    {
        /// <summary>Waiting before the next attack. Stays here at zero time while no player is in range.</summary>
        Cooldown = 0,
        /// <summary>Warning shown; the attack lands when it ends.</summary>
        Telegraph = 1,
    }

    public enum AttackEventKind : byte
    {
        None = 0,
        /// <summary>The telegraph began: show the glow and play the sound for <see cref="AttackEvent.TargetId"/>.</summary>
        TelegraphStarted,
        /// <summary>A melee attack landed on the target; apply <see cref="AttackEvent.Damage"/>.</summary>
        Hit,
        /// <summary>A ranged attack let go: fire a bolt at <see cref="AttackEvent.Aim"/> (it may still miss).</summary>
        Fired,
        /// <summary>The target left range (or went down) during the telegraph.</summary>
        Missed,
    }

    public struct AttackEvent
    {
        public AttackEventKind Kind;
        public int TargetId;
        public float Damage;
        /// <summary>Fired: where the target stood when the bolt left.</summary>
        public Vector3 Aim;
    }

    /// <summary>
    /// Generic telegraphed attack: cooldown (3-5 s), then pick the nearest player in range that the
    /// enemy can see and telegraph (~0.8 s). Then a ranged attack fires a bolt at where the target
    /// stands (dodge it by moving), and a melee attack hits them if still in range and in sight.
    /// Damage is moderate: the challenge is landing hits, not surviving surprises. A plain struct for ECS.
    /// </summary>
    public struct EnemyAttack
    {
        public AttackPhase Phase;
        public float TimeLeft;
        public int TargetId;
        public CombatRandom Random;

        public static EnemyAttack Create(uint seed, EnemyAttackSettings s)
        {
            var a = new EnemyAttack { Phase = AttackPhase.Cooldown, Random = new CombatRandom(seed) };
            a.TimeLeft = a.Random.Range(s.MinCooldown, s.MaxCooldown);
            return a;
        }

        /// <summary>Advances time and reports at most one event per step.</summary>
        /// <param name="canSee">Whether a straight line between two points is clear (terrain, plants); null = always.</param>
        public AttackEvent Step(float dt, Vector3 selfPosition, IReadOnlyList<AttackCandidate> players, EnemyAttackSettings s,
            System.Func<Vector3, Vector3, bool> canSee = null)
        {
            TimeLeft -= dt;
            if (TimeLeft > 0f) return default;

            if (Phase == AttackPhase.Cooldown)
            {
                int target = Nearest(selfPosition, players, s.Range, canSee);
                if (target < 0)
                {
                    TimeLeft = 0f;
                    return default;
                }
                Phase = AttackPhase.Telegraph;
                TargetId = players[target].Id;
                TimeLeft = s.TelegraphTime;
                return new AttackEvent { Kind = AttackEventKind.TelegraphStarted, TargetId = TargetId };
            }

            Phase = AttackPhase.Cooldown;
            TimeLeft = Random.Range(s.MinCooldown, s.MaxCooldown);
            bool inRange = false;
            Vector3 aim = default;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p.Id != TargetId) continue;
                aim = p.Position;
                if (s.Ranged)
                {
                    // A bolt goes where they stand now; it can still miss or hit cover.
                    inRange = p.CanBeAttacked;
                    break;
                }
                inRange = p.CanBeAttacked && Vector3.DistanceSquared(p.Position, selfPosition) <= s.Range * s.Range &&
                          (canSee == null || canSee(selfPosition, p.Position));
                break;
            }
            if (inRange && s.Ranged) return new AttackEvent { Kind = AttackEventKind.Fired, TargetId = TargetId, Damage = s.Damage, Aim = aim };
            return inRange
                ? new AttackEvent { Kind = AttackEventKind.Hit, TargetId = TargetId, Damage = s.Damage }
                : new AttackEvent { Kind = AttackEventKind.Missed, TargetId = TargetId };
        }

        /// <summary>Index of the nearest attackable player within range (and in sight, when <paramref name="canSee"/> is given), or -1.</summary>
        public static int Nearest(Vector3 from, IReadOnlyList<AttackCandidate> players, float range,
            System.Func<Vector3, Vector3, bool> canSee = null)
        {
            int best = -1;
            float bestDist = range * range;
            for (int i = 0; i < players.Count; i++)
            {
                if (!players[i].CanBeAttacked) continue;
                float d = Vector3.DistanceSquared(players[i].Position, from);
                if (d <= bestDist && (canSee == null || canSee(from, players[i].Position)))
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
