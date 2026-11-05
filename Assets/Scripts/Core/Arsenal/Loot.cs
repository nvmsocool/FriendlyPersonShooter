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

        /// <summary>A new player's kit: one random gun with one random common payload mod.</summary>
        public static Gun StartingGun(uint seed)
        {
            var rng = new CombatRandom(seed == 0 ? 1u : seed);
            var frame = GunFrames.All[rng.NextInt(GunFrames.All.Length)];
            var payload = GunMods.PayloadKinds[rng.NextInt(GunMods.PayloadKinds.Length)];
            return new Gun(frame, payload0: new Mod(payload));
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

        public static IReadOnlyList<LootBox> For(LayerMap map)
        {
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
                    boxes.Add(new LootBox(index * IdStride + placed, zone.Coord, spot));
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
