using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Net;
using Unity.Mathematics;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The expedition layer (MVP step 4) as every world sees it. The layer is generated from
    /// <see cref="NetTestSettings.WorldSeed"/>, so the server and every client build the same map
    /// without sending it. The world wraps at its edges: positions are kept in [0, Size) and
    /// anything drawn, aimed at or hit is first moved to its copy nearest the viewer.
    /// In the arena, every helper here does nothing.
    /// </summary>
    public static class ExpeditionWorld
    {
        /// <summary>Feature sizes (metres) used for drawing and for cover.</summary>
        public const float LandmarkWidth = 6f;
        public const float LandmarkHeight = 14f;
        public const float GateWidth = 8f;
        public const float GateHeight = 7f;
        public const float ShrineRadius = 4f;
        public const float GateUseRadius = 6f;
        public const float GatePassRadius = 2.5f;
        public const float CachePickupRadius = 2f;

        private static LayerMap _map;
        private static uint _mapSeed;
        private static ArenaLayout.Box[] _cover;

        public static bool Active => NetTestSettings.Game == GameMode.Expedition;

        public static LayerMap Map
        {
            get
            {
                if (_map == null || _mapSeed != NetTestSettings.WorldSeed)
                {
                    _mapSeed = NetTestSettings.WorldSeed;
                    _map = LayerGenerator.Generate(_mapSeed, LayerSettings.MvpGrid(), LayerSettings.ForMvp());
                    _cover = null;
                }
                return _map;
            }
        }

        /// <summary>Side of the square world in metres (0 in the arena).</summary>
        public static float Size => Active ? Map.Grid.Width * Map.Grid.ZoneSize : 0f;

        /// <summary>Puts a position back inside [0, Size) on x and z.</summary>
        public static float3 Wrap(float3 p)
        {
            if (!Active) return p;
            float s = Size;
            p.x -= s * math.floor(p.x / s);
            p.z -= s * math.floor(p.z / s);
            return p;
        }

        /// <summary>The copy of <paramref name="p"/> nearest <paramref name="reference"/> in the wrapping world.</summary>
        public static float3 Near(float3 p, float3 reference)
        {
            if (!Active) return p;
            float s = Size;
            float dx = p.x - reference.x;
            float dz = p.z - reference.z;
            dx -= s * math.round(dx / s);
            dz -= s * math.round(dz / s);
            return new float3(reference.x + dx, p.y, reference.z + dz);
        }

        public static float3 ToFloat3(System.Numerics.Vector2 ground, float y = 0f) => new float3(ground.X, y, ground.Y);

        public static System.Numerics.Vector2 Ground(float3 p) => new System.Numerics.Vector2(p.x, p.z);

        /// <summary>Landmarks and gate posts block shots and sight, like the arena's cover.</summary>
        public static ArenaLayout.Box[] Cover
        {
            get
            {
                var map = Map;
                if (_cover != null) return _cover;
                var boxes = new List<ArenaLayout.Box>();
                foreach (var l in map.Landmarks)
                {
                    boxes.Add(new ArenaLayout.Box(l.Point.World.X, l.Point.World.Y, LandmarkWidth, LandmarkHeight, LandmarkWidth));
                }
                foreach (var g in map.Gates)
                {
                    float x = g.Point.World.X, z = g.Point.World.Y;
                    boxes.Add(new ArenaLayout.Box(x - GateWidth * 0.5f, z, 1f, GateHeight, 1f));
                    boxes.Add(new ArenaLayout.Box(x + GateWidth * 0.5f, z, 1f, GateHeight, 1f));
                }
                _cover = boxes.ToArray();
                return _cover;
            }
        }

        /// <summary>A spot a few metres from the layer's start shrine.</summary>
        public static float3 StartSpawn(ref Random random)
        {
            var start = ToFloat3(Map.StartPoint.World);
            var offset = random.NextFloat2Direction() * random.NextFloat(3f, 7f);
            return Wrap(start + new float3(offset.x, 0f, offset.y));
        }

        /// <summary>A spot a few metres from the shrine nearest <paramref name="position"/> (where you respawn after bleeding out).</summary>
        public static float3 ShrineSpawn(float3 position, ref Random random)
        {
            var shrine = ToFloat3(Map.NearestShrine(Ground(position)).Point.World);
            var offset = random.NextFloat2Direction() * random.NextFloat(2f, 4f);
            return Wrap(shrine + new float3(offset.x, 0f, offset.y));
        }
    }
}
