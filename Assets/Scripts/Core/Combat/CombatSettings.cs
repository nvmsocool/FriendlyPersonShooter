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
        /// <summary>Health restored by an ally hit from a gun with no payload (times the frame's heal scale): a tiny amount.</summary>
        public float PlainHeal { get; set; } = 0.6f;
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

        // Haste.
        /// <summary>Extra move speed a Hasted player gets.</summary>
        public float HasteMoveBonus { get; set; } = 0.3f;
        /// <summary>Extra jump strength a Hasted player gets (scales with the stacked Haste strength).</summary>
        public float HasteJumpBonus { get; set; } = 0.2f;
        public float HasteDuration { get; set; } = 6f;
        /// <summary>Share of move and turn speed a Slowed enemy loses.</summary>
        public float SlowAmount { get; set; } = 0.35f;
        public float SlowDuration { get; set; } = 5f;
        /// <summary>A Slowed enemy never drops below this share of its speed, however much Slow stacks.</summary>
        public float SlowFloor { get; set; } = 0.2f;

        // Frenzy.
        /// <summary>Extra fire rate and capsule-regrowth speed a Frenzied player gets.</summary>
        public float FrenzyBonus { get; set; } = 0.3f;
        public float FrenzyDuration { get; set; } = 6f;
        /// <summary>How much slower a Jammed enemy attacks (its attack clock runs at 1 / (1 + this)).</summary>
        public float JamAmount { get; set; } = 0.6f;
        public float JamDuration { get; set; } = 5f;

        // Ward.
        /// <summary>Shield points one Ward hit gives (stacks like other buffs, up to 2x).</summary>
        public float WardShield { get; set; } = 20f;
        public float WardDuration { get; set; } = 6f;
        /// <summary>Share of an enemy's armor a Brittle debuff turns off (0.5 = armored hits do half way to full damage).</summary>
        public float BrittleAmount { get; set; } = 0.5f;
        public float BrittleDuration { get; set; } = 6f;

        // Focus.
        /// <summary>How much larger weak points are for a Focused player's own hits (radius).</summary>
        public float FocusWeakPointBonus { get; set; } = 0.35f;
        public float FocusDuration { get; set; } = 6f;
        /// <summary>How much larger a Marked enemy's weak points are for everyone (radius).</summary>
        public float MarkWeakPointBonus { get; set; } = 0.4f;
        public float MarkDuration { get; set; } = 6f;

        // Relay.
        /// <summary>How many of a Relayed player's next hits carry the relayer's payload.</summary>
        public int RelayHits { get; set; } = 5;
        public float RelayDuration { get; set; } = 6f;
        /// <summary>Share of a hit's damage on a Tagged enemy that splashes to each enemy nearby.</summary>
        public float TagSplashShare { get; set; } = 0.3f;
        /// <summary>Splash reaches enemies whose body centre is within this many metres of the Tagged enemy's.</summary>
        public float TagSplashRadius { get; set; } = 3f;
        public float TagDuration { get; set; } = 6f;

        // Chains (see ChainTracker).
        /// <summary>Seconds a chain's holder has to pass it on or cash it.</summary>
        public float ChainWindow { get; set; } = 5f;
        public int ChainMaxLinks { get; set; } = 4;
        public float ChainBonus2Links { get; set; } = 0.15f;
        public float ChainBonus3Links { get; set; } = 0.35f;
        public float ChainBonus4Links { get; set; } = 0.6f;
        /// <summary>A weak-point cashing hit multiplies the chain bonus by this.</summary>
        public float ChainWeakPointFactor { get; set; } = 1.5f;

        // Combos.
        /// <summary>Blitz (Haste + Might on a player) multiplies their Empower bonus by this.</summary>
        public float BlitzEmpowerMultiplier { get; set; } = 2f;
        /// <summary>Plague (Wither + Tag on an enemy) spreads Wither to enemies within this many metres.</summary>
        public float PlagueRadius { get; set; } = 4f;
        /// <summary>Seconds between Plague spreads; the first spread happens as soon as the combo forms.</summary>
        public float PlagueInterval { get; set; } = 1f;
        /// <summary>Share of the plagued enemy's Wither damage per second that a spread Wither deals.</summary>
        public float PlagueShare { get; set; } = 0.75f;
        public float PlagueWitherDuration { get; set; } = 3f;
        /// <summary>Deadeye (Might + Focus on a player): their weak-point hits use this multiplier instead of <see cref="WeakPointMultiplier"/>.</summary>
        public float DeadeyeWeakPointMultiplier { get; set; } = 3f;
        /// <summary>Bastion (Vigor shield + Ward on a player): Ward shield points regained per second.</summary>
        public float BastionRegen { get; set; } = 8f;
        /// <summary>Pinned (Slow + Mark on an enemy): seconds it freezes when the combo forms.</summary>
        public float PinnedFreeze { get; set; } = 1f;

        // Downed and revive.
        public ReviveSettings Revive { get; set; } = new ReviveSettings();

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

        /// <summary>Bonus fraction for a chain of <paramref name="links"/> links (0 below 2, capped at the max).</summary>
        public float ChainBonus(int links)
        {
            if (links > ChainMaxLinks) links = ChainMaxLinks;
            if (links >= 4) return ChainBonus4Links;
            if (links == 3) return ChainBonus3Links;
            if (links == 2) return ChainBonus2Links;
            return 0f;
        }

        /// <summary>Glimmers die to any single shot.</summary>
        public float GlimmerHealth { get; set; } = 5f;
        /// <summary>The raid boss's health per player in the raid (see <see cref="BulwarkSettings.HealthFor"/>).</summary>
        public float BulwarkHealth { get; set; } = 1300f;
        /// <summary>A Bulwark roaming the world: most hits do a fifth, so this is about 30 plate hits.</summary>
        public float WorldBulwarkHealth { get; set; } = 240f;
        /// <summary>Most Sentinel hits are on armor (see <see cref="SentinelSettings.ArmorMultiplier"/>); about 3 eye hits.</summary>
        public float SentinelHealth { get; set; } = 50f;
        public float DiverHealth { get; set; } = 35f;

        public float EnemyHealth(EnemyFamily family)
        {
            switch (family)
            {
                case EnemyFamily.Wisp: return WispHealth;
                case EnemyFamily.Skitter: return SkitterHealth;
                case EnemyFamily.Glimmer: return GlimmerHealth;
                case EnemyFamily.Bulwark: return BulwarkHealth;
                case EnemyFamily.Sentinel: return SentinelHealth;
                case EnemyFamily.Diver: return DiverHealth;
                default: return MiteHealth;
            }
        }
    }
}
