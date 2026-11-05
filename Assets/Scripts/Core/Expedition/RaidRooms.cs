using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>The rooms a raid run picks from (see Docs/RaidDevPlan.md). 0 is the boss stage.</summary>
    public enum RaidRoomKind : byte
    {
        Boss = 0,
        /// <summary>Puzzle: shoot the crystals in the altar's order, each hit by a different player.</summary>
        ColorLocks = 1,
        /// <summary>Puzzle: hold every plate at once until the bar fills, while enemies go for the holders.</summary>
        HoldPlates = 2,
        /// <summary>Skill: jump the stepping stones past the sweepers; everyone up must reach the far pad.</summary>
        Gauntlet = 3,
        /// <summary>Skill: kill enough Wisps before the timer runs out.</summary>
        WispStorm = 4,
    }

    /// <summary>One twist per run.</summary>
    public enum RaidModifier : byte
    {
        None = 0,
        /// <summary>Twice the adds; the Wisp Storm needs more kills.</summary>
        Swarming = 1,
        /// <summary>Enemy bolts fly faster.</summary>
        SwiftBolts = 2,
        /// <summary>Players take more damage; rooms pay more.</summary>
        Brittle = 3,
    }

    public enum RaidBoxKind : byte
    {
        Floor,
        Wall,
        Pillar,
        /// <summary>A gauntlet stepping stone or pad.</summary>
        Stone,
    }

    /// <summary>One collider box of a room, relative to the room's centre (the floor's top is y = 0).</summary>
    public readonly struct RaidBox
    {
        public readonly Vector3 Center;
        public readonly Vector3 Size;
        public readonly RaidBoxKind Kind;

        public RaidBox(Vector3 center, Vector3 size, RaidBoxKind kind)
        {
            Center = center;
            Size = size;
            Kind = kind;
        }

        public float Top => Center.Y + Size.Y * 0.5f;

        /// <summary>True if (x, z) is over the box's top.</summary>
        public bool Over(float x, float z, float margin = 0f) =>
            Math.Abs(x - Center.X) <= Size.X * 0.5f + margin && Math.Abs(z - Center.Z) <= Size.Z * 0.5f + margin;
    }

    /// <summary>A gauntlet sweeper: a low arm turning around a stone's centre. Jump it.</summary>
    public readonly struct RaidSweeper
    {
        public readonly Vector3 Pivot;
        public readonly float Length;
        /// <summary>Radians per second (the sign is the direction).</summary>
        public readonly float Speed;
        public readonly float Phase;

        public RaidSweeper(Vector3 pivot, float length, float speed, float phase)
        {
            Pivot = pivot;
            Length = length;
            Speed = speed;
            Phase = phase;
        }

        public float AngleAt(double t) => (float)(Phase + Speed * t);

        /// <summary>The arm's tip at room time <paramref name="t"/> (relative to the room centre).</summary>
        public Vector3 TipAt(double t)
        {
            float a = AngleAt(t);
            return Pivot + new Vector3((float)Math.Sin(a), 0f, (float)Math.Cos(a)) * Length;
        }
    }

    /// <summary>
    /// The shape of one raid room: colliders and the points its rules use. Every position is relative to
    /// the room's centre with the floor's top at y = 0. Each kind has one fixed layout (seeded variants
    /// come in phase 2), so the server and every client build the same platform without sending it.
    /// </summary>
    public sealed class RaidRoomLayout
    {
        public const float SquareHalf = 24f;
        public const float WallHeight = 1.6f;
        public const float CrystalRadius = 1.1f;
        public const float PlateRadius = 2.8f;
        public const float PortalRadius = 2.6f;
        /// <summary>Sweeper arms reach this high above the stone (jump peaks at about 1 m).</summary>
        public const float SweeperHeight = 0.55f;
        public const float SweeperWidth = 0.35f;
        /// <summary>Gauntlet: below this (under the stones' tops) a player has fallen.</summary>
        public const float FallDepth = 5f;

        public RaidRoomKind Kind { get; }
        public IReadOnlyList<RaidBox> Boxes { get; }
        /// <summary>Half extents of the room's space (x, z), for "is this player in the room".</summary>
        public Vector2 HalfExtents { get; }
        public Vector3 Portal { get; }
        /// <summary>Where players arrive.</summary>
        public IReadOnlyList<Vector3> Spawns { get; }
        /// <summary>Color Locks: crystal centres (Yellow, Red, Green). Hold the Plates: plate centres.</summary>
        public IReadOnlyList<Vector3> Targets { get; }
        /// <summary>Gauntlet: the stones in walking order, start pad first and end pad last.</summary>
        public IReadOnlyList<RaidBox> Path { get; }
        public IReadOnlyList<RaidSweeper> Sweepers { get; }
        /// <summary>Where adds come in (on the floor, away from the spawns).</summary>
        public IReadOnlyList<Vector3> AddSpots { get; }

        private RaidRoomLayout(RaidRoomKind kind, List<RaidBox> boxes, Vector2 halfExtents, Vector3 portal, List<Vector3> spawns,
            List<Vector3> targets, List<RaidBox> path, List<RaidSweeper> sweepers, List<Vector3> addSpots)
        {
            Kind = kind;
            Boxes = boxes;
            HalfExtents = halfExtents;
            Portal = portal;
            Spawns = spawns;
            Targets = targets;
            Path = path;
            Sweepers = sweepers;
            AddSpots = addSpots;
        }

        private static readonly Dictionary<RaidRoomKind, RaidRoomLayout> Cache = new Dictionary<RaidRoomKind, RaidRoomLayout>();

        public static RaidRoomLayout For(RaidRoomKind kind)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(kind, out var layout))
                {
                    layout = Build(kind);
                    Cache[kind] = layout;
                }
                return layout;
            }
        }

        /// <summary>The rooms runs can pick (the boss stage isn't one).</summary>
        public static readonly RaidRoomKind[] Pool = { RaidRoomKind.ColorLocks, RaidRoomKind.HoldPlates, RaidRoomKind.Gauntlet, RaidRoomKind.WispStorm };

        public static string NameOf(RaidRoomKind kind)
        {
            switch (kind)
            {
                case RaidRoomKind.ColorLocks: return "Color Locks";
                case RaidRoomKind.HoldPlates: return "Hold the Plates";
                case RaidRoomKind.Gauntlet: return "Sweeper Gauntlet";
                case RaidRoomKind.WispStorm: return "Wisp Storm";
                default: return "The Bulwark";
            }
        }

        public static string NameOf(RaidModifier modifier)
        {
            switch (modifier)
            {
                case RaidModifier.Swarming: return "Swarming";
                case RaidModifier.SwiftBolts: return "Swift Bolts";
                case RaidModifier.Brittle: return "Brittle";
                default: return "";
            }
        }

        /// <summary>True if a point (relative to the room centre) is in the room's space.</summary>
        public bool Contains(Vector3 p, float below = 40f, float above = 40f) =>
            Math.Abs(p.X) <= HalfExtents.X + 1f && Math.Abs(p.Z) <= HalfExtents.Y + 1f && p.Y > -below && p.Y < above;

        private static RaidRoomLayout Build(RaidRoomKind kind)
        {
            var boxes = new List<RaidBox>();
            var spawns = new List<Vector3>();
            var targets = new List<Vector3>();
            var path = new List<RaidBox>();
            var sweepers = new List<RaidSweeper>();
            var adds = new List<Vector3>();

            if (kind == RaidRoomKind.Gauntlet) return BuildGauntlet(boxes, spawns, path, sweepers);

            Square(boxes);
            for (int i = 0; i < 8; i++)
            {
                float a = (float)(2 * Math.PI * i / 8);
                spawns.Add(new Vector3((float)Math.Sin(a) * 6f, 0f, (float)Math.Cos(a) * 6f));
            }
            for (int i = 0; i < 8; i++)
            {
                float a = (float)(2 * Math.PI * (i + 0.5f) / 8);
                adds.Add(new Vector3((float)Math.Sin(a) * 20f, 0f, (float)Math.Cos(a) * 20f));
            }
            var portal = new Vector3(0f, 0f, -SquareHalf + 4f);

            switch (kind)
            {
                case RaidRoomKind.ColorLocks:
                    // Three crystal pillars on a ring, the altar (which shows the order) in the middle.
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (float)(2 * Math.PI * i / 3);
                        var at = new Vector3((float)Math.Sin(a) * 15f, 0f, (float)Math.Cos(a) * 15f);
                        boxes.Add(new RaidBox(at + new Vector3(0f, 1.5f, 0f), new Vector3(2.2f, 3f, 2.2f), RaidBoxKind.Pillar));
                        targets.Add(at + new Vector3(0f, 3f + CrystalRadius + 0.2f, 0f));
                    }
                    boxes.Add(new RaidBox(new Vector3(0f, 0.6f, 0f), new Vector3(3f, 1.2f, 3f), RaidBoxKind.Pillar));
                    break;
                case RaidRoomKind.HoldPlates:
                    // Four plates in the corners, low cover between them.
                    targets.Add(new Vector3(-13f, 0f, 13f));
                    targets.Add(new Vector3(13f, 0f, 13f));
                    targets.Add(new Vector3(13f, 0f, -13f));
                    targets.Add(new Vector3(-13f, 0f, -13f));
                    boxes.Add(new RaidBox(new Vector3(0f, 0.6f, 13f), new Vector3(4f, 1.2f, 1f), RaidBoxKind.Pillar));
                    boxes.Add(new RaidBox(new Vector3(0f, 0.6f, -13f), new Vector3(4f, 1.2f, 1f), RaidBoxKind.Pillar));
                    boxes.Add(new RaidBox(new Vector3(13f, 0.6f, 0f), new Vector3(1f, 1.2f, 4f), RaidBoxKind.Pillar));
                    boxes.Add(new RaidBox(new Vector3(-13f, 0.6f, 0f), new Vector3(1f, 1.2f, 4f), RaidBoxKind.Pillar));
                    break;
                case RaidRoomKind.WispStorm:
                    // Six pillars for cover from the bolts.
                    for (int i = 0; i < 6; i++)
                    {
                        float a = (float)(2 * Math.PI * (i + 0.5f) / 6);
                        var at = new Vector3((float)Math.Sin(a) * 12f, 0f, (float)Math.Cos(a) * 12f);
                        boxes.Add(new RaidBox(at + new Vector3(0f, 2f, 0f), new Vector3(2f, 4f, 2f), RaidBoxKind.Pillar));
                    }
                    break;
            }
            return new RaidRoomLayout(kind, boxes, new Vector2(SquareHalf, SquareHalf), portal, spawns, targets, path, sweepers, adds);
        }

        /// <summary>The walled square platform the boss arena also uses.</summary>
        private static void Square(List<RaidBox> boxes)
        {
            float s = SquareHalf * 2f;
            float h = WallHeight;
            boxes.Add(new RaidBox(new Vector3(0f, -1f, 0f), new Vector3(s + 2f, 2f, s + 2f), RaidBoxKind.Floor));
            boxes.Add(new RaidBox(new Vector3(0f, h / 2f, SquareHalf + 0.5f), new Vector3(s + 2f, h, 1f), RaidBoxKind.Wall));
            boxes.Add(new RaidBox(new Vector3(0f, h / 2f, -SquareHalf - 0.5f), new Vector3(s + 2f, h, 1f), RaidBoxKind.Wall));
            boxes.Add(new RaidBox(new Vector3(SquareHalf + 0.5f, h / 2f, 0f), new Vector3(1f, h, s), RaidBoxKind.Wall));
            boxes.Add(new RaidBox(new Vector3(-SquareHalf - 0.5f, h / 2f, 0f), new Vector3(1f, h, s), RaidBoxKind.Wall));
        }

        /// <summary>Gauntlet stones: 5.5 m apart (gaps of about 2 m, a jump reaches 4 m), drifting side to side.</summary>
        public const int GauntletStones = 12;
        public const float StoneSize = 3.2f;
        public const float SweeperStoneSize = 4.4f;
        public const float StoneSpacing = 5.5f;

        private static RaidRoomLayout BuildGauntlet(List<RaidBox> boxes, List<Vector3> spawns, List<RaidBox> path, List<RaidSweeper> sweepers)
        {
            const float pad = 12f;
            var start = new RaidBox(new Vector3(0f, -1f, -40f), new Vector3(pad, 2f, pad), RaidBoxKind.Stone);
            var end = new RaidBox(new Vector3(0f, -1f, 40f), new Vector3(pad, 2f, pad), RaidBoxKind.Stone);
            path.Add(start);
            for (int i = 0; i < GauntletStones; i++)
            {
                bool sweeper = i == 2 || i == 5 || i == 8 || i == 11;
                float size = sweeper ? SweeperStoneSize : StoneSize;
                float x = 2f * (float)Math.Sin(i * 1.3f);
                float top = 0.35f * (float)Math.Sin(i * 2.1f);
                float z = -30.1f + StoneSpacing * i;
                var stone = new RaidBox(new Vector3(x, top - 1f, z), new Vector3(size, 2f, size), RaidBoxKind.Stone);
                path.Add(stone);
                if (sweeper)
                {
                    float speed = (i % 2 == 0 ? 1f : -1f) * (1.9f + 0.15f * i);
                    sweepers.Add(new RaidSweeper(new Vector3(x, top, z), size * 0.5f + 0.4f, speed, i * 0.9f));
                }
            }
            path.Add(end);
            boxes.AddRange(path);
            for (int i = 0; i < 8; i++)
            {
                spawns.Add(start.Center + new Vector3(-3.5f + (i % 4) * 2.3f, 1f, -2f + (i / 4) * 3f));
            }
            var portal = end.Center + new Vector3(0f, 1f, 3.5f);
            return new RaidRoomLayout(RaidRoomKind.Gauntlet, boxes, new Vector2(10f, 48f), portal, spawns, new List<Vector3>(), path,
                sweepers, new List<Vector3>());
        }

        /// <summary>
        /// Gauntlet: the index in <see cref="Path"/> of the stone a point stands over (-1 if none). Used
        /// to send fallers back and to tell bots which stone comes next.
        /// </summary>
        public int StoneUnder(Vector3 p, float margin = 0.3f)
        {
            for (int i = 0; i < Path.Count; i++) if (Path[i].Over(p.X, p.Z, margin)) return i;
            return -1;
        }
    }
}
