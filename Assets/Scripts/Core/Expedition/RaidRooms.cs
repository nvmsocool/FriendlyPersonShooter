using System;
using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;

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
        /// <summary>Puzzle: two sets of runes, split by a wall, each flash a sequence to shoot back.</summary>
        EchoRunes = 5,
        /// <summary>Skill: keep the core alive for a minute while Skitters run at it from three gates.</summary>
        HoldCore = 6,
        /// <summary>Puzzle: a dark room; enemies can only be hurt near a player an ally's payload made glow.</summary>
        Lanterns = 7,
        /// <summary>Skill: an eye opens for a moment somewhere on a turning shell; hit it, under fire.</summary>
        SentinelEye = 8,
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
        /// <summary>Higher, floatier jumps (gravity at <see cref="RaidRunSettings.LowGravity"/>).</summary>
        LowGravity = 4,
        /// <summary>Short sight lines: fog closes in on every room.</summary>
        Fog = 5,
    }

    public enum RaidBoxKind : byte
    {
        Floor,
        Wall,
        Pillar,
        /// <summary>A gauntlet stepping stone or pad.</summary>
        Stone,
        /// <summary>Echo Runes: the slab a set of runes is set into.</summary>
        Backdrop,
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
    /// the room's centre with the floor's top at y = 0. A room's layout comes from its kind and a seed:
    /// seed 0 is the plain layout, and each run rolls a new seed per room, so a room repeats with its
    /// crystals, plates, pillars, runes or stones in new places. The floor, walls, spawns and portal stay
    /// put for every seed. The server and every client build the same layout from (kind, seed).
    /// </summary>
    public sealed class RaidRoomLayout
    {
        public const float SquareHalf = 24f;
        public const float WallHeight = 1.6f;
        public const float CrystalRadius = 1.1f;
        public const float RuneRadius = 0.9f;
        public const float PlateRadius = 2.8f;
        public const float PortalRadius = 2.6f;
        /// <summary>Sweeper arms reach this high above the stone (jump peaks at about 1 m).</summary>
        public const float SweeperHeight = 0.55f;
        public const float SweeperWidth = 0.35f;
        /// <summary>Gauntlet: below this (under the stones' tops) a player has fallen.</summary>
        public const float FallDepth = 5f;
        /// <summary>Echo Runes: the dividing wall's height (taller than a jump, so each side sees one set).</summary>
        public const float DividerHeight = 5f;
        /// <summary>Echo Runes: the divider runs from here to the north wall; south of it the halves meet.</summary>
        public const float DividerSouth = -11f;
        /// <summary>Hold the Core: the core's pillar (the crystal floats on top).</summary>
        public const float CoreHeight = 2.4f;

        public RaidRoomKind Kind { get; }
        public uint Seed { get; }
        public IReadOnlyList<RaidBox> Boxes { get; }
        /// <summary>Half extents of the room's space (x, z), for "is this player in the room".</summary>
        public Vector2 HalfExtents { get; }
        public Vector3 Portal { get; }
        /// <summary>Where players arrive.</summary>
        public IReadOnlyList<Vector3> Spawns { get; }
        /// <summary>
        /// Color Locks: crystal centres (Yellow, Red, Green). Hold the Plates: plate centres. Echo Runes: rune
        /// centres, set 0 (west) then set 1 (east). Hold the Core: the core crystal, then the three gates.
        /// Sentinel Eye: the shell's centre.
        /// </summary>
        public IReadOnlyList<Vector3> Targets { get; }
        /// <summary>Gauntlet: the stones in walking order, start pad first and end pad last.</summary>
        public IReadOnlyList<RaidBox> Path { get; }
        public IReadOnlyList<RaidSweeper> Sweepers { get; }
        /// <summary>Where adds come in (on the floor, away from the spawns).</summary>
        public IReadOnlyList<Vector3> AddSpots { get; }

        private RaidRoomLayout(RaidRoomKind kind, uint seed, List<RaidBox> boxes, Vector2 halfExtents, Vector3 portal, List<Vector3> spawns,
            List<Vector3> targets, List<RaidBox> path, List<RaidSweeper> sweepers, List<Vector3> addSpots)
        {
            Kind = kind;
            Seed = seed;
            Boxes = boxes;
            HalfExtents = halfExtents;
            Portal = portal;
            Spawns = spawns;
            Targets = targets;
            Path = path;
            Sweepers = sweepers;
            AddSpots = addSpots;
        }

        private static readonly Dictionary<(RaidRoomKind, uint), RaidRoomLayout> Cache = new Dictionary<(RaidRoomKind, uint), RaidRoomLayout>();

        /// <summary>The plain layout (seed 0).</summary>
        public static RaidRoomLayout For(RaidRoomKind kind) => For(kind, 0u);

        public static RaidRoomLayout For(RaidRoomKind kind, uint seed)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue((kind, seed), out var layout))
                {
                    if (Cache.Count > 64) Cache.Clear();
                    layout = Build(kind, seed);
                    Cache[(kind, seed)] = layout;
                }
                return layout;
            }
        }

        /// <summary>The rooms runs can pick (the boss stage isn't one).</summary>
        public static readonly RaidRoomKind[] Pool =
        {
            RaidRoomKind.ColorLocks, RaidRoomKind.HoldPlates, RaidRoomKind.Gauntlet, RaidRoomKind.WispStorm,
            RaidRoomKind.EchoRunes, RaidRoomKind.HoldCore, RaidRoomKind.Lanterns, RaidRoomKind.SentinelEye,
        };

        /// <summary>The highest room kind (platforms are stacked by kind).</summary>
        public const RaidRoomKind LastKind = RaidRoomKind.SentinelEye;

        public static string NameOf(RaidRoomKind kind)
        {
            switch (kind)
            {
                case RaidRoomKind.ColorLocks: return "Color Locks";
                case RaidRoomKind.HoldPlates: return "Hold the Plates";
                case RaidRoomKind.Gauntlet: return "Sweeper Gauntlet";
                case RaidRoomKind.WispStorm: return "Wisp Storm";
                case RaidRoomKind.EchoRunes: return "Echo Runes";
                case RaidRoomKind.HoldCore: return "Hold the Core";
                case RaidRoomKind.Lanterns: return "Lanterns";
                case RaidRoomKind.SentinelEye: return "Sentinel Eye";
                default: return "The Boss";
            }
        }

        public static string NameOf(RaidBossKind boss) =>
            boss == RaidBossKind.Twins ? "The Twins" : boss == RaidBossKind.Weaver ? "The Weaver" : "The Bulwark";

        public static string NameOf(RaidModifier modifier)
        {
            switch (modifier)
            {
                case RaidModifier.Swarming: return "Swarming";
                case RaidModifier.SwiftBolts: return "Swift Bolts";
                case RaidModifier.Brittle: return "Brittle";
                case RaidModifier.LowGravity: return "Low Gravity";
                case RaidModifier.Fog: return "Fog";
                default: return "";
            }
        }

        /// <summary>True if a point (relative to the room centre) is in the room's space.</summary>
        public bool Contains(Vector3 p, float below = 40f, float above = 40f) =>
            Math.Abs(p.X) <= HalfExtents.X + 1f && Math.Abs(p.Z) <= HalfExtents.Y + 1f && p.Y > -below && p.Y < above;

        private static Vector3 Ring(float angle, float radius, float y = 0f) =>
            new Vector3((float)Math.Sin(angle) * radius, y, (float)Math.Cos(angle) * radius);

        private static RaidRoomLayout Build(RaidRoomKind kind, uint seed)
        {
            var boxes = new List<RaidBox>();
            var spawns = new List<Vector3>();
            var targets = new List<Vector3>();
            var path = new List<RaidBox>();
            var sweepers = new List<RaidSweeper>();
            var adds = new List<Vector3>();
            // Seed 0 is the plain layout; any other seed moves things around.
            bool vary = seed != 0;
            var r = new CombatRandom(seed * 2246822519u + (uint)kind * 3266489917u | 1u);

            if (kind == RaidRoomKind.Gauntlet) return BuildGauntlet(seed, vary, r, boxes, spawns, path, sweepers);

            Square(boxes);
            if (kind == RaidRoomKind.EchoRunes)
            {
                // Arrive in the open south strip, where the two halves meet.
                for (int i = 0; i < 8; i++) spawns.Add(new Vector3(-7f + 2f * i, 0f, -17f));
            }
            else
            {
                for (int i = 0; i < 8; i++) spawns.Add(Ring((float)(2 * Math.PI * i / 8), 6f));
            }
            for (int i = 0; i < 8; i++) adds.Add(Ring((float)(2 * Math.PI * (i + 0.5f) / 8), 20f));
            var portal = new Vector3(0f, 0f, -SquareHalf + 4f);

            switch (kind)
            {
                case RaidRoomKind.ColorLocks:
                {
                    // Three crystal pillars on a ring, the altar (which shows the order) in the middle.
                    float turn = vary ? r.Angle() : 0f;
                    for (int i = 0; i < 3; i++)
                    {
                        float a = turn + (float)(2 * Math.PI * i / 3) + (vary ? r.Range(-0.35f, 0.35f) : 0f);
                        float radius = vary ? r.Range(11f, 16.5f) : 15f;
                        float height = vary ? r.Range(1.6f, 4.6f) : 3f;
                        var at = Ring(a, radius);
                        boxes.Add(new RaidBox(at + new Vector3(0f, height / 2f, 0f), new Vector3(2.2f, height, 2.2f), RaidBoxKind.Pillar));
                        targets.Add(at + new Vector3(0f, height + CrystalRadius + 0.2f, 0f));
                    }
                    boxes.Add(new RaidBox(new Vector3(0f, 0.6f, 0f), new Vector3(3f, 1.2f, 3f), RaidBoxKind.Pillar));
                    break;
                }
                case RaidRoomKind.HoldPlates:
                {
                    // Four plates around the room with low cover between neighbours.
                    float turn = vary ? r.Range(-0.6f, 0.6f) : 0f;
                    for (int i = 0; i < 4; i++)
                    {
                        float a = (float)(Math.PI / 4 + Math.PI / 2 * i) + turn + (vary ? r.Range(-0.25f, 0.25f) : 0f);
                        float radius = vary ? r.Range(13f, 19f) : 18.38f;
                        // Keep plates out of the portal's way (south) and inside the walls.
                        var p = Ring(a, radius);
                        p = new Vector3(Clamp(p.X, -19f, 19f), 0f, Clamp(p.Z, -16f, 19f));
                        targets.Add(p);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        var a = targets[i];
                        var b = targets[(i + 1) % 4];
                        var mid = (a + b) * 0.5f * (vary ? r.Range(0.85f, 1.05f) : 1f);
                        // Clear of the spawn ring in the middle.
                        if (mid.Length() < 9.5f) mid *= 9.5f / System.Math.Max(0.1f, mid.Length());
                        bool alongX = Math.Abs(a.X - b.X) > Math.Abs(a.Z - b.Z);
                        // A low wall halfway between the two plates, lying along the line between them.
                        var size = alongX ? new Vector3(4f, 1.2f, 1f) : new Vector3(1f, 1.2f, 4f);
                        boxes.Add(new RaidBox(new Vector3(mid.X, 0.6f, mid.Z), size, RaidBoxKind.Pillar));
                    }
                    break;
                }
                case RaidRoomKind.WispStorm:
                {
                    // Six pillars for cover.
                    float turn = vary ? r.Angle() : 0f;
                    for (int i = 0; i < 6; i++)
                    {
                        float a = turn + (float)(2 * Math.PI * (i + 0.5f) / 6) + (vary ? r.Range(-0.3f, 0.3f) : 0f);
                        float radius = vary ? r.Range(9f, 16f) : 12f;
                        float height = vary ? r.Range(2.5f, 5f) : 4f;
                        float width = vary ? r.Range(1.6f, 3f) : 2f;
                        var at = Ring(a, radius);
                        boxes.Add(new RaidBox(at + new Vector3(0f, height / 2f, 0f), new Vector3(width, height, width), RaidBoxKind.Pillar));
                    }
                    break;
                }
                case RaidRoomKind.EchoRunes:
                    BuildEchoRunes(vary, r, boxes, targets);
                    break;
                case RaidRoomKind.HoldCore:
                    BuildHoldCore(vary, r, boxes, targets);
                    break;
                case RaidRoomKind.Lanterns:
                {
                    // Tall pillars to break up the dark, so the light has corners to look around.
                    float turn = vary ? r.Angle() : 0f;
                    int count = vary ? 5 + r.NextInt(3) : 6;
                    for (int i = 0; i < count; i++)
                    {
                        float a = turn + (float)(2 * Math.PI * (i + 0.5f) / count) + (vary ? r.Range(-0.25f, 0.25f) : 0f);
                        float radius = vary ? r.Range(10f, 17f) : 13f;
                        float width = vary ? r.Range(1.8f, 3.2f) : 2.4f;
                        var at = Ring(a, radius);
                        boxes.Add(new RaidBox(at + new Vector3(0f, 3f, 0f), new Vector3(width, 6f, width), RaidBoxKind.Pillar));
                    }
                    break;
                }
                case RaidRoomKind.SentinelEye:
                {
                    // The shell floats over the middle; low walls around it to duck its bolts behind.
                    targets.Add(new Vector3(0f, SentinelEye.ShellHeight, 0f));
                    float turn = vary ? r.Angle() : (float)(Math.PI / 4);
                    int count = vary ? 4 + r.NextInt(3) : 4;
                    for (int i = 0; i < count; i++)
                    {
                        float a = turn + (float)(2 * Math.PI * i / count) + (vary ? r.Range(-0.3f, 0.3f) : 0f);
                        float radius = vary ? r.Range(11f, 17f) : 14f;
                        var at = Ring(a, radius);
                        // Lying across the line to the middle, so it covers someone facing the shell.
                        float yaw = a + (float)(Math.PI / 2);
                        bool alongX = Math.Abs(Math.Sin(yaw)) > Math.Abs(Math.Cos(yaw));
                        var size = alongX ? new Vector3(4f, 1.3f, 1f) : new Vector3(1f, 1.3f, 4f);
                        boxes.Add(new RaidBox(at + new Vector3(0f, 0.65f, 0f), size, RaidBoxKind.Pillar));
                    }
                    break;
                }
            }
            return new RaidRoomLayout(kind, seed, boxes, new Vector2(SquareHalf, SquareHalf), portal, spawns, targets, path, sweepers, adds);
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

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

        /// <summary>
        /// Echo Runes: a tall wall down the middle (from <see cref="DividerSouth"/> to the north wall) splits the
        /// room; each half has a slab of four runes on its outer wall. The pattern on the slab varies.
        /// </summary>
        private static void BuildEchoRunes(bool vary, CombatRandom r, List<RaidBox> boxes, List<Vector3> targets)
        {
            float north = SquareHalf;
            float len = north - DividerSouth;
            boxes.Add(new RaidBox(new Vector3(0f, DividerHeight / 2f, (north + DividerSouth) / 2f), new Vector3(1f, DividerHeight, len), RaidBoxKind.Wall));
            int pattern = vary ? r.NextInt(4) : 0;
            float slabZ = vary ? r.Range(4f, 9f) : 6f;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                float slabX = sx * (SquareHalf - 0.6f);
                boxes.Add(new RaidBox(new Vector3(slabX, 3.5f, slabZ), new Vector3(1.2f, 7f, 14f), RaidBoxKind.Backdrop));
                // Runes sit just in front of the slab, facing the middle.
                float x = slabX - sx * (0.6f + RuneRadius * 0.6f);
                for (int i = 0; i < EchoRunes.RunesPerSet; i++)
                {
                    float z, y;
                    switch (pattern)
                    {
                        case 1: // a 2 x 2 square
                            z = slabZ + (i % 2 == 0 ? -2.6f : 2.6f);
                            y = i < 2 ? 2.2f : 5f;
                            break;
                        case 2: // a diamond
                            z = slabZ + (i == 0 ? -4.2f : i == 3 ? 4.2f : 0f);
                            y = i == 1 ? 1.8f : i == 2 ? 5.4f : 3.6f;
                            break;
                        case 3: // a zigzag
                            z = slabZ - 4.8f + 3.2f * i;
                            y = i % 2 == 0 ? 2.2f : 4.8f;
                            break;
                        default: // a row
                            z = slabZ - 4.8f + 3.2f * i;
                            y = 3.4f;
                            break;
                    }
                    // Mirror the east set, so the two sides don't look alike.
                    if (side == 1) z = 2f * slabZ - z;
                    targets.Add(new Vector3(x, y, z));
                }
            }
        }

        /// <summary>
        /// Hold the Core: the core on a pillar in the middle, three gates (north, west, east) the runners come
        /// in from, and low cover blocks off the runners' lanes.
        /// </summary>
        private static void BuildHoldCore(bool vary, CombatRandom r, List<RaidBox> boxes, List<Vector3> targets)
        {
            boxes.Add(new RaidBox(new Vector3(0f, CoreHeight / 2f, 0f), new Vector3(1.6f, CoreHeight, 1.6f), RaidBoxKind.Pillar));
            targets.Add(new Vector3(0f, CoreHeight + 1f, 0f));
            float edge = SquareHalf - 2f;
            targets.Add(new Vector3(vary ? r.Range(-9f, 9f) : 0f, 0f, edge));
            targets.Add(new Vector3(-edge, 0f, vary ? r.Range(-6f, 12f) : 0f));
            targets.Add(new Vector3(edge, 0f, vary ? r.Range(-6f, 12f) : 0f));
            int blocks = vary ? 4 + r.NextInt(3) : 4;
            for (int i = 0, tries = 0; i < blocks && tries < 60; tries++)
            {
                var c = vary ? new Vector3(r.Range(-18f, 18f), 0f, r.Range(-14f, 18f)) : Ring((float)(Math.PI / 4 + Math.PI / 2 * i), 11f);
                if (c.Length() < 9.5f) continue; // clear of the spawn ring
                bool onLane = false;
                for (int g = 1; g <= 3; g++) if (DistanceToSegment(c, targets[g], Vector3.Zero) < 4f) onLane = true;
                if (onLane && vary) continue;
                boxes.Add(new RaidBox(new Vector3(c.X, 0.7f, c.Z), new Vector3(2.4f, 1.4f, 2.4f), RaidBoxKind.Pillar));
                i++;
            }
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = new Vector2(b.X - a.X, b.Z - a.Z);
            var ap = new Vector2(p.X - a.X, p.Z - a.Z);
            float t = Clamp(Vector2.Dot(ap, ab) / Math.Max(1e-4f, ab.LengthSquared()), 0f, 1f);
            return (ap - ab * t).Length();
        }

        /// <summary>Gauntlet stones: 5.5 m apart (gaps of about 2 m, a jump reaches 4 m), drifting side to side.</summary>
        public const int GauntletStones = 12;
        public const float StoneSize = 3.2f;
        public const float SweeperStoneSize = 4.4f;
        public const float StoneSpacing = 5.5f;

        private static RaidRoomLayout BuildGauntlet(uint seed, bool vary, CombatRandom r, List<RaidBox> boxes, List<Vector3> spawns,
            List<RaidBox> path, List<RaidSweeper> sweepers)
        {
            const float pad = 12f;
            var start = new RaidBox(new Vector3(0f, -1f, -40f), new Vector3(pad, 2f, pad), RaidBoxKind.Stone);
            var end = new RaidBox(new Vector3(0f, -1f, 40f), new Vector3(pad, 2f, pad), RaidBoxKind.Stone);
            // Which stones carry sweepers: four, never two in a row, never the first.
            var hasSweeper = new bool[GauntletStones];
            if (!vary)
            {
                hasSweeper[2] = hasSweeper[5] = hasSweeper[8] = hasSweeper[11] = true;
            }
            else
            {
                for (int placed = 0, tries = 0; placed < 4 && tries < 200; tries++)
                {
                    int i = 1 + r.NextInt(GauntletStones - 1);
                    if (hasSweeper[i] || hasSweeper[i - 1] || (i + 1 < GauntletStones && hasSweeper[i + 1])) continue;
                    hasSweeper[i] = true;
                    placed++;
                }
            }
            float sway = vary ? r.Range(1.2f, 2.8f) : 2f;
            float swayFreq = vary ? r.Range(0.8f, 1.7f) : 1.3f;
            float swayPhase = vary ? r.Angle() : 0f;
            float rise = vary ? r.Range(0.2f, 0.45f) : 0.35f;
            float spin = vary ? r.Range(1.8f, 2.5f) : 1.9f;
            path.Add(start);
            for (int i = 0; i < GauntletStones; i++)
            {
                bool sweeper = hasSweeper[i];
                float size = sweeper ? SweeperStoneSize : StoneSize;
                float x = sway * (float)Math.Sin(swayPhase + i * swayFreq);
                float top = rise * (float)Math.Sin(i * 2.1f + swayPhase);
                float z = -30.1f + StoneSpacing * i;
                var stone = new RaidBox(new Vector3(x, top - 1f, z), new Vector3(size, 2f, size), RaidBoxKind.Stone);
                path.Add(stone);
                if (sweeper)
                {
                    float speed = (i % 2 == 0 ? 1f : -1f) * (spin + 0.15f * i * (vary ? 0.5f : 1f));
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
            return new RaidRoomLayout(RaidRoomKind.Gauntlet, seed, boxes, new Vector2(10f, 48f), portal, spawns, new List<Vector3>(), path,
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
