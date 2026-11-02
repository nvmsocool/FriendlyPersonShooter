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

        public static EnemyAttackSettings ForWisp() => new EnemyAttackSettings { Range = 20f, Damage = 6f };
        public static EnemyAttackSettings ForSkitter() => new EnemyAttackSettings { Range = 15f, Damage = 8f };
        public static EnemyAttackSettings ForMite() => new EnemyAttackSettings { Range = 2f, Damage = 2f };

        public static EnemyAttackSettings For(EnemyFamily family)
        {
            switch (family)
            {
                case EnemyFamily.Wisp: return ForWisp();
                case EnemyFamily.Skitter: return ForSkitter();
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
        /// <summary>The attack landed on the target; apply <see cref="AttackEvent.Damage"/>.</summary>
        Hit,
        /// <summary>The target left range (or went down) during the telegraph.</summary>
        Missed,
    }

    public struct AttackEvent
    {
        public AttackEventKind Kind;
        public int TargetId;
        public float Damage;
    }

    /// <summary>
    /// Generic telegraphed attack: cooldown (3-5 s), then pick the nearest player in range and
    /// telegraph (~0.8 s), then hit that player if they are still in range. Damage is moderate:
    /// the challenge is landing hits, not surviving surprises. A plain struct for ECS.
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
        public AttackEvent Step(float dt, Vector3 selfPosition, IReadOnlyList<AttackCandidate> players, EnemyAttackSettings s)
        {
            TimeLeft -= dt;
            if (TimeLeft > 0f) return default;

            if (Phase == AttackPhase.Cooldown)
            {
                int target = Nearest(selfPosition, players, s.Range);
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
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p.Id != TargetId) continue;
                inRange = p.CanBeAttacked && Vector3.DistanceSquared(p.Position, selfPosition) <= s.Range * s.Range;
                break;
            }
            return inRange
                ? new AttackEvent { Kind = AttackEventKind.Hit, TargetId = TargetId, Damage = s.Damage }
                : new AttackEvent { Kind = AttackEventKind.Missed, TargetId = TargetId };
        }

        /// <summary>Index of the nearest attackable player within range, or -1.</summary>
        public static int Nearest(Vector3 from, IReadOnlyList<AttackCandidate> players, float range)
        {
            int best = -1;
            float bestDist = range * range;
            for (int i = 0; i < players.Count; i++)
            {
                if (!players[i].CanBeAttacked) continue;
                float d = Vector3.DistanceSquared(players[i].Position, from);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }
    }
}
