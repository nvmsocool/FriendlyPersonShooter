using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Arsenal
{
    /// <summary>Where loot comes from; each source rolls differently.</summary>
    public enum LootSource : byte
    {
        Box = 0,
        /// <summary>A boss kill or raid clear: rare or better.</summary>
        Boss = 1,
    }

    /// <summary>Loot rolls. Deterministic: the same seed always gives the same item.</summary>
    public static class LootRolls
    {
        public const float BoxGunChance = 0.3f;
        public const float BossGunChance = 0.35f;

        public static LootItem Roll(LootSource source, uint seed)
        {
            var rng = new CombatRandom(seed == 0 ? 1u : seed);
            bool boss = source == LootSource.Boss;
            var tier = boss ? (rng.Chance(0.25f) ? ModTier.Epic : ModTier.Rare) : RollTier(ref rng);
            if (rng.Chance(boss ? BossGunChance : BoxGunChance))
            {
                var frame = GunFrames.All[rng.NextInt(GunFrames.All.Length)];
                // Found guns come bare or with one stat mod; a boss gun always has one.
                var mod = boss || rng.Chance(0.4f) ? new Mod(GunMods.StatKinds[rng.NextInt(GunMods.StatKinds.Length)], tier) : default;
                return LootItem.Of(new Gun(frame, mod));
            }
            float roll = rng.NextFloat();
            float payload = boss ? 0.3f : 0.35f, special = boss ? 0.2f : 0.1f;
            ModKind kind;
            if (roll < payload) kind = GunMods.PayloadKinds[rng.NextInt(GunMods.PayloadKinds.Length)];
            else if (roll < payload + special) kind = GunMods.SpecialKinds[rng.NextInt(GunMods.SpecialKinds.Length)];
            else kind = GunMods.StatKinds[rng.NextInt(GunMods.StatKinds.Length)];
            return LootItem.Of(new Mod(kind, tier));
        }

        /// <summary>Box tiers: 65% common, 28% rare, 7% epic.</summary>
        private static ModTier RollTier(ref CombatRandom rng)
        {
            float t = rng.NextFloat();
            return t < 0.07f ? ModTier.Epic : t < 0.35f ? ModTier.Rare : ModTier.Common;
        }

        /// <summary>How many starting guns a new player picks from.</summary>
        public const int StartingChoiceCount = 3;

        /// <summary>
        /// A new player's choice of starting guns: <paramref name="count"/> guns, each with one common payload mod.
        /// No two share a frame and no two share a payload (so at most as many as there are payloads).
        /// </summary>
        public static Gun[] StartingChoices(uint seed, int count = StartingChoiceCount)
        {
            var rng = new CombatRandom(seed == 0 ? 1u : seed);
            var frames = (FrameKind[])GunFrames.All.Clone();
            var payloads = (ModKind[])GunMods.PayloadKinds.Clone();
            Shuffle(frames, ref rng);
            Shuffle(payloads, ref rng);
            count = Math.Max(1, Math.Min(count, Math.Min(frames.Length, payloads.Length)));
            var guns = new Gun[count];
            for (int i = 0; i < count; i++) guns[i] = new Gun(frames[i], payload0: new Mod(payloads[i]));
            return guns;
        }

        /// <summary>How many items a loot box offers to pick from.</summary>
        public const int BoxChoiceCount = 3;

        /// <summary>
        /// A loot box's offer: <paramref name="count"/> box rolls to pick one from. No two are the same kind of
        /// thing (two guns never share a frame, two mods never share a kind).
        /// </summary>
        public static LootItem[] BoxChoices(uint seed, int count = BoxChoiceCount) => Choices(LootSource.Box, seed, count);

        /// <summary>A boss reward (raid clear or world boss): three rare-or-better rolls to pick one from.</summary>
        public static LootItem[] BossChoices(uint seed, int count = BoxChoiceCount) => Choices(LootSource.Boss, seed, count);

        /// <summary><paramref name="count"/> rolls from <paramref name="source"/>, no two the same kind of thing.</summary>
        public static LootItem[] Choices(LootSource source, uint seed, int count = BoxChoiceCount)
        {
            var items = new LootItem[Math.Max(1, count)];
            int found = 0;
            for (uint attempt = 0; found < items.Length; attempt++)
            {
                var item = Roll(source, SeedHash.Of(seed, 0xC401, (int)attempt));
                bool repeat = false;
                for (int i = 0; i < found && !repeat; i++) repeat = SameKind(items[i], item);
                // After many tries take repeats rather than loop forever (can't happen with today's tables).
                if (repeat && attempt < 64) continue;
                items[found++] = item;
            }
            return items;
        }

        /// <summary>Same frame (guns) or same mod kind (mods).</summary>
        public static bool SameKind(LootItem a, LootItem b) =>
            a.IsGun == b.IsGun && (a.IsGun ? a.Gun.Frame == b.Gun.Frame : a.Mod.Kind == b.Mod.Kind);

        private static void Shuffle<T>(T[] items, ref CombatRandom rng)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                var t = items[i];
                items[i] = items[j];
                items[j] = t;
            }
        }

        /// <summary>A bot's kit: its role's payload on a hitscan frame its aim can use.</summary>
        public static Gun BotGun(PayloadKind payload, uint seed)
        {
            var rng = new CombatRandom(seed == 0 ? 1u : seed);
            var frames = new[] { FrameKind.Rifle, FrameKind.Smg, FrameKind.Pistol };
            return new Gun(frames[rng.NextInt(frames.Length)], payload0: new Mod(GunMods.ForPayload(payload)));
        }
    }

    /// <summary>A loot box standing in the world.</summary>
    public readonly struct LootBox
    {
        public readonly int Id;
        public readonly ZoneCoord Zone;
        /// <summary>World position on the ground plane (x, z).</summary>
        public readonly Vector2 World;

        public LootBox(int id, ZoneCoord zone, Vector2 world)
        {
            Id = id;
            Zone = zone;
            World = world;
        }
    }

    /// <summary>
    /// Where a layer's loot boxes stand: a few per zone, from the layer seed, clear of the zone's
    /// landmarks, shrines, spawn points, gate and raid door, and away from the zone's edges.
    /// </summary>
    public static class LootBoxSites
    {
        public const int PerZone = 9;
        /// <summary>Box ids are zone index times this, plus the box's number in the zone.</summary>
        public const int IdStride = 16;
        public const float Clearance = 14f;
        public const float EdgeMargin = 20f;
        private const int Salt = 0x10B7;

        /// <summary>Ids on a deeper layer start this far apart, so opened boxes never clash between layers.</summary>
        public const int LayerIdStride = 100000;

        /// <summary>The layer's boxes; <paramref name="layer"/> offsets their ids (layer 1 keeps ids from 0).</summary>
        public static IReadOnlyList<LootBox> For(LayerMap map, int layer = 1)
        {
            int idBase = (Math.Max(1, layer) - 1) * LayerIdStride;
            if (map == null) throw new ArgumentNullException(nameof(map));
            var boxes = new List<LootBox>();
            var door = RaidSite.DoorFor(map);
            var tiling = map.Tiling;
            var avoid = new List<Vector2>();
            for (int index = 0; index < map.Zones.Count; index++)
            {
                var zone = map.Zones[index];
                avoid.Clear();
                foreach (var l in zone.Landmarks) avoid.Add(l.Point.World);
                foreach (var s in zone.Shrines) avoid.Add(s.Point.World);
                foreach (var p in zone.SpawnPoints) avoid.Add(p.Point.World);
                if (zone.HasGate) avoid.Add(zone.Gate.Point.World);
                avoid.Add(door);
                tiling.Bounds(zone.Coord, out var min, out var max);
                float margin = Math.Min(EdgeMargin, tiling.EdgeDistance(zone.Coord, tiling.Centre(zone.Coord)) * 0.5f);
                // Boxes per zone scale with its area (a hexagon gets the full count).
                int perZone = Math.Max(1, (int)Math.Round(PerZone * tiling.AreaShare(zone.Coord)));
                var rng = new CombatRandom(SeedHash.Of(map.Seed, Salt, zone.Coord.Key));
                int placed = 0;
                for (int attempt = 0; attempt < 120 && placed < perZone; attempt++)
                {
                    var spot = new Vector2(rng.Range(min.X, max.X), rng.Range(min.Y, max.Y));
                    if (tiling.EdgeDistance(zone.Coord, spot) < margin) continue;
                    bool clear = true;
                    foreach (var a in avoid)
                    {
                        if (Vector2.DistanceSquared(a, spot) < Clearance * Clearance) { clear = false; break; }
                    }
                    if (!clear) continue;
                    boxes.Add(new LootBox(idBase + index * IdStride + placed, zone.Coord, spot));
                    avoid.Add(spot);
                    placed++;
                }
            }
            return boxes;
        }
    }

    /// <summary>Which boxes each player has opened this session. Every player gets their own roll from every box, once.</summary>
    public sealed class LootBoxLedger
    {
        private readonly HashSet<long> _opened = new HashSet<long>();

        /// <summary>True the first time <paramref name="playerId"/> opens <paramref name="boxId"/>.</summary>
        public bool TryOpen(int boxId, int playerId) => _opened.Add(Key(boxId, playerId));

        public bool HasOpened(int boxId, int playerId) => _opened.Contains(Key(boxId, playerId));

        public int OpenedBy(int playerId)
        {
            int n = 0;
            foreach (var k in _opened) if ((int)(k & 0xFFFFFFFF) == playerId) n++;
            return n;
        }

        /// <summary>The roll <paramref name="playerId"/> gets from <paramref name="boxId"/> in a session with <paramref name="sessionSeed"/>.</summary>
        public static uint RollSeed(uint sessionSeed, int boxId, int playerId) => SeedHash.Of(sessionSeed, 0x10B8, boxId, playerId);

        private static long Key(int boxId, int playerId) => ((long)boxId << 32) | (uint)playerId;
    }
}
