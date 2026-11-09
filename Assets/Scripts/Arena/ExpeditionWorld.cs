using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Net;
using Unity.Mathematics;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The expedition layer (MVP step 4) as every world sees it. The layer is generated from
    /// <see cref="NetTestSettings.WorldSeed"/>, so the server and every client build the same map
    /// without sending it. The world wraps (see <see cref="Core.World.ZoneTiling"/>): positions are kept
    /// in the canonical copy of their zone, and anything drawn, aimed at or hit is first moved to its
    /// copy nearest the viewer.
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
        private static int _mapLayer;
        private static ArenaLayout.Box[] _cover;

        public static bool Active => NetTestSettings.Game == GameMode.Expedition;

        /// <summary>
        /// The layer the whole group is on (1 = the top, see <see cref="LayerSettings.LayerCount"/>). The server sets
        /// it; clients copy it from their expedition status.
        /// </summary>
        public static int Layer = 1;

        /// <summary>The current layer's tuning (palette, elites, tier, gate).</summary>
        public static LayerSettings Settings => LayerSettings.ForLayer(Layer);


        public static LayerMap Map
        {
            get
            {
                if (_map == null || _mapSeed != NetTestSettings.WorldSeed || _mapLayer != Layer)
                {
                    _mapSeed = NetTestSettings.WorldSeed;
                    _mapLayer = Layer;
                    _map = LayerGenerator.Generate(LayerSettings.SeedFor(_mapSeed, Layer), LayerSettings.MvpTiling(), Settings);
                    _cover = null;
                }
                return _map;
            }
        }

        private static IReadOnlyList<System.Numerics.Vector2> _raidDoors;
        private static uint _raidDoorSeed = uint.MaxValue;

        /// <summary>Where the layer's raid doors stand (world x, z). The raid platforms float above the first.</summary>
        public static IReadOnlyList<System.Numerics.Vector2> RaidDoors
        {
            get
            {
                var map = Map;
                if (_raidDoorSeed != map.Seed)
                {
                    _raidDoorSeed = map.Seed;
                    _raidDoors = RaidSite.DoorsFor(map);
                }
                return _raidDoors;
            }
        }

        /// <summary>
        /// The open raid door (index into <see cref="RaidDoors"/>). The server sets it; clients copy it from their
        /// expedition status (see <see cref="RaidDoorRotation"/>).
        /// </summary>
        public static int RaidDoorIndex;

        /// <summary>Spent raid doors as bits (drawn dark).</summary>
        public static ulong RaidDoorsSpent;

        /// <summary>Where the open raid door stands (world x, z).</summary>
        public static System.Numerics.Vector2 RaidDoor
        {
            get
            {
                var doors = RaidDoors;
                return doors[RaidDoorIndex >= 0 && RaidDoorIndex < doors.Count ? RaidDoorIndex : 0];
            }
        }

        /// <summary>Where the raid platforms float above (the first door; it never moves).</summary>
        public static System.Numerics.Vector2 RaidAnchor => RaidDoors[0];

        /// <summary>Moves a position into the canonical copy of its zone (keeps y).</summary>
        public static float3 Wrap(float3 p)
        {
            if (!Active) return p;
            var w = Map.Tiling.WrapPoint(Ground(p));
            return new float3(w.X, p.y, w.Y);
        }

        /// <summary>The copy of <paramref name="p"/> nearest <paramref name="reference"/> in the wrapping world.</summary>
        public static float3 Near(float3 p, float3 reference)
        {
            if (!Active) return p;
            var d = Map.Tiling.Delta(Ground(reference), Ground(p));
            return new float3(reference.x + d.X, p.y, reference.z + d.Y);
        }

        public static float3 ToFloat3(System.Numerics.Vector2 ground, float y = 0f) => new float3(ground.X, y, ground.Y);

        public static System.Numerics.Vector2 Ground(float3 p) => new System.Numerics.Vector2(p.x, p.z);

        /// <summary>
        /// Gate posts (and, on the flat floor, landmark blocks) stop shots and sight, like the arena's cover.
        /// On generated ground the zones' own landmarks and structures do that through their colliders.
        /// </summary>
        public static ArenaLayout.Box[] Cover
        {
            get
            {
                var map = Map;
                bool terrain = ExpeditionTerrain.Active;
                if (_cover != null && (!terrain || _coverOnGround)) return _cover;
                var boxes = new List<ArenaLayout.Box>();
                if (!terrain)
                {
                    foreach (var l in map.Landmarks)
                    {
                        boxes.Add(new ArenaLayout.Box(l.Point.World.X, l.Point.World.Y, LandmarkWidth, LandmarkHeight, LandmarkWidth));
                    }
                }
                _coverOnGround = true;
                foreach (var g in map.Gates)
                {
                    float x = g.Point.World.X, z = g.Point.World.Y;
                    float y = 0f;
                    if (terrain)
                    {
                        // Until the gate's zone is built its posts float at 0; check again next time.
                        if (ExpeditionTerrain.World.IsReady(x, z)) y = GateBaseY(g);
                        else _coverOnGround = false;
                    }
                    boxes.Add(new ArenaLayout.Box(x - GateWidth * 0.5f, z, 1f, GateHeight, 1f, y));
                    boxes.Add(new ArenaLayout.Box(x + GateWidth * 0.5f, z, 1f, GateHeight, 1f, y));
                }
                _cover = boxes.ToArray();
                return _cover;
            }
        }

        private static bool _coverOnGround;

        /// <summary>The ground under a gate (0 on the flat floor or before its zone is built).</summary>
        public static float GateBaseY(GateSite g) => ExpeditionTerrain.GroundY(g.Point.World.X, g.Point.World.Y);

        /// <summary>A spot a few metres from the layer's start shrine.</summary>
        public static float3 StartSpawn(ref Random random)
        {
            var start = ToFloat3(Map.StartPoint.World);
            var offset = random.NextFloat2Direction() * random.NextFloat(3f, 7f);
            return ExpeditionTerrain.OnGround(Wrap(start + new float3(offset.x, 0f, offset.y)));
        }

        /// <summary>A spot a few metres from the shrine nearest <paramref name="position"/> (where you respawn after bleeding out).</summary>
        public static float3 ShrineSpawn(float3 position, ref Random random)
        {
            var shrine = ToFloat3(Map.NearestShrine(Ground(position)).Point.World);
            var offset = random.NextFloat2Direction() * random.NextFloat(2f, 4f);
            return ExpeditionTerrain.OnGround(Wrap(shrine + new float3(offset.x, 0f, offset.y)));
        }
    }
}
