using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>
    /// Builds a <see cref="LayerMap"/> from a seed. Every zone draws from its own random stream
    /// (seed + zone), so the same seed and tiling give the same layer on every machine, and changing
    /// one setting for one zone doesn't reshuffle the rest. Landmark names are made unique across
    /// the whole layer by generating zones in index order. Feature counts are for a hexagon and
    /// scale down with zone area (at least one each).
    /// </summary>
    public static class LayerGenerator
    {
        private const int FamilyCount = 3;

        public static LayerMap Generate(uint seed, ZoneTiling tiling, LayerSettings settings = null)
        {
            if (tiling == null) throw new ArgumentNullException(nameof(tiling));
            settings = settings ?? new LayerSettings();
            Validate(settings);

            var layerRandom = new CombatRandom(SeedHash.Of(seed, SeedHash.LayerSalt));
            // New players start in a hexagon: the roomiest zones.
            var startZone = new ZoneCoord(layerRandom.NextInt(tiling.Width), layerRandom.NextInt(tiling.Height), 0);

            var zones = new ZoneInfo[tiling.Count];
            var takenByZone = new List<Vector2>[zones.Length];
            var landmarks = new List<Landmark>();
            var shrines = new List<Shrine>();
            var usedNames = new HashSet<string>();

            for (int i = 0; i < zones.Length; i++)
            {
                var taken = new List<Vector2>();
                zones[i] = GenerateZone(seed, tiling, settings, tiling.Zones[i], taken, usedNames, landmarks, shrines);
                takenByZone[i] = taken;
            }

            var startPoint = zones[tiling.IndexOf(startZone)].Shrines[0].Point;
            var gates = PlaceGates(seed, tiling, settings, startPoint.World, zones, takenByZone, ref layerRandom);
            return new LayerMap(seed, tiling, zones, landmarks, shrines, gates, startZone, startPoint);
        }

        /// <summary>
        /// The zones a gate may stand in: centres at least <see cref="LayerSettings.GateDistanceShare"/> of the
        /// farthest zone centre's distance from <paramref name="start"/>, only hexagons when enough qualify.
        /// </summary>
        public static List<ZoneCoord> GateCandidates(ZoneTiling tiling, LayerSettings settings, Vector2 start)
        {
            float farthest = 0f;
            foreach (var z in tiling.Zones) farthest = Math.Max(farthest, tiling.DistanceBetween(start, tiling.Centre(z)));
            float min = settings.GateDistanceShare * farthest - 1e-3f;
            var all = new List<ZoneCoord>();
            var hexes = new List<ZoneCoord>();
            foreach (var z in tiling.Zones)
            {
                if (tiling.DistanceBetween(start, tiling.Centre(z)) < min) continue;
                all.Add(z);
                if (z.Shape == ZoneShape.Hexagon) hexes.Add(z);
            }
            return hexes.Count >= settings.GateCount ? hexes : all;
        }

        private static ZoneInfo GenerateZone(uint seed, ZoneTiling tiling, LayerSettings s, ZoneCoord coord,
            List<Vector2> taken, HashSet<string> usedNames, List<Landmark> landmarks, List<Shrine> shrines)
        {
            var random = new CombatRandom(SeedHash.Of(seed, SeedHash.ZoneSalt, coord.Key));
            float share = tiling.AreaShare(coord);

            var tile = s.Tiles[random.NextInt(s.Tiles.Count)];
            var bias = PickBias(s, ref random);
            var familyWeights = FamilyWeights(s, bias);

            var zoneLandmarks = new List<Landmark>();
            int landmarkCount = Scaled(Between(s.MinLandmarks, s.MaxLandmarks, ref random), share);
            for (int i = 0; i < landmarkCount; i++)
            {
                string name = LandmarkNames.Make(tile, ref random, usedNames);
                var point = new MapPoint(coord, Place(s, tiling, coord, taken, ref random));
                var landmark = new Landmark(landmarks.Count, name, point);
                landmarks.Add(landmark);
                zoneLandmarks.Add(landmark);
            }

            var zoneShrines = new List<Shrine>();
            int shrineCount = Scaled(Between(s.MinShrines, s.MaxShrines, ref random), share);
            for (int i = 0; i < shrineCount; i++)
            {
                var shrine = new Shrine(shrines.Count, new MapPoint(coord, Place(s, tiling, coord, taken, ref random)));
                shrines.Add(shrine);
                zoneShrines.Add(shrine);
            }

            // Spawn points don't block other features: enemies can appear near a landmark.
            var spawnTaken = new List<Vector2>(taken);
            var spawnPoints = new List<SpawnPoint>();
            int spawnCount = Scaled(Between(s.MinSpawnPoints, s.MaxSpawnPoints, ref random), share);
            for (int i = 0; i < spawnCount; i++)
            {
                var family = PickFamily(familyWeights, ref random);
                var point = new MapPoint(coord, Place(s, tiling, coord, spawnTaken, ref random));
                spawnPoints.Add(new SpawnPoint(i, family, point));
            }

            return new ZoneInfo(coord, tile, bias, familyWeights, zoneLandmarks, zoneShrines, spawnPoints);
        }

        /// <summary>A hexagon's count scaled to a zone's area share; never below one unless the count was zero.</summary>
        private static int Scaled(int count, float share) => count <= 0 ? 0 : Math.Max(1, (int)Math.Round(count * share));

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

        private static List<GateSite> PlaceGates(uint seed, ZoneTiling tiling, LayerSettings s, Vector2 start,
            ZoneInfo[] zones, List<Vector2>[] takenByZone, ref CombatRandom layerRandom)
        {
            var candidates = GateCandidates(tiling, s, start);
            if (candidates.Count < s.GateCount)
            {
                throw new InvalidOperationException(
                    $"Only {candidates.Count} zones are far enough from the start; can't place {s.GateCount} gates.");
            }

            var gates = new List<GateSite>();
            for (int g = 0; g < s.GateCount; g++)
            {
                int pick = layerRandom.NextInt(candidates.Count);
                var coord = candidates[pick];
                candidates.RemoveAt(pick);

                int index = tiling.IndexOf(coord);
                var zone = zones[index];
                var random = new CombatRandom(SeedHash.Of(seed, SeedHash.GateSalt, coord.Key));
                var site = new GateSite(gates.Count, new MapPoint(coord, Place(s, tiling, coord, takenByZone[index], ref random)));
                zone.Gate = site;
                gates.Add(site);
            }
            return gates;
        }

        /// <summary>
        /// A spot inside the zone, at least the edge margin (capped at half the zone's inner radius) from its
        /// edges, trying to keep <see cref="LayerSettings.FeatureSpacing"/> from everything already taken;
        /// after the tries run out it takes the roomiest spot found.
        /// </summary>
        private static Vector2 Place(LayerSettings s, ZoneTiling tiling, ZoneCoord zone, List<Vector2> taken, ref CombatRandom random)
        {
            var centre = tiling.Centre(zone);
            float margin = Math.Min(s.EdgeMargin, tiling.EdgeDistance(zone, centre) * 0.5f);
            tiling.Bounds(zone, out var min, out var max);
            Vector2 best = centre;
            float bestGap = -1f;
            int tries = Math.Max(1, s.PlacementTries);
            for (int i = 0, valid = 0; i < tries * 8 && valid < tries; i++)
            {
                var p = new Vector2(random.Range(min.X, max.X), random.Range(min.Y, max.Y));
                if (tiling.EdgeDistance(zone, p) < margin) continue;
                valid++;
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
