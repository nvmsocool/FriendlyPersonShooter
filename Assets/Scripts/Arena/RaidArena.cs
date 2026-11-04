using FriendlyPersonShooter.Core.Expedition;
using Unity.Mathematics;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The raid's arena: a walled stone platform floating high above the raid door, out of sight of the
    /// ground (fog hides everything past ~235 m). It is ordinary collision on the zones' layer, so player
    /// movement, shots, bots and Skitters all treat it as ground. The server and every client build the
    /// same colliders from the layer map; clients also draw it (see ArenaView).
    /// </summary>
    public static class RaidArena
    {
        /// <summary>Above the 800 m the ground queries start from, so spawns and shrines never land on it.</summary>
        public const float FloorY = 1200f;
        public const float HalfSize = 24f;
        public const float WallHeight = 1.6f;
        public const float PillarSize = 2.2f;
        public const float PillarHeight = 4f;
        /// <summary>Players spawn on a ring this far from the boss.</summary>
        public const float SpawnRing = 17f;

        /// <summary>One box of the arena, in world space (centre and full size).</summary>
        public readonly struct Box
        {
            public readonly Vector3 Center;
            public readonly Vector3 Size;
            public readonly bool IsFloor;

            public Box(Vector3 center, Vector3 size, bool isFloor = false)
            {
                Center = center;
                Size = size;
                IsFloor = isFloor;
            }
        }

        private static GameObject _root;
        private static Box[] _boxes;
        private static uint _boxesSeed;

        /// <summary>Where the raid door stands on the ground (world x, z).</summary>
        public static System.Numerics.Vector2 Door => ExpeditionWorld.RaidDoor;

        /// <summary>The middle of the arena floor.</summary>
        public static float3 Center => new float3(Door.X, FloorY, Door.Y);

        /// <summary>Floor, walls and four pillars.</summary>
        public static Box[] Boxes
        {
            get
            {
                uint seed = ExpeditionWorld.Map.Seed;
                if (_boxes != null && _boxesSeed == seed) return _boxes;
                _boxesSeed = seed;
                var c = (Vector3)Center;
                float s = HalfSize * 2f;
                float h = WallHeight;
                float p = 10f;
                _boxes = new[]
                {
                    new Box(c + new Vector3(0f, -1f, 0f), new Vector3(s + 2f, 2f, s + 2f), true),
                    new Box(c + new Vector3(0f, h / 2f, HalfSize + 0.5f), new Vector3(s + 2f, h, 1f)),
                    new Box(c + new Vector3(0f, h / 2f, -HalfSize - 0.5f), new Vector3(s + 2f, h, 1f)),
                    new Box(c + new Vector3(HalfSize + 0.5f, h / 2f, 0f), new Vector3(1f, h, s)),
                    new Box(c + new Vector3(-HalfSize - 0.5f, h / 2f, 0f), new Vector3(1f, h, s)),
                    new Box(c + new Vector3(p, PillarHeight / 2f, p), new Vector3(PillarSize, PillarHeight, PillarSize)),
                    new Box(c + new Vector3(-p, PillarHeight / 2f, p), new Vector3(PillarSize, PillarHeight, PillarSize)),
                    new Box(c + new Vector3(p, PillarHeight / 2f, -p), new Vector3(PillarSize, PillarHeight, PillarSize)),
                    new Box(c + new Vector3(-p, PillarHeight / 2f, -p), new Vector3(PillarSize, PillarHeight, PillarSize)),
                };
                return _boxes;
            }
        }

        /// <summary>Builds the arena's colliders once per scene (server and clients in one process share them).</summary>
        public static void Ensure()
        {
            if (!ExpeditionWorld.Active) return;
            if (_root != null && _boxesSeed == ExpeditionWorld.Map.Seed) return;
            if (_root != null) Object.Destroy(_root);
            _root = new GameObject("Raid Arena");
            _root.layer = 0;
            foreach (var b in Boxes)
            {
                var go = new GameObject(b.IsFloor ? "Floor" : "Wall");
                go.layer = 0;
                go.transform.SetParent(_root.transform, false);
                go.transform.position = b.Center;
                go.AddComponent<BoxCollider>().size = b.Size;
            }
            Physics.SyncTransforms();
        }

        /// <summary>True for a point over the platform (inside its walls) and near its height.</summary>
        public static bool Contains(float3 p, float below = 60f, float above = 60f)
        {
            var c = Center;
            var d = ExpeditionWorld.Near(p, c) - c;
            return math.abs(d.x) <= HalfSize + 1f && math.abs(d.z) <= HalfSize + 1f && d.y > -below && d.y < above;
        }

        /// <summary>The i-th of n spots on the spawn ring, facing the boss.</summary>
        public static float3 SpawnSpot(int i, int n)
        {
            float a = 2f * math.PI * i / math.max(1, n);
            return Center + new float3(math.sin(a) * SpawnRing, 0.05f, math.cos(a) * SpawnRing);
        }
    }
}
