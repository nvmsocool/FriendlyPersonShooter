namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// Tunable numbers for payloads, effect slots and health. Time is in seconds, health and
    /// damage in hit points, bonuses as fractions (0.25 = +25%).
    /// </summary>
    public sealed class CombatSettings
    {
        // Vigor.
        /// <summary>Health restored by one Vigor hit on an ally.</summary>
        public float VigorHeal { get; set; } = 12f;
        /// <summary>Most shield a player can hold from Vigor overflow.</summary>
        public float ShieldCap { get; set; } = 25f;
        public float ShieldDuration { get; set; } = 4f;
        /// <summary>Wither damage per second on an enemy.</summary>
        public float WitherDps { get; set; } = 4f;
        public float WitherDuration { get; set; } = 5f;

        // Might.
        /// <summary>Extra damage an Empowered player's shots deal.</summary>
        public float MightDamageBonus { get; set; } = 0.25f;
        public float MightDuration { get; set; } = 6f;
        /// <summary>Extra damage an Exposed enemy takes from everyone.</summary>
        public float ExposeBonus { get; set; } = 0.2f;
        public float ExposeDuration { get; set; } = 6f;

        // Slots and stacking.
        public int MaxBuffSlots { get; set; } = 3;
        public int MaxDebuffSlots { get; set; } = 3;
        /// <summary>Share of a second source's strength added to an effect already present.</summary>
        public float ExtraSourceShare { get; set; } = 0.5f;
        /// <summary>An effect's strength can't pass this many times a single source's strength.</summary>
        public float StackCapMultiplier { get; set; } = 2f;

        // Health.
        public float PlayerMaxHealth { get; set; } = 100f;
        public float WispHealth { get; set; } = 30f;
        public float SkitterHealth { get; set; } = 60f;
        /// <summary>Mites die to any hit.</summary>
        public float MiteHealth { get; set; } = 1f;
        /// <summary>Damage multiplier for a weak-point hit (for example a Skitter's core).</summary>
        public float WeakPointMultiplier { get; set; } = 2f;

        public float EnemyHealth(EnemyFamily family)
        {
            switch (family)
            {
                case EnemyFamily.Wisp: return WispHealth;
                case EnemyFamily.Skitter: return SkitterHealth;
                default: return MiteHealth;
            }
        }
    }
}
