using System;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// An enemy's health and debuff slots. Wither deals its damage over time in <see cref="Tick"/>;
    /// Expose raises all damage the enemy takes, Wither included. Slow lowers its move and turn
    /// speed (the game scales its behavior's speeds by <see cref="MoveMultiplier"/>), and Tag makes
    /// hits on it splash to enemies nearby (see <see cref="HitResult.SplashDamage"/>).
    /// </summary>
    public sealed class EnemyVitals
    {
        private readonly float _slowFloor;

        public EnemyVitals(int id, EnemyFamily family, CombatSettings settings) : this(id, family, settings, settings.EnemyHealth(family))
        {
        }

        /// <summary>An enemy with its own max health (a raid boss scaled to the group).</summary>
        public EnemyVitals(int id, EnemyFamily family, CombatSettings settings, float maxHealth)
        {
            _slowFloor = settings.SlowFloor;
            Id = id;
            Family = family;
            MaxHealth = maxHealth;
            Health = MaxHealth;
            Debuffs = new EffectSet(settings.MaxDebuffSlots, settings.ExtraSourceShare, settings.StackCapMultiplier);
        }

        public int Id { get; }
        public EnemyFamily Family { get; }
        public float Health { get; private set; }
        public float MaxHealth { get; }
        public EffectSet Debuffs { get; }
        public bool IsDead => Health <= 0f;

        /// <summary>Sets health directly, within 0..max (a raid twin that comes back part-healed).</summary>
        public void SetHealth(float health) => Health = Math.Max(0f, Math.Min(MaxHealth, health));

        /// <summary>Damage multiplier from Expose (1 when not exposed).</summary>
        public float DamageTakenMultiplier => 1f + Debuffs.StrengthOf(EffectKind.Expose);

        /// <summary>Multiply the enemy's move speed by this (1 when not Slowed; never below the Slow floor).</summary>
        public float MoveMultiplier => Math.Max(_slowFloor, 1f - Debuffs.StrengthOf(EffectKind.Slow));

        /// <summary>Multiply the enemy's turn speed by this. Slow affects turning as much as moving.</summary>
        public float TurnMultiplier => MoveMultiplier;

        public bool IsTagged => Debuffs.Has(EffectKind.Tag);

        /// <summary>Share of each hit's damage that splashes to nearby enemies (0 when not Tagged).</summary>
        public float SplashShare => Debuffs.StrengthOf(EffectKind.Tag);

        /// <summary>True while the Plague combo (Wither + Tag) is active; see <see cref="Plague"/>.</summary>
        public bool IsPlagued => Debuffs.Has(EffectKind.Wither) && Debuffs.Has(EffectKind.Tag);

        /// <summary>Seconds until this enemy's Plague spreads again (managed by <see cref="Plague"/>).</summary>
        internal float PlagueTimer;

        /// <summary>Takes damage that is already fully scaled. Returns the health actually lost.</summary>
        public float TakeDamage(float amount)
        {
            if (amount <= 0f || IsDead) return 0f;
            float lost = Math.Min(amount, Health);
            Health -= lost;
            return lost;
        }

        /// <summary>
        /// Applies Wither damage for this step, then counts down debuffs. Returns the damage dealt.
        /// When Wither runs out mid-step, only the part of the step it covered deals damage.
        /// </summary>
        public float Tick(float dt)
        {
            float dealt = 0f;
            float dps = Debuffs.StrengthOf(EffectKind.Wither);
            if (dps > 0f && !IsDead)
            {
                float covered = Math.Min(dt, Debuffs.RemainingOf(EffectKind.Wither));
                dealt = TakeDamage(dps * covered * DamageTakenMultiplier);
            }
            Debuffs.Tick(dt);
            return dealt;
        }
    }
}
