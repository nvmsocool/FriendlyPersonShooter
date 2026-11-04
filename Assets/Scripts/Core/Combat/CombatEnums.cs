namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// What a gun's bullets carry. Each payload buffs a player it hits and debuffs an enemy it hits.
    /// Values are fixed so they can be sent over the network; Frenzy, Ward and Focus come later.
    /// </summary>
    public enum PayloadKind : byte
    {
        Vigor = 0,
        Might = 1,
        Haste = 2,
        // Frenzy = 3, Ward = 4, Focus = 5 (not built yet)
        Relay = 6,
    }

    /// <summary>A timed effect sitting in a player's buff slots or an enemy's debuff slots.</summary>
    public enum EffectKind : byte
    {
        /// <summary>Player buff from Vigor overflow. Strength is shield points; absorbs damage first.</summary>
        Shield = 0,
        /// <summary>Player buff from Might. Strength is a damage bonus fraction (0.25 = +25%).</summary>
        Empower = 1,
        /// <summary>Player buff from Haste. Strength is a move-speed bonus fraction (0.3 = +30%); jumps grow in proportion.</summary>
        Hasted = 2,
        /// <summary>
        /// Player buff from Relay: the player's next few hits also carry the relayer's payload and
        /// count as a chain link. Strength is unused (1); the carried payload and hits left live on
        /// <see cref="PlayerVitals"/>.
        /// </summary>
        Relayed = 6,
        /// <summary>Enemy debuff from Vigor. Strength is damage per second.</summary>
        Wither = 64,
        /// <summary>Enemy debuff from Might. Strength is extra damage taken (0.2 = +20%).</summary>
        Expose = 65,
        /// <summary>Enemy debuff from Haste. Strength is the share of speed lost (0.35 = moves and turns 35% slower).</summary>
        Slow = 66,
        /// <summary>Enemy debuff from Relay. Strength is the share of each hit's damage that splashes to nearby enemies.</summary>
        Tag = 70,
    }

    /// <summary>Who a player accepts buffs from.</summary>
    public enum BuffAcceptance : byte
    {
        Everyone = 0,
        PartyOnly = 1,
        NoOne = 2,
    }

    /// <summary>The enemy families in the MVP arena.</summary>
    public enum EnemyFamily : byte
    {
        Wisp = 0,
        Skitter = 1,
        Mite = 2,
        /// <summary>A small, short-lived target that pops up near players: one shot, a little Yellow, no attack.</summary>
        Glimmer = 3,
    }

    /// <summary>What happened when an effect was offered to a set of slots.</summary>
    public enum EffectOutcome : byte
    {
        /// <summary>No effect was involved (for example a pure heal).</summary>
        None = 0,
        /// <summary>Put into a free slot.</summary>
        Added,
        /// <summary>Put into a slot by pushing out the oldest effect.</summary>
        ReplacedOldest,
        /// <summary>Same effect from the same source: only the duration was reset.</summary>
        Refreshed,
        /// <summary>Same effect from a new source: duration reset and strength raised (up to the cap).</summary>
        Stacked,
        /// <summary>The target would not take it; see <see cref="RefuseReason"/>.</summary>
        Refused,
    }

    /// <summary>Why a buff or hit was refused.</summary>
    public enum RefuseReason : byte
    {
        None = 0,
        /// <summary>A player's payload never buffs themselves.</summary>
        SelfBuff,
        /// <summary>The target accepts buffs from no one.</summary>
        OptedOut,
        /// <summary>The target accepts buffs from their party only, and the shooter isn't in it.</summary>
        NotInParty,
        /// <summary>The target player is down; only their soul fragments can be shot (see <see cref="ReviveState"/>).</summary>
        TargetDown,
        /// <summary>The target enemy is already dead.</summary>
        TargetDead,
    }

    /// <summary>
    /// A stronger effect that triggers while two specific effects sit on one target. Values are
    /// flags so a target's active combos fit in one byte.
    /// </summary>
    [System.Flags]
    public enum ComboKind : byte
    {
        None = 0,
        /// <summary>Wither + Tag on an enemy: its Wither keeps spreading to nearby enemies.</summary>
        Plague = 1,
        /// <summary>Haste + Might on a player: their Empower damage bonus counts double.</summary>
        Blitz = 2,
    }

    /// <summary>Maps payloads to the effect they put on an ally or an enemy.</summary>
    public static class Payloads
    {
        /// <summary>The buff this payload gives a player (Vigor gives Shield, from heal overflow).</summary>
        public static EffectKind AllyEffect(PayloadKind payload)
        {
            switch (payload)
            {
                case PayloadKind.Vigor: return EffectKind.Shield;
                case PayloadKind.Haste: return EffectKind.Hasted;
                case PayloadKind.Relay: return EffectKind.Relayed;
                default: return EffectKind.Empower;
            }
        }

        /// <summary>The debuff this payload puts on an enemy.</summary>
        public static EffectKind EnemyEffect(PayloadKind payload)
        {
            switch (payload)
            {
                case PayloadKind.Vigor: return EffectKind.Wither;
                case PayloadKind.Haste: return EffectKind.Slow;
                case PayloadKind.Relay: return EffectKind.Tag;
                default: return EffectKind.Expose;
            }
        }
    }
}
