namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// What a gun's bullets carry. Each payload buffs a player it hits and debuffs an enemy it hits.
    /// Values are fixed so they can be sent over the network; the other five payloads come later.
    /// </summary>
    public enum PayloadKind : byte
    {
        Vigor = 0,
        Might = 1,
        // Haste = 2, Frenzy = 3, Ward = 4, Focus = 5, Relay = 6 (not built yet)
    }

    /// <summary>A timed effect sitting in a player's buff slots or an enemy's debuff slots.</summary>
    public enum EffectKind : byte
    {
        /// <summary>Player buff from Vigor overflow. Strength is shield points; absorbs damage first.</summary>
        Shield = 0,
        /// <summary>Player buff from Might. Strength is a damage bonus fraction (0.25 = +25%).</summary>
        Empower = 1,
        /// <summary>Enemy debuff from Vigor. Strength is damage per second.</summary>
        Wither = 64,
        /// <summary>Enemy debuff from Might. Strength is extra damage taken (0.2 = +20%).</summary>
        Expose = 65,
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
        /// <summary>The target player is down (revives come in a later step).</summary>
        TargetDown,
        /// <summary>The target enemy is already dead.</summary>
        TargetDead,
    }
}
