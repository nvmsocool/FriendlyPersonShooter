using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Arsenal
{
    /// <summary>
    /// What a mod does. Stat mods improve one number; special mods do something unusual; payload mods
    /// go only in the payload slot and give the gun its payload. None = an empty slot.
    /// </summary>
    public enum ModKind : byte
    {
        None = 0,
        // Stat mods
        Damage = 1,
        FireRate = 2,
        Range = 3,
        Stability = 4,
        Scope = 5,
        Magazine = 6,
        Reload = 7,
        // Special mods (rare finds; they take a stat slot)
        Split = 16,
        Ricochet = 17,
        Conductor = 18,
        SelfShare = 19,
        // Payload mods (payload slot only)
        Vigor = 32,
        Might = 33,
        Haste = 34,
        Relay = 35,
        Frenzy = 36,
        Ward = 37,
        Focus = 38,
    }

    public enum ModTier : byte
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
    }

    /// <summary>One mod: its kind and tier. Default = empty slot.</summary>
    public readonly struct Mod
    {
        public readonly ModKind Kind;
        public readonly ModTier Tier;

        public Mod(ModKind kind, ModTier tier = ModTier.Common)
        {
            Kind = kind;
            Tier = kind == ModKind.None ? ModTier.Common : tier;
        }

        public bool IsEmpty => Kind == ModKind.None;
        public bool IsPayload => GunMods.IsPayload(Kind);
        public bool IsSpecial => GunMods.IsSpecial(Kind);

        public override string ToString() => IsEmpty ? "empty" : $"{Tier} {GunMods.Name(Kind)}";
    }

    /// <summary>The numbers and names behind mods.</summary>
    public static class GunMods
    {
        public static readonly ModKind[] StatKinds =
        {
            ModKind.Damage, ModKind.FireRate, ModKind.Range, ModKind.Stability, ModKind.Scope, ModKind.Magazine, ModKind.Reload,
        };
        public static readonly ModKind[] SpecialKinds = { ModKind.Split, ModKind.Ricochet, ModKind.Conductor, ModKind.SelfShare };
        public static readonly ModKind[] PayloadKinds =
        {
            ModKind.Vigor, ModKind.Might, ModKind.Haste, ModKind.Relay, ModKind.Frenzy, ModKind.Ward, ModKind.Focus,
        };

        /// <summary>A tier's share of a mod's effect: an epic mod is 30% better than a common one.</summary>
        public static float TierScale(ModTier tier) => tier == ModTier.Epic ? 1.3f : tier == ModTier.Rare ? 1.15f : 1f;

        // Base effects of a common stat mod (multiplied by TierScale).
        public const float DamageBonus = 0.06f;
        public const float FireRateBonus = 0.06f;
        public const float RangeBonus = 0.15f;
        /// <summary>Spread is multiplied by (1 - this).</summary>
        public const float StabilityBonus = 0.2f;
        /// <summary>Added zoom, and zoomed spread is multiplied by (1 - <see cref="ScopeSpreadBonus"/>).</summary>
        public const float ScopeZoom = 0.5f;
        public const float ScopeSpreadBonus = 0.3f;
        public const float MagazineBonus = 0.25f;
        /// <summary>Reload time is multiplied by (1 - this).</summary>
        public const float ReloadBonus = 0.15f;
        /// <summary>Strength of a payload mod's effects by tier (heals, buff and debuff strength).</summary>
        public static float PayloadStrength(ModTier tier) => tier == ModTier.Epic ? 1.2f : tier == ModTier.Rare ? 1.1f : 1f;

        /// <summary>Split: each of the two payloads works at this strength.</summary>
        public const float SplitStrength = 0.6f;
        /// <summary>Self-share: the shooter gets this share of the buffs they give.</summary>
        public const float SelfShare = 0.2f;
        /// <summary>Ricochet: an ally hit bounces to the nearest other ally within this distance.</summary>
        public const float RicochetRange = 10f;

        public static bool IsPayload(ModKind k) => k >= ModKind.Vigor && k <= ModKind.Focus;
        public static bool IsSpecial(ModKind k) => k >= ModKind.Split && k <= ModKind.SelfShare;
        public static bool IsStat(ModKind k) => k >= ModKind.Damage && k <= ModKind.Reload;

        public static PayloadKind Payload(ModKind k)
        {
            switch (k)
            {
                case ModKind.Vigor: return PayloadKind.Vigor;
                case ModKind.Might: return PayloadKind.Might;
                case ModKind.Haste: return PayloadKind.Haste;
                case ModKind.Relay: return PayloadKind.Relay;
                case ModKind.Frenzy: return PayloadKind.Frenzy;
                case ModKind.Ward: return PayloadKind.Ward;
                case ModKind.Focus: return PayloadKind.Focus;
                default: return PayloadKind.None;
            }
        }

        public static ModKind ForPayload(PayloadKind p)
        {
            switch (p)
            {
                case PayloadKind.Vigor: return ModKind.Vigor;
                case PayloadKind.Might: return ModKind.Might;
                case PayloadKind.Haste: return ModKind.Haste;
                case PayloadKind.Relay: return ModKind.Relay;
                case PayloadKind.Frenzy: return ModKind.Frenzy;
                case PayloadKind.Ward: return ModKind.Ward;
                case PayloadKind.Focus: return ModKind.Focus;
                default: return ModKind.None;
            }
        }

        public static string Name(ModKind k)
        {
            switch (k)
            {
                case ModKind.FireRate: return "Fire rate";
                case ModKind.SelfShare: return "Self-share";
                case ModKind.None: return "empty";
                default: return k.ToString();
            }
        }

        /// <summary>What the mod does, in a few words for the HUD.</summary>
        public static string Describe(Mod m)
        {
            float s = TierScale(m.Tier);
            switch (m.Kind)
            {
                case ModKind.Damage: return $"+{DamageBonus * s * 100f:0}% damage";
                case ModKind.FireRate: return $"+{FireRateBonus * s * 100f:0}% fire rate";
                case ModKind.Range: return $"+{RangeBonus * s * 100f:0}% range";
                case ModKind.Stability: return $"-{StabilityBonus * s * 100f:0}% spread";
                case ModKind.Scope: return $"+{ScopeZoom * s:0.0}x zoom";
                case ModKind.Magazine: return $"+{MagazineBonus * s * 100f:0}% magazine";
                case ModKind.Reload: return $"-{ReloadBonus * s * 100f:0}% capsule regrowth time";
                case ModKind.Split: return "holds two payloads at 60%";
                case ModKind.Ricochet: return "ally hits bounce to another ally";
                case ModKind.Conductor: return "chains you start get +1 link";
                case ModKind.SelfShare: return "you get 20% of buffs you give";
                case ModKind.Vigor: return "heals allies; withers enemies";
                case ModKind.Might: return "allies deal more damage; enemies take more";
                case ModKind.Haste: return "allies move faster; slows enemies";
                case ModKind.Relay: return "allies' shots also carry Might (or your Split's other payload); tags enemies";
                case ModKind.Frenzy: return "allies fire faster and regrow their capsule faster; jams enemies";
                case ModKind.Ward: return "shields allies; makes enemy armor brittle";
                case ModKind.Focus: return "allies get bigger weak points; marks enemy weak points";
                default: return "";
            }
        }
    }
}
