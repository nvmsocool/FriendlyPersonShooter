using System.Collections.Generic;
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
    /// Each room kind a raid run can pick (<see cref="RaidRoomLayout"/>) has its own platform, stacked
    /// <see cref="RoomSpacing"/> apart above the boss arena, so every platform exists all the time and a
    /// run just moves its players from one to the next.
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
        /// <summary>Room platforms sit this far apart, straight up (well past the fog, so each looks alone).</summary>
        public const float RoomSpacing = 300f;

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

        /// <summary>The centre (floor top) of a room's platform; the boss arena for <see cref="RaidRoomKind.Boss"/>.</summary>
        public static float3 RoomCenter(RaidRoomKind kind) => Center + new float3(0f, RoomSpacing * (int)kind, 0f);

        /// <summary>A world point in a room's own coordinates (as <see cref="RaidRoomLayout"/> uses them).</summary>
        public static System.Numerics.Vector3 ToRoom(RaidRoomKind kind, float3 world)
        {
            var c = RoomCenter(kind);
            var d = ExpeditionWorld.Near(world, c) - c;
            return new System.Numerics.Vector3(d.x, d.y, d.z);
        }

        public static float3 FromRoom(RaidRoomKind kind, System.Numerics.Vector3 local) => RoomCenter(kind) + new float3(local.X, local.Y, local.Z);

        /// <summary>True for a point in a room's space (the boss arena's for Boss).</summary>
        public static bool InRoom(RaidRoomKind kind, float3 p, float below = 40f, float above = 40f) =>
            kind == RaidRoomKind.Boss ? Contains(p, below, above) : RaidRoomLayout.For(kind).Contains(ToRoom(kind, p), below, above);

        /// <summary>True anywhere up among the raid platforms (far above any ground).</summary>
        public static bool InRaidSpace(float3 p) => p.y > FloorY - 150f;

        /// <summary>The room whose space holds a point (Boss for the arena), or false.</summary>
        public static bool RoomAt(float3 p, out RaidRoomKind kind)
        {
            kind = RaidRoomKind.Boss;
            if (!InRaidSpace(p)) return false;
            int k = (int)math.round((p.y - FloorY) / RoomSpacing);
            if (k < 0 || k > (int)RaidRoomLayout.LastKind) return false;
            kind = (RaidRoomKind)k;
            return true;
        }

        /// <summary>The i-th arrival spot in a room (the boss ring for Boss).</summary>
        public static float3 RoomSpawn(RaidRoomKind kind, int i, int n)
        {
            if (kind == RaidRoomKind.Boss) return SpawnSpot(i, n);
            var spawns = RaidRoomLayout.For(kind).Spawns;
            return FromRoom(kind, spawns[((i % spawns.Count) + spawns.Count) % spawns.Count]) + new float3(0f, 0.05f, 0f);
        }

        /// <summary>Each room's collider root and the layout seed it was built with.</summary>
        private static readonly Dictionary<RaidRoomKind, (GameObject Root, uint Seed)> _rooms = new Dictionary<RaidRoomKind, (GameObject, uint)>();

        /// <summary>Builds the arena's colliders once per scene (server and clients in one process share them).</summary>
        public static void Ensure()
        {
            if (!ExpeditionWorld.Active) return;
            if (_root != null && _boxesSeed == ExpeditionWorld.Map.Seed) return;
            if (_root != null) Object.Destroy(_root);
            _rooms.Clear();
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
            foreach (var kind in RaidRoomLayout.Pool) BuildRoom(kind, 0u);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Makes a room's colliders match a layout seed (a run's shape of that room). Called by the server as
        /// a stage begins and by each client from its status; a no-op once they match.
        /// </summary>
        public static void UseRoom(RaidRoomKind kind, uint seed)
        {
            if (kind == RaidRoomKind.Boss) return;
            Ensure();
            if (_root == null || (_rooms.TryGetValue(kind, out var built) && built.Seed == seed)) return;
            BuildRoom(kind, seed);
            Physics.SyncTransforms();
        }

        /// <summary>The layout seed a room's colliders were last built with (0 for the plain layout).</summary>
        public static uint SeedOf(RaidRoomKind kind) => _rooms.TryGetValue(kind, out var built) ? built.Seed : 0u;

        private static void BuildRoom(RaidRoomKind kind, uint seed)
        {
            if (_rooms.TryGetValue(kind, out var old) && old.Root != null) Object.Destroy(old.Root);
            var root = new GameObject($"{kind} room");
            root.layer = 0;
            root.transform.SetParent(_root.transform, false);
            var c = (Vector3)RoomCenter(kind);
            foreach (var b in RaidRoomLayout.For(kind, seed).Boxes)
            {
                var go = new GameObject($"{kind} {b.Kind}");
                go.layer = 0;
                go.transform.SetParent(root.transform, false);
                go.transform.position = c + new Vector3(b.Center.X, b.Center.Y, b.Center.Z);
                go.AddComponent<BoxCollider>().size = new Vector3(b.Size.X, b.Size.Y, b.Size.Z);
            }
            _rooms[kind] = (root, seed);
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
