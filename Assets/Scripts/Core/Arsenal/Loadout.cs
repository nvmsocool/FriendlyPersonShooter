namespace FriendlyPersonShooter.Core.Arsenal
{
    /// <summary>Something that can lie on the ground or come out of a box: a whole gun or one mod.</summary>
    public readonly struct LootItem
    {
        public readonly bool IsGun;
        public readonly Gun Gun;
        public readonly Mod Mod;

        private LootItem(bool isGun, Gun gun, Mod mod)
        {
            IsGun = isGun;
            Gun = gun;
            Mod = mod;
        }

        public static LootItem Of(Gun gun) => new LootItem(true, gun, default);
        public static LootItem Of(Mod mod) => new LootItem(false, default, mod);
        public static readonly LootItem None = default;

        public bool IsNone => IsGun ? Gun.IsEmpty : Mod.IsEmpty;

        /// <summary>Best tier on the item (a gun's best mod), for its glow on the ground.</summary>
        public ModTier Tier
        {
            get
            {
                if (!IsGun) return Mod.Tier;
                var best = ModTier.Common;
                foreach (var m in new[] { Gun.Mod0, Gun.Mod1, Gun.Mod2, Gun.Payload0, Gun.Payload1 })
                {
                    if (!m.IsEmpty && m.Tier > best) best = m.Tier;
                }
                return best;
            }
        }

        public ulong Pack() => IsGun ? Gun.Pack() | (1UL << 63) : Gun.PackMod(Mod);
        public static LootItem Unpack(ulong v) => (v >> 63) != 0 ? Of(Gun.Unpack(v & ~(1UL << 63))) : Of(Gun.UnpackMod((byte)v));

        public override string ToString() => IsGun ? Gun.ToString() : Mod.ToString();
    }

    /// <summary>What equipping an item did (or would do): up to two items dropped in exchange.</summary>
    public struct EquipResult
    {
        public bool Equipped;
        public LootItem Dropped;
        /// <summary>A second drop: the extra payload when Split is replaced.</summary>
        public LootItem Dropped2;
        /// <summary>Why it can't be equipped (when not <see cref="Equipped"/>).</summary>
        public string Refusal;
    }

    /// <summary>
    /// A player's gun: one gun only, no inventory. A picked-up gun replaces the gun in hand; a picked-up mod goes on
    /// the gun in hand (an empty slot, else in place of the same kind of mod, else the weakest). Whatever is replaced
    /// is handed back to drop on the ground.
    /// </summary>
    public sealed class Loadout
    {
        /// <summary>The gun in hand (empty before the starting pick).</summary>
        public Gun InHand;

        public Loadout() { }

        public Loadout(Gun gun)
        {
            InHand = gun;
        }

        public EquipResult Equip(LootItem item) => item.IsGun ? EquipGun(item.Gun) : EquipMod(item.Mod);

        public EquipResult EquipGun(Gun gun)
        {
            var r = new EquipResult();
            if (gun.IsEmpty)
            {
                r.Refusal = "nothing to equip";
                return r;
            }
            // One gun: a new one always replaces the gun in hand.
            if (!InHand.IsEmpty) r.Dropped = LootItem.Of(InHand);
            InHand = gun;
            r.Equipped = true;
            return r;
        }

        public EquipResult EquipMod(Mod mod)
        {
            var r = new EquipResult();
            var gun = InHand;
            if (mod.IsEmpty)
            {
                r.Refusal = "nothing to equip";
                return r;
            }
            if (gun.IsEmpty)
            {
                r.Refusal = "you need a gun in hand to put a mod on";
                return r;
            }

            if (mod.IsPayload)
            {
                if (gun.Payload0.IsEmpty) gun = gun.WithPayload(0, mod);
                else if (gun.HasSplit && gun.Payload1.IsEmpty) gun = gun.WithPayload(1, mod);
                else
                {
                    r.Dropped = LootItem.Of(gun.Payload0);
                    gun = gun.WithPayload(0, mod);
                }
            }
            else
            {
                int slot = SlotFor(gun, mod);
                var old = gun.GetMod(slot);
                if (!old.IsEmpty) r.Dropped = LootItem.Of(old);
                gun = gun.WithMod(slot, mod);
                // Losing Split closes the second payload slot.
                if (old.Kind == ModKind.Split && !gun.HasSplit && !gun.Payload1.IsEmpty)
                {
                    r.Dropped2 = LootItem.Of(gun.Payload1);
                    gun = gun.WithPayload(1, default);
                }
            }
            InHand = gun;
            r.Equipped = true;
            return r;
        }

        /// <summary>The mod slot a new stat or special mod would take: empty, else the same kind (weakest of them), else the weakest.</summary>
        public static int SlotFor(Gun gun, Mod mod)
        {
            for (int i = 0; i < Gun.ModSlots; i++) if (gun.GetMod(i).IsEmpty) return i;
            int same = -1;
            for (int i = 0; i < Gun.ModSlots; i++)
            {
                var m = gun.GetMod(i);
                if (m.Kind == mod.Kind && (same < 0 || m.Tier < gun.GetMod(same).Tier)) same = i;
            }
            if (same >= 0) return same;
            int weakest = 0;
            for (int i = 1; i < Gun.ModSlots; i++)
            {
                if (Weight(gun.GetMod(i)) < Weight(gun.GetMod(weakest))) weakest = i;
            }
            return weakest;
        }

        /// <summary>How much a mod is worth keeping: tier first; special mods count as one tier higher.</summary>
        private static int Weight(Mod m) => (int)m.Tier * 2 + (m.IsSpecial ? 2 : 0);
    }
}
