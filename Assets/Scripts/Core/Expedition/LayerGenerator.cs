using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>
    /// Builds a <see cref="LayerMap"/> from a seed. Every zone draws from its own random stream
    /// (seed + zone), so the same seed and grid give the same layer on every machine, and changing
    /// one setting for one zone doesn't reshuffle the rest. Landmark names are made unique across
    /// the whole layer by generating zones in row order.
    /// </summary>
    public static class LayerGenerator
    {
        private const int FamilyCount = 3;

        public static LayerMap Generate(uint seed, WorldGrid grid, LayerSettings settings = null)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            settings = settings ?? new LayerSettings();
            Validate(settings);

            var layerRandom = new CombatRandom(SeedHash.Of(seed, SeedHash.LayerSalt));
            var startZone = new ZoneCoord(layerRandom.NextInt(grid.Width), layerRandom.NextInt(grid.Height));

            var zones = new ZoneInfo[grid.Width * grid.Height];
            var takenByZone = new List<Vector2>[zones.Length];
            var landmarks = new List<Landmark>();
            var shrines = new List<Shrine>();
            var usedNames = new HashSet<string>();

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var taken = new List<Vector2>();
                    var zone = GenerateZone(seed, grid, settings, new ZoneCoord(x, y), taken, usedNames, landmarks, shrines);
                    zones[y * grid.Width + x] = zone;
                    takenByZone[y * grid.Width + x] = taken;
                }
            }

            var gates = PlaceGates(seed, grid, settings, startZone, zones, takenByZone, ref layerRandom);
            var startPoint = zones[startZone.Y * grid.Width + startZone.X].Shrines[0].Point;
            return new LayerMap(seed, grid, zones, landmarks, shrines, gates, startZone, startPoint);
        }

        /// <summary>The smallest zone distance from the start a gate may have under these settings.</summary>
        public static int MinGateDistance(WorldGrid grid, LayerSettings settings)
        {
            int farthest = Math.Max(grid.Width / 2, grid.Height / 2);
            int min = (int)Math.Ceiling(settings.GateDistanceShare * farthest - 1e-6);
            return Math.Max(0, Math.Min(farthest, min));
        }

        private static ZoneInfo GenerateZone(uint seed, WorldGrid grid, LayerSettings s, ZoneCoord coord,
            List<Vector2> taken, HashSet<string> usedNames, List<Landmark> landmarks, List<Shrine> shrines)
        {
            var random = new CombatRandom(SeedHash.Of(seed, SeedHash.ZoneSalt, coord.X, coord.Y));
            float size = grid.ZoneSize;

            var tile = s.Tiles[random.NextInt(s.Tiles.Count)];
            var bias = PickBias(s, ref random);
            var familyWeights = FamilyWeights(s, bias);

            var zoneLandmarks = new List<Landmark>();
            int landmarkCount = Between(s.MinLandmarks, s.MaxLandmarks, ref random);
            for (int i = 0; i < landmarkCount; i++)
            {
                string name = LandmarkNames.Make(tile, ref random, usedNames);
                var point = new MapPoint(coord, Place(s, size, taken, ref random), size);
                var landmark = new Landmark(landmarks.Count, name, point);
                landmarks.Add(landmark);
                zoneLandmarks.Add(landmark);
            }

            var zoneShrines = new List<Shrine>();
            int shrineCount = Between(s.MinShrines, s.MaxShrines, ref random);
            for (int i = 0; i < shrineCount; i++)
            {
                var shrine = new Shrine(shrines.Count, new MapPoint(coord, Place(s, size, taken, ref random), size));
                shrines.Add(shrine);
                zoneShrines.Add(shrine);
            }

            // Spawn points don't block other features: enemies can appear near a landmark.
            var spawnTaken = new List<Vector2>(taken);
            var spawnPoints = new List<SpawnPoint>();
            int spawnCount = Between(s.MinSpawnPoints, s.MaxSpawnPoints, ref random);
            for (int i = 0; i < spawnCount; i++)
            {
                var family = PickFamily(familyWeights, ref random);
                var point = new MapPoint(coord, Place(s, size, spawnTaken, ref random), size);
                spawnPoints.Add(new SpawnPoint(i, family, point));
            }

            return new ZoneInfo(coord, tile, bias, familyWeights, zoneLandmarks, zoneShrines, spawnPoints);
        }

        private static ZoneColorBias PickBias(LayerSettings s, ref CombatRandom random)
        {
            if (s.ForcedColor.HasValue) return new ZoneColorBias(s.ForcedColor.Value);
            var primary = s.Palette[random.NextInt(s.Palette.Count)];
            if (s.Palette.Count < 2 || !random.Chance(s.SecondColorChance)) return new ZoneColorBias(primary);

            // Pick a different color from the rest of the palette.
            int pick = random.NextInt(s.Palette.Count - 1);
            var secondary = primary;
            for (int i = 0, seen = 0; i < s.Palette.Count; i++)
            {
                if (s.Palette[i] == primary) continue;
                if (seen++ == pick)
                {
                    secondary = s.Palette[i];
                    break;
                }
            }
            if (secondary == primary) return new ZoneColorBias(primary);
            return new ZoneColorBias(primary, secondary, random.Range(s.MinSecondaryShare, s.MaxSecondaryShare));
        }

        /// <summary>Turns a color lean into a spawn share per family (Yellow → Wisp and so on), adding to 1.</summary>
        internal static float[] FamilyWeights(LayerSettings s, ZoneColorBias bias)
        {
            var weights = new float[FamilyCount];
            // A forced color means a one-color layer: no off-lean spawns from the rest of the palette.
            var colors = s.ForcedColor.HasValue ? new List<EssenceColor>() : new List<EssenceColor>(s.Palette);
            if (!colors.Contains(bias.Primary)) colors.Add(bias.Primary);
            if (bias.HasSecondary && !colors.Contains(bias.Secondary)) colors.Add(bias.Secondary);

            float total = 0f;
            foreach (var color in colors)
            {
                if (!EssenceColors.TryGetFamily(color, out var family)) continue;
                float w = bias.LeansToward(color) ? bias.Weight(color) : s.OffBiasWeight;
                weights[(int)family] += w;
                total += w;
            }
            for (int i = 0; i < weights.Length; i++) weights[i] /= total;
            return weights;
        }

        private static EnemyFamily PickFamily(float[] weights, ref CombatRandom random)
        {
            float roll = random.NextFloat();
            float sum = 0f;
            int last = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                last = i;
                sum += weights[i];
                if (roll < sum) return (EnemyFamily)i;
            }
            return (EnemyFamily)last;
        }

        private static List<GateSite> PlaceGates(uint seed, WorldGrid grid, LayerSettings s, ZoneCoord start,
            ZoneInfo[] zones, List<Vector2>[] takenByZone, ref CombatRandom layerRandom)
        {
            int minDistance = MinGateDistance(grid, s);
            var candidates = new List<int>();
            for (int i = 0; i < zones.Length; i++)
            {
                if (grid.Distance(start, zones[i].Coord) >= minDistance) candidates.Add(i);
            }
            if (candidates.Count < s.GateCount)
            {
                throw new InvalidOperationException(
                    $"Only {candidates.Count} zones are {minDistance}+ zones from the start; can't place {s.GateCount} gates.");
            }

            var gates = new List<GateSite>();
            for (int g = 0; g < s.GateCount; g++)
            {
                int pick = layerRandom.NextInt(candidates.Count);
                int index = candidates[pick];
                candidates.RemoveAt(pick);

                var zone = zones[index];
                var random = new CombatRandom(SeedHash.Of(seed, SeedHash.GateSalt, zone.Coord.X, zone.Coord.Y));
                var local = Place(s, grid.ZoneSize, takenByZone[index], ref random);
                var site = new GateSite(gates.Count, new MapPoint(zone.Coord, local, grid.ZoneSize));
                zone.Gate = site;
                gates.Add(site);
            }
            return gates;
        }

        /// <summary>
        /// A spot inside the zone margins, trying to keep <see cref="LayerSettings.FeatureSpacing"/>
        /// from everything already taken; after the tries run out it takes the roomiest spot found.
        /// </summary>
        private static Vector2 Place(LayerSettings s, float size, List<Vector2> taken, ref CombatRandom random)
        {
            float margin = Math.Min(s.EdgeMargin, size * 0.25f);
            Vector2 best = default;
            float bestGap = -1f;
            int tries = Math.Max(1, s.PlacementTries);
            for (int i = 0; i < tries; i++)
            {
                var p = new Vector2(random.Range(margin, size - margin), random.Range(margin, size - margin));
                float gap = float.MaxValue;
                foreach (var t in taken) gap = Math.Min(gap, Vector2.Distance(p, t));
                if (gap > bestGap)
                {
                    bestGap = gap;
                    best = p;
                }
                if (gap >= s.FeatureSpacing) break;
            }
            taken.Add(best);
            return best;
        }

        private static int Between(int min, int max, ref CombatRandom random)
        {
            if (max < min) max = min;
            return min + random.NextInt(max - min + 1);
        }

        private static void Validate(LayerSettings s)
        {
            if (s.Palette == null || s.Palette.Count == 0) throw new ArgumentException("The palette needs at least one color.");
            foreach (var color in s.Palette)
            {
                if (!EssenceColors.TryGetFamily(color, out _))
                    throw new ArgumentException($"{color} has no enemy family yet, so no zone can spawn it.");
            }
            if (s.ForcedColor.HasValue && !EssenceColors.TryGetFamily(s.ForcedColor.Value, out _))
                throw new ArgumentException($"{s.ForcedColor.Value} has no enemy family yet.");
            if (s.Tiles == null || s.Tiles.Count == 0) throw new ArgumentException("The tile catalog is empty.");
            if (s.MinShrines < 1) throw new ArgumentException("Every zone needs at least one shrine.");
            if (s.MinLandmarks < 1) throw new ArgumentException("Every zone needs at least one landmark.");
            if (s.MinSpawnPoints < 0 || s.GateCount < 0) throw new ArgumentException("Counts can't be negative.");
        }
    }
}
