using System;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Arsenal
{
    /// <summary>
    /// One gun: a frame, three mod slots and a payload slot (a second payload slot opens with Split).
    /// A plain value; <see cref="Pack"/> squeezes it into a ulong for the network.
    /// </summary>
    public readonly struct Gun : IEquatable<Gun>
    {
        public const int ModSlots = 3;

        public readonly FrameKind Frame;
        public readonly Mod Mod0, Mod1, Mod2;
        public readonly Mod Payload0, Payload1;

        public Gun(FrameKind frame, Mod mod0 = default, Mod mod1 = default, Mod mod2 = default, Mod payload0 = default, Mod payload1 = default)
        {
            Frame = frame;
            Mod0 = mod0;
            Mod1 = mod1;
            Mod2 = mod2;
            Payload0 = payload0;
            Payload1 = payload1;
        }

        public static readonly Gun Empty = default;
        public bool IsEmpty => Frame == FrameKind.None;

        public Mod GetMod(int slot) => slot == 0 ? Mod0 : slot == 1 ? Mod1 : slot == 2 ? Mod2 : default;

        public Gun WithMod(int slot, Mod mod) => new Gun(Frame, slot == 0 ? mod : Mod0, slot == 1 ? mod : Mod1, slot == 2 ? mod : Mod2, Payload0, Payload1);
        public Gun WithPayload(int slot, Mod mod) => new Gun(Frame, Mod0, Mod1, Mod2, slot == 0 ? mod : Payload0, slot == 1 ? mod : Payload1);

        public bool Has(ModKind kind) => Mod0.Kind == kind || Mod1.Kind == kind || Mod2.Kind == kind;
        public bool HasSplit => Has(ModKind.Split);

        /// <summary>The payload of the first payload slot (None if empty).</summary>
        public PayloadKind Payload => GunMods.Payload(Payload0.Kind);
        /// <summary>The second payload (only while the gun has Split).</summary>
        public PayloadKind SecondPayload => HasSplit ? GunMods.Payload(Payload1.Kind) : PayloadKind.None;

        public ulong Pack()
        {
            ulong v = (ulong)Frame & 0xF;
            v |= (ulong)PackMod(Mod0) << 4;
            v |= (ulong)PackMod(Mod1) << 12;
            v |= (ulong)PackMod(Mod2) << 20;
            v |= (ulong)PackMod(Payload0) << 28;
            v |= (ulong)PackMod(Payload1) << 36;
            return v;
        }

        public static Gun Unpack(ulong v)
        {
            return new Gun((FrameKind)(v & 0xF), UnpackMod((byte)(v >> 4)), UnpackMod((byte)(v >> 12)), UnpackMod((byte)(v >> 20)),
                UnpackMod((byte)(v >> 28)), UnpackMod((byte)(v >> 36)));
        }

        public static byte PackMod(Mod m) => (byte)(((byte)m.Kind & 0x3F) | ((byte)m.Tier << 6));
        public static Mod UnpackMod(byte b) => new Mod((ModKind)(b & 0x3F), (ModTier)(b >> 6));

        public bool Equals(Gun o) => Pack() == o.Pack();
        public override bool Equals(object obj) => obj is Gun g && Equals(g);
        public override int GetHashCode() => Pack().GetHashCode();

        public override string ToString()
        {
            if (IsEmpty) return "empty";
            string payload = Payload0.IsEmpty ? "no payload" : GunMods.Name(Payload0.Kind) + (HasSplit && !Payload1.IsEmpty ? "+" + GunMods.Name(Payload1.Kind) : "");
            return $"{payload} {GunFrames.Name(Frame)}";
        }
    }

    /// <summary>A gun's final numbers: its frame's, changed by its mods. Skills are applied on top by the caller.</summary>
    public sealed class GunStats
    {
        public FrameStats Frame;
        public FireMode Mode;
        public float Damage;
        public float Interval;
        public int BurstCount;
        public float BurstGap;
        public float Spread;
        public float ZoomedSpread;
        public float Range;
        public float FalloffStart, FalloffEnd, FalloffMin;
        public int Magazine;
        public float Reload;
        public float Zoom;
        public float HealScale;
        public float EffectScale;
        public PayloadKind Payload;
        public PayloadKind SecondPayload;
        /// <summary>Strength of the first (and second) payload: tier, Split and the frame's effect scale.</summary>
        public float PayloadStrength, SecondStrength;
        public bool Ricochet, Conductor, SelfShare;

        public static GunStats For(Gun gun)
        {
            var f = GunFrames.For(gun.Frame);
            if (f == null) return null;
            var s = new GunStats
            {
                Frame = f, Mode = f.Mode, Damage = f.Damage, Interval = f.Interval, BurstCount = f.BurstCount, BurstGap = f.BurstGap,
                Spread = f.Spread, Range = f.Range, FalloffStart = f.FalloffStart, FalloffEnd = f.FalloffEnd, FalloffMin = f.FalloffMin,
                Magazine = f.Magazine, Reload = f.Reload, Zoom = f.Zoom, HealScale = f.HealScale, EffectScale = f.EffectScale,
            };
            float zoomedSpread = f.ZoomedSpread;
            float magazine = f.Magazine;
            for (int i = 0; i < Gun.ModSlots; i++)
            {
                var m = gun.GetMod(i);
                float t = GunMods.TierScale(m.Tier);
                switch (m.Kind)
                {
                    case ModKind.Damage: s.Damage *= 1f + GunMods.DamageBonus * t; break;
                    case ModKind.FireRate: s.Interval /= 1f + GunMods.FireRateBonus * t; break;
                    case ModKind.Range:
                        s.Range *= 1f + GunMods.RangeBonus * t;
                        s.FalloffStart *= 1f + GunMods.RangeBonus * t;
                        s.FalloffEnd *= 1f + GunMods.RangeBonus * t;
                        break;
                    case ModKind.Stability: s.Spread *= 1f - GunMods.StabilityBonus * t; break;
                    case ModKind.Scope:
                        s.Zoom += GunMods.ScopeZoom * t;
                        zoomedSpread *= 1f - GunMods.ScopeSpreadBonus * t;
                        break;
                    case ModKind.Magazine: magazine *= 1f + GunMods.MagazineBonus * t; break;
                    case ModKind.Reload: s.Reload *= 1f - GunMods.ReloadBonus * t; break;
                    case ModKind.Ricochet: s.Ricochet = true; break;
                    case ModKind.Conductor: s.Conductor = true; break;
                    case ModKind.SelfShare: s.SelfShare = true; break;
                }
            }
            s.ZoomedSpread = s.Spread * zoomedSpread;
            s.Magazine = Math.Max(f.Magazine + (magazine > f.Magazine ? 1 : 0), (int)Math.Round(magazine));

            s.Payload = gun.Payload;
            s.SecondPayload = gun.SecondPayload;
            float split = s.SecondPayload != PayloadKind.None ? GunMods.SplitStrength : 1f;
            s.PayloadStrength = GunMods.PayloadStrength(gun.Payload0.Tier) * split * f.EffectScale;
            s.SecondStrength = GunMods.PayloadStrength(gun.Payload1.Tier) * split * f.EffectScale;
            return s;
        }

        /// <summary>Damage multiplier at <paramref name="distance"/> metres (range falloff).</summary>
        public float FalloffAt(float distance)
        {
            if (FalloffEnd <= FalloffStart || distance <= FalloffStart) return 1f;
            if (distance >= FalloffEnd) return FalloffMin;
            float t = (distance - FalloffStart) / (FalloffEnd - FalloffStart);
            return 1f + (FalloffMin - 1f) * t;
        }

        /// <summary>Spread half-angle in degrees, zoomed or not.</summary>
        public float SpreadFor(bool zoomed) => zoomed ? ZoomedSpread : Spread;
    }
}
