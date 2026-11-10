using FriendlyPersonShooter.Core.Avatars;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.ZoneArt;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>The meshes of one robotic insect. Built in units of the enemy's hit radius (scale by it to draw).</summary>
    public sealed class BugParts
    {
        public BugLook Look;
        /// <summary>Everything that doesn't move on its own: shell, head, eyes, antennae (and the legs of bugs that don't walk).</summary>
        public Mesh Body;
        /// <summary>The legs in the two halves of a scuttle (null for bugs whose legs are part of the body).</summary>
        public Mesh LegsA, LegsB;
        /// <summary>One right-hand wing, root at the origin, reaching along +X, flat in XZ; null for wingless bugs.</summary>
        public Mesh Wing;
        /// <summary>The second pair's wing when it differs from the first (a butterfly's hindwing), else null.</summary>
        public Mesh Wing2;
        /// <summary>Where each right-hand wing pair is rooted (the left side mirrors it).</summary>
        public Vector3[] WingRoots;
    }

    /// <summary>
    /// Builds robotic insects from a <see cref="BugLook"/>, in the robots' low-poly style (see <see cref="RobotMeshes"/>):
    /// flat-shaded facets with vertex colours, a family-coloured shell over dark metal, and glowing eyes (vertex alpha
    /// below 1 glows with the ZoneArt vertex-colour shader). Each bug faces +Z with its hit sphere's centre at the origin
    /// and radius 1, so what you see stays close to what you can hit.
    /// </summary>
    public static class BugMeshes
    {
        private static readonly Color Dark = new Color(0.06f, 0.06f, 0.08f, 1f);

        public static BugParts Build(BugLook look, Color family)
        {
            Color.RGBToHSV(family, out float h, out float s, out float v);
            var shell = Color.HSVToRGB(Mathf.Repeat(h + look.HueShift, 1f), s, Mathf.Clamp01(v * look.Lightness));
            // A touch of glow keeps the essence colour readable against a backlit sky.
            shell.a = 0.94f;
            var band = Color.Lerp(shell, Color.black, 0.45f);
            band.a = 0.98f;
            var metal = Color.HSVToRGB(look.MetalHue, 0.15f, look.MetalLight);
            metal.a = 1f;
            var glow = Color.Lerp(Color.white, shell, 0.35f);
            glow.a = 0.3f;
            var c = new Palette { Shell = shell, Band = band, Metal = metal, Glow = glow };
            var parts = new BugParts { Look = look };
            switch (look.Family)
            {
                case EnemyFamily.Skitter: Beetle(parts, look, c); break;
                case EnemyFamily.Wisp: Firefly(parts, look, c); break;
                case EnemyFamily.Diver: Dragonfly(parts, look, c); break;
                case EnemyFamily.Sentinel: Mantis(parts, look, c); break;
                case EnemyFamily.Bulwark: PillBug(parts, look, c); break;
                case EnemyFamily.Glimmer: Butterfly(parts, look, c); break;
                default: Gnat(parts, look, c); break;
            }
            return parts;
        }

        private struct Palette
        {
            public Color Shell, Band, Metal, Glow;
        }

        // ---------------- Families ----------------

        /// <summary>Skitter: a beetle or a tick, low and wide, walking on its hit sphere's floor.</summary>
        private static void Beetle(BugParts parts, BugLook look, Palette c)
        {
            var b = new MeshBuilder();
            float w = 0.72f * look.Bulk, hgt = 0.5f * look.Hump, len = 0.78f * look.Length;
            // Wing cases (the abdomen), split down the middle, with bands across.
            Ellipsoid(b, new Vector3(0f, 0.02f, -0.3f), new Vector3(w, hgt, len), c.Shell);
            RobotMeshes.Box(b, RobotMeshes.M(new Vector3(0f, 0.02f + hgt * 0.97f, -0.3f), Quaternion.identity, new Vector3(0.05f, 0.04f, len * 1.6f)), Dark);
            for (int i = 0; i < look.Stripes; i++)
            {
                float z = -0.3f + len * (0.45f - i * 0.35f);
                Ellipsoid(b, new Vector3(0f, 0.02f, z), new Vector3(w * 1.01f, hgt * 1.01f, 0.06f), c.Band);
            }
            // A glowing core on the back between the wing cases.
            RobotMeshes.Ico(b, 0, RobotMeshes.M(new Vector3(0f, hgt * 0.85f, -0.05f), Quaternion.identity, new Vector3(0.12f, 0.06f, 0.12f)), c.Glow);
            // Thorax and head.
            Ellipsoid(b, new Vector3(0f, -0.05f, 0.42f), new Vector3(w * 0.75f, hgt * 0.75f, 0.3f), c.Shell);
            var head = new Vector3(0f, -0.12f, 0.42f + 0.3f + 0.12f);
            Ellipsoid(b, head, new Vector3(0.3f, 0.24f, 0.24f), c.Metal);
            Eyes(b, look, head, 0.27f, 0.07f, c.Glow);
            Mandibles(b, look, head + new Vector3(0f, -0.08f, 0.18f), 0.35f, c.Metal);
            if (look.Horn > 0f) Spike(b, head + new Vector3(0f, 0.15f, 0.08f), new Vector3(0f, 0.75f, 0.45f), 0.35f * look.Horn, 0.11f, c.Shell);
            Antennae(b, look, head + new Vector3(0f, 0.16f, 0.12f), 0.65f, c.Metal);
            parts.Body = b.ToMesh("Beetle");
            Legs(parts, look, c, look.LegPairs, 0.48f, -0.42f, w * 0.75f, -0.12f, -1f, 0.9f * look.LegLength, 0.045f);
        }

        /// <summary>Wisp: a firefly or a moth with a glowing lantern for an abdomen.</summary>
        private static void Firefly(BugParts parts, BugLook look, Palette c)
        {
            var b = new MeshBuilder();
            var lantern = c.Shell;
            lantern.a = 0.35f;
            Ellipsoid(b, new Vector3(0f, -0.1f, -0.35f * look.Length), new Vector3(0.36f * look.Bulk, 0.34f * look.Hump, 0.48f * look.Length), lantern);
            for (int i = 0; i < look.Segments - 1; i++)
            {
                float z = -0.35f * look.Length + 0.28f - i * 0.17f;
                Ellipsoid(b, new Vector3(0f, -0.1f, z), new Vector3(0.37f * look.Bulk, 0.35f * look.Hump, 0.035f), c.Metal);
            }
            Ellipsoid(b, new Vector3(0f, 0.04f, 0.12f), new Vector3(0.26f, 0.24f, 0.26f), c.Metal);
            var head = new Vector3(0f, 0.02f, 0.45f);
            Ellipsoid(b, head, new Vector3(0.2f, 0.18f, 0.18f), c.Metal);
            Eyes(b, look, head, 0.18f, 0.06f, c.Glow);
            Antennae(b, look, head + new Vector3(0f, 0.12f, 0.08f), 0.55f, c.Metal);
            // Short dangling legs (it never lands), part of the body.
            for (int i = 0; i < look.LegPairs; i++)
            {
                float z = 0.22f - i * 0.12f;
                for (int s = -1; s <= 1; s += 2)
                {
                    var hip = new Vector3(s * 0.15f, -0.14f, z);
                    var knee = hip + new Vector3(s * 0.12f, -0.12f, 0.05f) * look.LegLength;
                    var foot = knee + new Vector3(s * 0.02f, -0.22f, -0.04f) * look.LegLength;
                    Strut(b, hip, knee, 0.06f, c.Metal);
                    Strut(b, knee, foot, 0.045f, Dark);
                }
            }
            parts.Body = b.ToMesh("Firefly");
            var wingColor = Color.Lerp(c.Shell, Color.white, 0.55f);
            wingColor.a = 0.6f;
            parts.Wing = Wing(look.WingShape, 0.8f * look.WingLength, 0.42f, wingColor, c.Metal);
            parts.WingRoots = WingRoots(look.WingPairs, new Vector3(0.14f, 0.2f, 0.18f), 0.2f);
        }

        /// <summary>Diver: a dragonfly or a wasp. Its wings are drawn over the wing hit zones (see the view).</summary>
        private static void Dragonfly(BugParts parts, BugLook look, Palette c)
        {
            var b = new MeshBuilder();
            Ellipsoid(b, Vector3.zero, new Vector3(0.48f * look.Bulk, 0.5f * look.Hump, 0.62f), c.Shell);
            var head = new Vector3(0f, 0.08f, 0.85f);
            Ellipsoid(b, head, new Vector3(0.36f, 0.32f, 0.3f), c.Metal);
            // Big compound eyes, glowing.
            for (int s = -1; s <= 1; s += 2)
            {
                RobotMeshes.Ico(b, 1, RobotMeshes.M(head + new Vector3(s * 0.24f, 0.08f, 0.08f), Quaternion.identity, Vector3.one * 0.2f * look.EyeSize), c.Glow);
            }
            Mandibles(b, look, head + new Vector3(0f, -0.15f, 0.2f), 0.3f, c.Metal);
            Antennae(b, look, head + new Vector3(0f, 0.25f, 0.1f), 0.5f, c.Metal);
            // The long abdomen, segment by segment, tapering back.
            float total = 2.1f * look.Length;
            float step = total / look.Segments;
            for (int i = 0; i < look.Segments; i++)
            {
                float t = (i + 0.5f) / look.Segments;
                float r = Mathf.Lerp(0.26f, 0.12f, t) * look.Bulk;
                var color = i % 2 == 0 || look.Stripes == 0 ? c.Shell : c.Band;
                Ellipsoid(b, new Vector3(0f, -0.04f - t * 0.15f, -0.55f - (i + 0.5f) * step), new Vector3(r, r, step * 0.62f), color);
            }
            if (look.Horn > 0f)
            {
                // A wasp's stinger.
                var tail = new Vector3(0f, -0.2f, -0.55f - total);
                Spike(b, tail, new Vector3(0f, -0.25f, -1f), 0.45f * look.Horn, 0.08f, Dark);
            }
            // Legs tucked under the thorax.
            for (int i = 0; i < look.LegPairs; i++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    var hip = new Vector3(s * 0.2f, -0.35f, 0.3f - i * 0.25f);
                    var knee = hip + new Vector3(s * 0.25f, -0.15f, 0.12f) * look.LegLength;
                    var foot = knee + new Vector3(s * -0.05f, -0.25f, 0.25f) * look.LegLength;
                    Strut(b, hip, knee, 0.08f, c.Metal);
                    Strut(b, knee, foot, 0.06f, Dark);
                }
            }
            parts.Body = b.ToMesh("Dragonfly");
            var wingColor = Color.Lerp(c.Shell, Color.white, 0.4f);
            wingColor.a = 0.5f;
            // Long enough to cover the wing hit zone, which sits about 1.9 body radii out.
            parts.Wing = Wing(look.WingShape, 2.5f * look.WingLength, 0.75f, wingColor, c.Metal);
            parts.WingRoots = WingRoots(look.WingPairs, new Vector3(0.3f, 0.3f, 0.25f), 0.45f);
        }

        /// <summary>Sentinel: an upright mantis. Its eye (the weak point) is drawn by the view at the eye's hit spot.</summary>
        private static void Mantis(BugParts parts, BugLook look, Palette c)
        {
            var b = new MeshBuilder();
            // Ground is BodyHeight below the hit sphere's centre: 1.6 m at a 0.85 m radius.
            const float ground = -1.88f;
            // Thorax: a tall, leaning trunk.
            Strut(b, new Vector3(0f, -0.65f, -0.05f), new Vector3(0f, 0.35f, 0.25f), 0.6f * look.Bulk, c.Shell);
            Ellipsoid(b, new Vector3(0f, -0.55f, -0.05f), new Vector3(0.36f * look.Bulk, 0.3f, 0.36f), c.Shell);
            // Abdomen: segments trailing back and down.
            for (int i = 0; i < look.Segments; i++)
            {
                float t = i / (float)Mathf.Max(1, look.Segments - 1);
                var at = Vector3.Lerp(new Vector3(0f, -0.75f, -0.35f), new Vector3(0f, -1.05f, -1.15f * look.Length), t);
                var color = i % 2 == 0 || look.Stripes == 0 ? c.Shell : c.Band;
                Ellipsoid(b, at, new Vector3(0.3f * look.Bulk, 0.26f, 0.26f) * Mathf.Lerp(1f, 0.75f, t), color);
            }
            if (look.WingPairs > 0)
            {
                // Folded wings along the back.
                var wing = c.Band;
                wing.a = 0.8f;
                RobotMeshes.Box(b, RobotMeshes.M(new Vector3(0f, -0.45f, -0.55f), Quaternion.Euler(-20f, 0f, 0f), new Vector3(0.5f * look.Bulk, 0.04f, 1.1f * look.WingLength)), wing);
            }
            // Neck and head (just behind the eye's spot at 0.85 forward, 0.53 up).
            var head = new Vector3(0f, 0.53f, 0.52f);
            Strut(b, new Vector3(0f, 0.3f, 0.22f), head, 0.16f, c.Metal);
            RobotMeshes.Prism(b, 3, 1f, 0.55f, RobotMeshes.M(head, Quaternion.Euler(90f, 0f, 180f), new Vector3(0.62f, 0.36f, 0.42f)), c.Metal);
            for (int s = -1; s <= 1; s += 2)
            {
                // Small side eyes; the big centre one is the weak point the view draws.
                RobotMeshes.Ico(b, 0, RobotMeshes.M(head + new Vector3(s * 0.28f, 0.1f, 0.05f), Quaternion.identity, Vector3.one * 0.09f * look.EyeSize), c.Glow);
            }
            if (look.Horn > 0f) Spike(b, head + new Vector3(0f, 0.15f, -0.05f), new Vector3(0f, 1f, -0.3f), 0.4f * look.Horn, 0.1f, c.Shell);
            Mandibles(b, look, head + new Vector3(0f, -0.2f, 0.12f), 0.25f, c.Metal);
            Antennae(b, look, head + new Vector3(0f, 0.2f, 0.05f), 1f, c.Metal);
            // Raptorial arms, folded in front like a prayer.
            for (int s = -1; s <= 1; s += 2)
            {
                var shoulder = new Vector3(s * 0.22f, 0.12f, 0.3f);
                var elbow = new Vector3(s * 0.3f, -0.4f, 0.8f);
                var claw = new Vector3(s * 0.2f, 0.05f, 0.82f);
                Strut(b, shoulder, elbow, 0.12f, c.Shell);
                Strut(b, elbow, claw, 0.09f, c.Metal);
                for (int k = 0; k < 3; k++)
                {
                    var at = Vector3.Lerp(elbow, claw, 0.25f + k * 0.25f);
                    Spike(b, at, new Vector3(0f, -0.3f, 1f), 0.12f, 0.04f, Dark);
                }
            }
            parts.Body = b.ToMesh("Mantis");
            Legs(parts, look, c, 2, 0.05f, -0.45f, 0.25f, -0.6f, ground, 1.45f * look.LegLength, 0.07f);
        }

        /// <summary>A roaming Bulwark: an armoured pill bug, its shell in plates. The view draws the weak-point plate on its nose.</summary>
        private static void PillBug(BugParts parts, BugLook look, Palette c)
        {
            var b = new MeshBuilder();
            float w = 0.95f * look.Bulk, hgt = 0.78f * look.Hump;
            float front = 0.72f, back = -0.9f * look.Length;
            float plate = (front - back) / look.Segments;
            for (int i = 0; i < look.Segments; i++)
            {
                float t = (i + 0.5f) / look.Segments;
                float z = front - (i + 0.5f) * plate;
                // Domed: tallest in the middle, lower at the ends.
                float dome = Mathf.Lerp(0.72f, 1f, Mathf.Sin(t * Mathf.PI));
                var color = i % 2 == 0 || look.Stripes == 0 ? c.Shell : c.Band;
                Ellipsoid(b, new Vector3(0f, -0.15f, z), new Vector3(w * dome, hgt * dome, plate * 0.72f), color);
                // A dark seam between plates.
                if (i > 0) Ellipsoid(b, new Vector3(0f, -0.16f, z + plate * 0.5f), new Vector3(w * dome * 0.98f, hgt * dome * 0.98f, 0.03f), Dark);
                if (look.Horn > 0f && i % 2 == 1)
                {
                    Spike(b, new Vector3(0f, -0.15f + hgt * dome * 0.95f, z), Vector3.up, 0.25f * look.Horn, 0.1f, c.Metal);
                }
            }
            // Head under the front plate (the weak-point plate covers its face).
            var head = new Vector3(0f, -0.62f, 0.75f);
            Ellipsoid(b, head, new Vector3(0.4f, 0.22f, 0.25f), c.Metal);
            Eyes(b, look, head + new Vector3(0f, 0.05f, 0f), 0.24f, 0.07f, c.Glow);
            Mandibles(b, look, head + new Vector3(0f, -0.06f, 0.18f), 0.3f, c.Metal);
            Antennae(b, look, head + new Vector3(0f, 0.08f, 0.15f), 0.6f, c.Metal);
            parts.Body = b.ToMesh("PillBug");
            Legs(parts, look, c, look.LegPairs, 0.55f, -0.75f, w * 0.72f, -0.62f, -1f, 0.55f * look.LegLength, 0.06f);
        }

        /// <summary>The colours a Glimmer can drop (any of the six essences), painted on its wings.</summary>
        private static readonly Color[] GlimmerPaint =
        {
            new Color(1f, 0.85f, 0.2f), new Color(1f, 0.2f, 0.15f), new Color(0.3f, 1f, 0.35f),
            new Color(0.25f, 0.5f, 0.95f), new Color(0.6f, 0.3f, 0.85f), new Color(1f, 0.5f, 0.1f),
        };

        /// <summary>A Glimmer: a little butterfly whose wings show the colours it may drop, in a different order on each.</summary>
        private static void Butterfly(BugParts parts, BugLook look, Palette c)
        {
            var b = new MeshBuilder();
            // A slim segmented body under the wings.
            for (int i = 0; i < look.Segments; i++)
            {
                float t = (i + 0.5f) / look.Segments;
                float r = Mathf.Lerp(0.13f, 0.07f, t) * look.Bulk;
                Ellipsoid(b, new Vector3(0f, 0f, 0.1f - t * 0.75f * look.Length), new Vector3(r, r, 0.75f * look.Length / look.Segments * 0.65f), i % 2 == 0 ? c.Metal : c.Shell);
            }
            Ellipsoid(b, new Vector3(0f, 0.02f, 0.22f), new Vector3(0.14f, 0.13f, 0.16f), c.Metal);
            var head = new Vector3(0f, 0.03f, 0.42f);
            Ellipsoid(b, head, new Vector3(0.11f, 0.1f, 0.1f), c.Metal);
            Eyes(b, look, head, 0.13f, 0.05f, c.Glow);
            Antennae(b, look, head + new Vector3(0f, 0.07f, 0.05f), 0.6f, c.Metal);
            for (int i = 0; i < look.LegPairs; i++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    var hip = new Vector3(s * 0.08f, -0.08f, 0.3f - i * 0.08f);
                    var knee = hip + new Vector3(s * 0.12f, -0.06f, 0.04f) * look.LegLength;
                    var foot = knee + new Vector3(s * 0.03f, -0.16f, -0.02f) * look.LegLength;
                    Strut(b, hip, knee, 0.035f, c.Metal);
                    Strut(b, knee, foot, 0.028f, Dark);
                }
            }
            parts.Body = b.ToMesh("Butterfly");
            // Three bands on each forewing and hindwing: all six colours, starting at a different one on each Glimmer.
            int k = (int)(look.Phase * 1000f) % GlimmerPaint.Length;
            Color[] Bands(int first)
            {
                var bands = new Color[3];
                for (int i = 0; i < 3; i++)
                {
                    bands[i] = GlimmerPaint[(k + first + i) % GlimmerPaint.Length];
                    bands[i].a = 0.92f;
                }
                return bands;
            }
            parts.Wing = Wing(look.WingShape, 0.95f * look.WingLength, 0.85f, Bands(0), c.Metal);
            parts.Wing2 = Wing(BugWings.Round, 0.75f * look.WingLength, 0.65f, Bands(3), c.Metal);
            // Forewings tilt forward and hindwings back, as one butterfly-shaped spread.
            parts.WingRoots = new[] { new Vector3(0.08f, 0.1f, 0.25f), new Vector3(0.08f, 0.08f, 0.0f) };
        }

        /// <summary>A Mite: a tiny gnat or flea, all in one mesh (swarms have many).</summary>
        private static void Gnat(BugParts parts, BugLook look, Palette c)
        {
            var b = new MeshBuilder();
            var body = c.Shell;
            body.a = 0.75f;
            Ellipsoid(b, new Vector3(0f, 0f, -0.45f), new Vector3(0.42f * look.Bulk, 0.42f * look.Hump, 0.6f * look.Length), body);
            Ellipsoid(b, new Vector3(0f, 0.05f, 0.2f), new Vector3(0.36f, 0.34f, 0.32f), c.Shell);
            var head = new Vector3(0f, 0f, 0.65f);
            Ellipsoid(b, head, new Vector3(0.26f, 0.24f, 0.22f), c.Metal);
            Eyes(b, look, head, 0.22f, 0.1f, c.Glow);
            Antennae(b, look, head + new Vector3(0f, 0.15f, 0.1f), 0.7f, c.Metal);
            Mandibles(b, look, head + new Vector3(0f, -0.1f, 0.15f), 0.3f, c.Metal);
            for (int i = 0; i < look.LegPairs; i++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    var hip = new Vector3(s * 0.25f, -0.15f, 0.3f - i * 0.25f);
                    var knee = hip + new Vector3(s * 0.35f, 0.15f, 0f) * look.LegLength;
                    var foot = knee + new Vector3(s * 0.15f, -0.55f, 0.05f) * look.LegLength;
                    Strut(b, hip, knee, 0.07f, c.Metal);
                    Strut(b, knee, foot, 0.05f, Dark);
                }
            }
            if (look.WingPairs > 0)
            {
                var wing = Color.Lerp(c.Shell, Color.white, 0.3f);
                wing.a = 0.7f;
                for (int s = -1; s <= 1; s += 2)
                {
                    var root = new Vector3(s * 0.15f, 0.3f, 0.15f);
                    RobotMeshes.Ico(b, 0, RobotMeshes.M(root + new Vector3(s * 0.4f, 0.2f, -0.25f) * look.WingLength,
                        Quaternion.Euler(-25f, s * 30f, s * -30f), new Vector3(0.38f, 0.03f, 0.6f) * look.WingLength), wing);
                }
            }
            parts.Body = b.ToMesh("Gnat");
        }

        // ---------------- Pieces ----------------

        /// <summary>A low-poly ellipsoid.</summary>
        private static void Ellipsoid(MeshBuilder b, Vector3 center, Vector3 radii, Color color) =>
            RobotMeshes.Ico(b, 1, RobotMeshes.M(center, Quaternion.identity, radii), color);

        /// <summary>A six-sided strut between two points.</summary>
        private static void Strut(MeshBuilder b, Vector3 a, Vector3 c, float thickness, Color color)
        {
            var d = c - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            RobotMeshes.Prism(b, 6, 1f, 0.8f, RobotMeshes.M((a + c) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len), new Vector3(thickness, len, thickness)), color);
        }

        /// <summary>A cone from <paramref name="at"/> pointing along <paramref name="dir"/>.</summary>
        private static void Spike(MeshBuilder b, Vector3 at, Vector3 dir, float length, float width, Color color)
        {
            dir.Normalize();
            RobotMeshes.Prism(b, 5, 1f, 0f, RobotMeshes.M(at + dir * (length * 0.5f), Quaternion.FromToRotation(Vector3.up, dir), new Vector3(width, length, width)), color);
        }

        /// <summary>Glowing eyes in pairs across the front of the head.</summary>
        private static void Eyes(MeshBuilder b, BugLook look, Vector3 head, float spread, float size, Color glow)
        {
            int rows = look.Eyes / 2;
            for (int r = 0; r < rows; r++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = s * spread * (0.55f - r * 0.12f);
                    var at = head + new Vector3(x, 0.05f + r * 0.08f, spread * (0.55f - r * 0.15f));
                    float k = r == 0 ? 1f : 0.65f;
                    RobotMeshes.Ico(b, 0, RobotMeshes.M(at, Quaternion.identity, Vector3.one * size * look.EyeSize * k), glow);
                }
            }
        }

        private static void Mandibles(MeshBuilder b, BugLook look, Vector3 at, float scale, Color metal)
        {
            if (look.Mandibles <= 0f) return;
            float m = look.Mandibles * scale;
            for (int s = -1; s <= 1; s += 2)
            {
                var root = at + new Vector3(s * 0.4f * m, 0f, 0f);
                var mid = root + new Vector3(s * 0.25f * m, 0f, 0.6f * m);
                var tip = mid + new Vector3(-s * 0.35f * m, 0f, 0.35f * m);
                Strut(b, root, mid, 0.18f * m, metal);
                Strut(b, mid, tip, 0.12f * m, Dark);
            }
        }

        private static void Antennae(MeshBuilder b, BugLook look, Vector3 at, float scale, Color metal)
        {
            if (look.Antennae == BugAntennae.None) return;
            float len = look.AntennaLength * scale;
            for (int s = -1; s <= 1; s += 2)
            {
                var root = at + new Vector3(s * 0.08f, 0f, 0f);
                var mid = root + new Vector3(s * 0.3f, 0.55f, 0.45f) * (len * 0.5f);
                Strut(b, root, mid, 0.06f, metal);
                switch (look.Antennae)
                {
                    case BugAntennae.Clubs:
                        RobotMeshes.Ico(b, 0, RobotMeshes.M(mid, Quaternion.identity, Vector3.one * 0.07f), Dark);
                        break;
                    case BugAntennae.Hooks:
                        var bend = mid + new Vector3(s * 0.25f, -0.1f, 0.3f) * (len * 0.4f);
                        Strut(b, mid, bend, 0.045f, Dark);
                        break;
                    case BugAntennae.Feathers:
                        var tip = mid + new Vector3(s * 0.2f, 0.3f, 0.4f) * (len * 0.5f);
                        Strut(b, mid, tip, 0.025f, metal);
                        for (int k = 1; k <= 3; k++)
                        {
                            var on = Vector3.Lerp(root, tip, k / 4f);
                            Strut(b, on, on + new Vector3(s * 0.12f, 0.02f, -0.05f) * len, 0.02f, Dark);
                        }
                        break;
                    default:
                        var end = mid + new Vector3(s * 0.15f, 0.25f, 0.55f) * (len * 0.5f);
                        Strut(b, mid, end, 0.045f, Dark);
                        break;
                }
            }
        }

        /// <summary>
        /// Walking legs in two scuttle poses: alternate legs swing forward and back (a tripod gait).
        /// Hips run from <paramref name="front"/> to <paramref name="back"/> along Z, at <paramref name="hipX"/> out
        /// and <paramref name="hipY"/> up; feet land on <paramref name="ground"/>, <paramref name="reach"/> out.
        /// </summary>
        private static void Legs(BugParts parts, BugLook look, Palette c, int pairs, float front, float back, float hipX, float hipY,
            float ground, float reach, float thick)
        {
            parts.LegsA = LegPose(look, c, pairs, front, back, hipX, hipY, ground, reach, thick, 1f);
            parts.LegsB = LegPose(look, c, pairs, front, back, hipX, hipY, ground, reach, thick, -1f);
        }

        private static Mesh LegPose(BugLook look, Palette c, int pairs, float front, float back, float hipX, float hipY, float ground, float reach,
            float thick, float pose)
        {
            var b = new MeshBuilder();
            float t = thick * look.LegThickness * 1.8f;
            for (int i = 0; i < pairs; i++)
            {
                float k = pairs == 1 ? 0.5f : i / (float)(pairs - 1);
                float z = Mathf.Lerp(front, back, k);
                // Splay: front legs point forward, back legs back.
                float splay = Mathf.Lerp(35f, -35f, k);
                for (int s = -1; s <= 1; s += 2)
                {
                    float swing = ((i + (s > 0 ? 1 : 0)) % 2 == 0 ? 1f : -1f) * pose * 14f;
                    float yaw = (splay + swing) * s;
                    var outward = Quaternion.Euler(0f, yaw, 0f) * new Vector3(s, 0f, 0f);
                    var hip = new Vector3(s * hipX, hipY, z);
                    // Legs swinging forward lift their foot a little.
                    float lift = swing * pose > 0f ? 0.06f : 0f;
                    var foot = new Vector3(hip.x + outward.x * reach, ground + lift, hip.z + outward.z * reach);
                    var knee = Vector3.Lerp(hip, foot, 0.45f) + Vector3.up * (reach * 0.32f + (hip.y - ground) * 0.12f);
                    Strut(b, hip, knee, t * 1.2f, c.Metal);
                    RobotMeshes.Ico(b, 0, RobotMeshes.M(knee, Quaternion.identity, Vector3.one * t * 0.9f), c.Shell);
                    Strut(b, knee, foot, t, Dark);
                }
            }
            return b.ToMesh("BugLegs");
        }

        /// <summary>One wing, root at the origin, along +X, flat in XZ, both faces drawn.</summary>
        private static Mesh Wing(BugWings shape, float length, float width, Color membrane, Color vein) =>
            Wing(shape, length, width, new[] { membrane }, vein);

        /// <summary>A wing in bands of colour from root to tip.</summary>
        private static Mesh Wing(BugWings shape, float length, float width, Color[] bands, Color vein)
        {
            var b = new MeshBuilder();
            const int n = 7;
            var outline = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float x = t * length;
                float half;
                switch (shape)
                {
                    case BugWings.Long: half = width * 0.5f * Mathf.Sin(Mathf.Lerp(0.25f, 1f, t) * Mathf.PI) * 0.55f + 0.02f; break;
                    case BugWings.Swept: half = width * 0.5f * (1f - t * 0.8f); break;
                    default: half = width * 0.5f * Mathf.Sin(Mathf.Lerp(0.2f, 1f, t) * Mathf.PI); break;
                }
                float sweep = shape == BugWings.Swept ? -t * length * 0.35f : 0f;
                outline[i] = new Vector3(x, 0f, half + sweep);
                outline[n * 2 - 1 - i] = new Vector3(x, 0f, -half * 0.8f + sweep);
            }
            for (int i = 0; i < n - 1; i++)
            {
                var a = outline[i];
                var bb = outline[i + 1];
                var cc = outline[n * 2 - 2 - i];
                var d = outline[n * 2 - 1 - i];
                var col = bands[i * bands.Length / (n - 1)];
                b.FlatTriangle(a, bb, cc, col);
                b.FlatTriangle(a, cc, d, col);
                b.FlatTriangle(a, cc, bb, col);
                b.FlatTriangle(a, d, cc, col);
            }
            // A vein along the leading edge.
            Strut(b, Vector3.zero, outline[n - 1] * 0.95f, 0.03f, vein);
            return b.ToMesh("BugWing");
        }

        private static Vector3[] WingRoots(int pairs, Vector3 first, float gap)
        {
            var roots = new Vector3[pairs];
            for (int i = 0; i < pairs; i++) roots[i] = first + new Vector3(0f, -0.02f * i, -gap * i);
            return roots;
        }
    }
}
