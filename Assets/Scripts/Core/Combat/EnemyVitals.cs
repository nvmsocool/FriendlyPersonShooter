using System;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// An enemy's health and debuff slots. Wither deals its damage over time in <see cref="Tick"/>;
    /// Expose raises all damage the enemy takes, Wither included.
    /// </summary>
    public sealed class EnemyVitals
    {
        public EnemyVitals(int id, EnemyFamily family, CombatSettings settings)
        {
            Id = id;
            Family = family;
            MaxHealth = settings.EnemyHealth(family);
            Health = MaxHealth;
            Debuffs = new EffectSet(settings.MaxDebuffSlots, settings.ExtraSourceShare, settings.StackCapMultiplier);
        }

        public int Id { get; }
        public EnemyFamily Family { get; }
        public float Health { get; private set; }
        public float MaxHealth { get; }
        public EffectSet Debuffs { get; }
        public bool IsDead => Health <= 0f;

        /// <summary>Damage multiplier from Expose (1 when not exposed).</summary>
        public float DamageTakenMultiplier => 1f + Debuffs.StrengthOf(EffectKind.Expose);

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
