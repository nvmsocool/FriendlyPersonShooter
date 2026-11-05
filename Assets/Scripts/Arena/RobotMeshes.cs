using System.Collections.Generic;
using FriendlyPersonShooter.Core.Avatars;
using FriendlyPersonShooter.ZoneArt;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>The meshes and attachment points of one robot avatar.</summary>
    public sealed class RobotParts
    {
        public RobotLook Look;
        /// <summary>Torso, chest plate, shoulder orbs, neck and thrusters; origin at the body's centre.</summary>
        public Mesh Body;
        /// <summary>Head, face and ears; origin at the bottom of the head (where it sits on the neck).</summary>
        public Mesh Head;
        /// <summary>One floating hand (symmetric, so it serves as left and right); origin at the palm, fingers along +Z.</summary>
        public Mesh Hand;
        /// <summary>A propeller that spins on top of the head, or null.</summary>
        public Mesh Spinner;
        /// <summary>Where the propeller sits, in head space.</summary>
        public Vector3 SpinnerAt;
        /// <summary>Body half width and half height (metres).</summary>
        public float BodyRadius, BodyHalfHeight;
        /// <summary>Where the head sits, in body space.</summary>
        public Vector3 HeadAt;
    }

    /// <summary>
    /// Builds goofy floating robots from a <see cref="RobotLook"/>, in the zones' low-poly style: flat-shaded
    /// facets with vertex colours, and glowing eyes, chest light and thrusters (vertex alpha below 1 glows
    /// with the ZoneArt vertex-colour shader). The main accent is the player's team colour.
    /// </summary>
    public static class RobotMeshes
    {
        private static readonly Color Dark = new Color(0.07f, 0.08f, 0.1f, 1f);
        private static readonly Color Tooth = new Color(0.96f, 0.95f, 0.9f, 1f);

        public static RobotParts Build(uint seed, Color accent)
        {
            var look = new RobotLook(seed);
            accent.a = 1f;
            var shell = Color.HSVToRGB(look.ShellHue, look.ShellTint, look.ShellLight);
            var glow = Color.HSVToRGB(look.GlowHue, 0.65f, 1f);
            glow.a = 0.3f;
            var parts = new RobotParts { Look = look };
            // Pupil directions for googly eyes: fixed per robot, never quite the same way.
            var rng = new System.Random((int)seed);

            BuildBody(parts, look, shell, accent, glow);
            BuildHead(parts, look, shell, accent, glow, rng);
            BuildHand(parts, look, shell, accent);
            return parts;
        }

        // ---------------- Body ----------------

        private static void BuildBody(RobotParts parts, RobotLook look, Color shell, Color accent, Color glow)
        {
            var b = new MeshBuilder();
            float bw = 0.3f * look.BodyWidth;
            float bh = 0.28f * look.BodyHeight;
            float front;
            switch (look.Body)
            {
                case RobotBody.Barrel:
                    Prism(b, 8, 1f, 0.85f, M(Vector3.zero, Quaternion.Euler(0f, 22.5f, 0f), new Vector3(2f * bw, 2f * bh, 1.8f * bw)), shell);
                    Prism(b, 8, 1f, 1f, M(new Vector3(0f, bh * 0.55f, 0f), Quaternion.Euler(0f, 22.5f, 0f), new Vector3(2.08f * bw * 0.93f, 0.06f, 1.86f * bw * 0.93f)), accent);
                    Prism(b, 8, 1f, 1f, M(new Vector3(0f, -bh * 0.55f, 0f), Quaternion.Euler(0f, 22.5f, 0f), new Vector3(2.08f * bw, 0.06f, 1.86f * bw)), accent);
                    front = 0.9f * bw * 0.92f;
                    break;
                case RobotBody.Cone:
                    // Wide shoulders, narrow bottom.
                    Prism(b, 8, 0.45f, 1f, M(Vector3.zero, Quaternion.Euler(0f, 22.5f, 0f), new Vector3(2.2f * bw, 2f * bh, 2f * bw)), shell);
                    front = 0.75f * bw;
                    break;
                case RobotBody.Ball:
                    bh = bw = 0.8f * Mathf.Max(bw, bh);
                    Ico(b, 1, M(Vector3.zero, Quaternion.identity, new Vector3(bw, bh, bw)), shell);
                    front = bw;
                    break;
                default:
                    Egg(b, bw, bh, shell);
                    front = 0.9f * bw;
                    break;
            }
            // Chest plate in the team colour with a glowing light.
            Box(b, M(new Vector3(0f, bh * 0.1f, front - 0.04f), Quaternion.identity, new Vector3(bw * 0.95f, bh * 0.75f, 0.1f)), accent);
            Ico(b, 0, M(new Vector3(0f, bh * 0.15f, front + 0.02f), Quaternion.identity, new Vector3(0.055f, 0.055f, 0.03f)), glow);
            // Shoulder orbs, where arms would be: the hands float free.
            for (int s = -1; s <= 1; s += 2)
            {
                Ico(b, 0, M(new Vector3(s * (bw + 0.015f), bh * 0.35f, 0f), Quaternion.identity, Vector3.one * 0.065f), accent);
            }
            // Neck: short and dark, or a long goofy spring.
            float neck = look.Neck;
            if (neck > 0.1f)
            {
                int coils = Mathf.Max(3, Mathf.RoundToInt(neck / 0.04f));
                for (int i = 0; i < coils; i++)
                {
                    float y = bh + (i + 0.5f) * neck / coils;
                    Prism(b, 6, 1f, 1f, M(new Vector3(0f, y, 0f), Quaternion.identity, new Vector3(i % 2 == 0 ? 0.13f : 0.09f, neck / coils * 0.9f, i % 2 == 0 ? 0.13f : 0.09f)), i % 2 == 0 ? Dark : accent);
                }
            }
            else
            {
                Prism(b, 6, 1f, 1f, M(new Vector3(0f, bh + neck * 0.5f, 0f), Quaternion.identity, new Vector3(0.14f, neck + 0.04f, 0.14f)), Dark);
            }
            parts.HeadAt = new Vector3(0f, bh + neck, 0f);

            // Thrusters instead of legs, with a glowing flame.
            float jetScale = look.TwinJets ? 0.75f : 1f;
            for (int s = 0; s < (look.TwinJets ? 2 : 1); s++)
            {
                float x = look.TwinJets ? (s == 0 ? -1f : 1f) * bw * 0.45f : 0f;
                float y = -bh * (look.Body == RobotBody.Cone ? 0.95f : 0.85f);
                Prism(b, 6, 0.65f, 1f, M(new Vector3(x, y - 0.05f * jetScale, 0f), Quaternion.identity, new Vector3(0.18f, 0.13f, 0.18f) * jetScale), Dark);
                Prism(b, 6, 0f, 1f, M(new Vector3(x, y - 0.2f * jetScale, 0f), Quaternion.Euler(180f, 0f, 0f), new Vector3(0.12f, 0.2f, 0.12f) * jetScale), glow);
            }

            parts.BodyRadius = bw;
            parts.BodyHalfHeight = bh;
            parts.Body = b.ToMesh("RobotBody");
        }

        /// <summary>An egg: an icosphere that is wider at the bottom.</summary>
        private static void Egg(MeshBuilder b, float bw, float bh, Color color)
        {
            var (verts, tris) = MeshBuilder.Icosphere(1);
            var v = new Vector3[verts.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = verts[i];
                float widen = 1f - 0.22f * p.y;
                v[i] = new Vector3(p.x * bw * widen, p.y * bh, p.z * bw * 0.9f * widen);
            }
            Convex(b, v, tris, Matrix4x4.identity, color);
        }

        // ---------------- Head ----------------

        private static void BuildHead(RobotParts parts, RobotLook look, Color shell, Color accent, Color glow, System.Random rng)
        {
            var b = new MeshBuilder();
            float hw = 0.26f * look.HeadWidth;
            float hh = 0.21f * look.HeadHeight;
            float hd = 0.23f * look.HeadDepth;
            float cy, halfW, halfH, face;
            switch (look.Head)
            {
                case RobotHead.Boxy:
                    halfW = hw * 0.9f; halfH = hh * 0.9f; cy = halfH; face = hd * 0.9f;
                    Box(b, M(new Vector3(0f, cy, 0f), Quaternion.identity, new Vector3(2f * halfW, 2f * halfH, 2f * face)), shell);
                    // A little bevel band around the middle.
                    Box(b, M(new Vector3(0f, cy - halfH * 0.85f, 0f), Quaternion.identity, new Vector3(2f * halfW + 0.03f, 0.04f, 2f * face + 0.03f)), accent);
                    break;
                case RobotHead.Can:
                    {
                        float r = Mathf.Max(hw, hd) * 0.85f;
                        halfW = r; halfH = hh * 1.1f; cy = halfH; face = r * 0.924f;
                        Prism(b, 8, 1f, 1f, M(new Vector3(0f, cy, 0f), Quaternion.Euler(0f, 22.5f, 0f), new Vector3(2f * r, 2f * halfH, 2f * r)), shell);
                        Prism(b, 8, 1f, 1f, M(new Vector3(0f, cy + halfH + 0.015f, 0f), Quaternion.Euler(0f, 22.5f, 0f), new Vector3(2f * r * 0.8f, 0.03f, 2f * r * 0.8f)), accent);
                        break;
                    }
                case RobotHead.Bean:
                    halfW = hw * 1.25f; halfH = hh * 0.8f; cy = halfH; face = hd;
                    Ico(b, 1, M(new Vector3(0f, cy, 0f), Quaternion.identity, new Vector3(halfW, halfH, face)), shell);
                    break;
                default:
                    halfW = hw; halfH = hh; cy = halfH; face = hd;
                    Ico(b, 1, M(new Vector3(0f, cy, 0f), Quaternion.identity, new Vector3(halfW, halfH, face)), shell);
                    break;
            }
            float top = cy + halfH;
            bool round = look.Head == RobotHead.Round || look.Head == RobotHead.Bean;
            float earY = round ? cy + halfH * 0.75f : top;

            // Face: a dark visor with glowing eyes (googly eyes go straight on the shell).
            float eyeY = cy + halfH * 0.12f;
            float faceFront = face + 0.02f;
            if (look.Eyes != RobotEyes.Googly)
            {
                Box(b, M(new Vector3(0f, eyeY, face - 0.03f), Quaternion.identity, new Vector3(halfW * 1.55f, halfH * 0.8f, 0.1f)), Dark);
            }
            switch (look.Eyes)
            {
                case RobotEyes.Cyclops:
                    Ico(b, 0, M(new Vector3(0f, eyeY, faceFront), Quaternion.identity, new Vector3(0.1f, 0.1f, 0.04f)), glow);
                    Ico(b, 0, M(new Vector3(0f, eyeY, faceFront + 0.03f), Quaternion.identity, new Vector3(0.04f, 0.04f, 0.02f)), Dark);
                    break;
                case RobotEyes.Triple:
                    for (int i = -1; i <= 1; i++)
                    {
                        float r = i == 0 ? 0.035f : 0.045f;
                        Ico(b, 0, M(new Vector3(i * halfW * 0.45f, eyeY + (i == 0 ? 0.025f : -0.01f), faceFront), Quaternion.identity, new Vector3(r, r, 0.025f)), glow);
                    }
                    break;
                case RobotEyes.Googly:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float e = (s < 0 ? look.LeftEye : look.RightEye) * 0.085f;
                        var c = new Vector3(s * halfW * 0.42f, eyeY, face * 0.95f + 0.01f);
                        Ico(b, 1, M(c, Quaternion.identity, new Vector3(e, e, 0.045f)), Tooth);
                        var off = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * e * 0.9f;
                        Ico(b, 0, M(c + new Vector3(off.x, off.y, 0.04f), Quaternion.identity, new Vector3(e * 0.45f, e * 0.45f, 0.02f)), Dark);
                    }
                    break;
                default:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float e = (s < 0 ? look.LeftEye : look.RightEye) * 0.05f;
                        Ico(b, 0, M(new Vector3(s * halfW * 0.4f, eyeY, faceFront), Quaternion.identity, new Vector3(e, e, 0.025f)), glow);
                    }
                    break;
            }

            // Mouth.
            float mouthY = cy - halfH * 0.5f;
            float mouthZ = (round ? face * 0.86f : face) + 0.01f;
            switch (look.Mouth)
            {
                case RobotMouth.Grin:
                    Box(b, M(new Vector3(0f, mouthY, mouthZ), Quaternion.identity, new Vector3(halfW * 0.6f, 0.03f, 0.04f)), glow);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Box(b, M(new Vector3(s * halfW * 0.33f, mouthY + 0.02f, mouthZ), Quaternion.Euler(0f, 0f, s * 40f), new Vector3(0.06f, 0.03f, 0.04f)), glow);
                    }
                    break;
                case RobotMouth.Oh:
                    Ico(b, 0, M(new Vector3(0f, mouthY, mouthZ), Quaternion.identity, new Vector3(0.035f, 0.045f, 0.03f)), Dark);
                    break;
                case RobotMouth.Teeth:
                    Box(b, M(new Vector3(0f, mouthY, mouthZ - 0.01f), Quaternion.identity, new Vector3(halfW * 0.7f, 0.065f, 0.04f)), Dark);
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i - 1.5f) * halfW * 0.15f;
                        Box(b, M(new Vector3(x, mouthY + (i % 2 == 0 ? 0.008f : -0.004f), mouthZ + 0.012f), Quaternion.identity, new Vector3(halfW * 0.12f, 0.042f, 0.025f)), Tooth);
                    }
                    break;
            }

            // Ears.
            switch (look.Ears)
            {
                case RobotEars.Cat:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var at = new Vector3(s * halfW * 0.62f, earY + 0.06f, -0.02f);
                        var rot = Quaternion.Euler(0f, 0f, -s * 20f);
                        Prism(b, 4, 1f, 0f, M(at, rot * Quaternion.Euler(0f, 45f, 0f), new Vector3(0.17f, 0.22f, 0.17f)), shell);
                        Prism(b, 4, 1f, 0f, M(at + rot * new Vector3(0f, -0.01f, 0.045f), rot * Quaternion.Euler(0f, 45f, 0f), new Vector3(0.1f, 0.15f, 0.04f)), accent);
                    }
                    break;
                case RobotEars.Antenna:
                    {
                        var rot = Quaternion.Euler(8f, 0f, -10f);
                        var at = new Vector3(0f, top - 0.02f, 0f);
                        Prism(b, 5, 1f, 1f, M(at + rot * new Vector3(0f, 0.15f, 0f), rot, new Vector3(0.025f, 0.3f, 0.025f)), Dark);
                        Ico(b, 0, M(at + rot * new Vector3(0f, 0.32f, 0f), Quaternion.identity, Vector3.one * 0.055f), glow);
                        Prism(b, 6, 1f, 1f, M(at, Quaternion.identity, new Vector3(0.08f, 0.05f, 0.08f)), accent);
                        break;
                    }
                case RobotEars.Propeller:
                    // A beanie in the team colour with a propeller on top.
                    Ico(b, 1, M(new Vector3(0f, top - 0.03f, 0f), Quaternion.identity, new Vector3(halfW * 0.75f, 0.07f, face * 0.75f)), accent);
                    Prism(b, 5, 1f, 1f, M(new Vector3(0f, top + 0.06f, 0f), Quaternion.identity, new Vector3(0.025f, 0.09f, 0.025f)), Dark);
                    parts.SpinnerAt = new Vector3(0f, top + 0.11f, 0f);
                    var sp = new MeshBuilder();
                    Box(sp, M(Vector3.zero, Quaternion.Euler(0f, 0f, 8f), new Vector3(0.42f, 0.015f, 0.07f)), Dark);
                    Box(sp, M(Vector3.zero, Quaternion.Euler(8f, 90f, 0f), new Vector3(0.42f, 0.015f, 0.07f)), accent);
                    Ico(sp, 0, M(new Vector3(0f, 0.015f, 0f), Quaternion.identity, Vector3.one * 0.03f), glow);
                    parts.Spinner = sp.ToMesh("RobotSpinner");
                    break;
                case RobotEars.Bunny:
                    {
                        // One ear up, one flopped over.
                        Ico(b, 0, M(new Vector3(-halfW * 0.35f, earY + 0.2f, 0f), Quaternion.Euler(0f, 0f, 8f), new Vector3(0.055f, 0.22f, 0.035f)), accent);
                        var baseAt = new Vector3(halfW * 0.35f, earY + 0.09f, 0f);
                        Ico(b, 0, M(baseAt, Quaternion.Euler(0f, 0f, -5f), new Vector3(0.055f, 0.12f, 0.035f)), accent);
                        Ico(b, 0, M(baseAt + new Vector3(0.1f, 0.12f, 0f), Quaternion.Euler(0f, 0f, -65f), new Vector3(0.05f, 0.13f, 0.033f)), accent);
                        break;
                    }
                case RobotEars.Headphones:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Prism(b, 8, 1f, 1f, M(new Vector3(s * (halfW + 0.03f), cy, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.17f, 0.07f, 0.17f)), accent);
                        Ico(b, 0, M(new Vector3(s * (halfW + 0.07f), cy, 0f), Quaternion.identity, new Vector3(0.02f, 0.05f, 0.05f)), glow);
                    }
                    Box(b, M(new Vector3(0f, top + 0.02f, 0f), Quaternion.identity, new Vector3(2f * halfW + 0.08f, 0.035f, 0.06f)), Dark);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Box(b, M(new Vector3(s * (halfW + 0.03f), (top + cy) * 0.5f + 0.03f, 0f), Quaternion.identity, new Vector3(0.035f, top - cy + 0.02f, 0.06f)), Dark);
                    }
                    break;
                case RobotEars.TwinAntennae:
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float len = s < 0 ? 0.18f : 0.32f;
                        var rot = Quaternion.Euler(0f, 0f, -s * 14f);
                        var at = new Vector3(s * halfW * 0.4f, earY, 0f);
                        Prism(b, 5, 1f, 1f, M(at + rot * new Vector3(0f, len * 0.5f, 0f), rot, new Vector3(0.022f, len, 0.022f)), Dark);
                        Ico(b, 0, M(at + rot * new Vector3(0f, len + 0.03f, 0f), Quaternion.identity, Vector3.one * 0.045f), glow);
                    }
                    break;
            }
            parts.Head = b.ToMesh("RobotHead");
        }

        // ---------------- Hand ----------------

        private static void BuildHand(RobotParts parts, RobotLook look, Color shell, Color accent)
        {
            var b = new MeshBuilder();
            float s = look.HandSize;
            if (look.Fingers)
            {
                // A white palm with three chunky dark fingers, like the reference robot.
                Box(b, M(Vector3.zero, Quaternion.identity, new Vector3(0.11f, 0.075f, 0.09f) * s), shell);
                for (int i = -1; i <= 1; i++)
                {
                    var at = new Vector3(i * 0.036f, -0.008f, 0.07f) * s;
                    Prism(b, 6, 1f, 1f, M(at, Quaternion.Euler(110f, 0f, 0f), new Vector3(0.033f, 0.075f, 0.033f) * s), Dark);
                }
                Prism(b, 6, 1f, 1f, M(new Vector3(0f, 0.045f, 0.03f) * s, Quaternion.Euler(60f, 0f, 0f), new Vector3(0.03f, 0.06f, 0.03f) * s), Dark);
            }
            else
            {
                // A big round mitt with a thumb on top.
                Ico(b, 1, M(new Vector3(0f, 0f, 0.01f) * s, Quaternion.identity, new Vector3(0.075f, 0.06f, 0.09f) * s), shell);
                Ico(b, 0, M(new Vector3(0f, 0.05f, 0.03f) * s, Quaternion.Euler(-30f, 0f, 0f), new Vector3(0.03f, 0.03f, 0.05f) * s), shell);
            }
            // Cuff in the team colour.
            Prism(b, 8, 1f, 1f, M(new Vector3(0f, 0f, -0.07f) * s, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.1f, 0.05f, 0.1f) * s), accent);
            parts.Hand = b.ToMesh("RobotHand");
        }

        // ---------------- Low-poly primitives ----------------

        private static Matrix4x4 M(Vector3 pos, Quaternion rot, Vector3 scale) => Matrix4x4.TRS(pos, rot, scale);

        private static readonly Vector3[] BoxVerts =
        {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
        };
        private static readonly int[] BoxTris =
        {
            0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 0, 4, 5, 0, 5, 1, 3, 2, 6, 3, 6, 7, 0, 3, 7, 0, 7, 4, 1, 5, 6, 1, 6, 2,
        };

        private static void Box(MeshBuilder b, Matrix4x4 m, Color color) => Convex(b, BoxVerts, BoxTris, m, color);

        /// <summary>A unit icosphere (radius 1) through <paramref name="m"/>: 20 facets at 0, 80 at 1.</summary>
        private static void Ico(MeshBuilder b, int subdivisions, Matrix4x4 m, Color color)
        {
            var (verts, tris) = MeshBuilder.Icosphere(subdivisions);
            Convex(b, verts, tris, m, color);
        }

        private static readonly Dictionary<long, (Vector3[], int[])> PrismCache = new Dictionary<long, (Vector3[], int[])>();

        /// <summary>
        /// A unit prism along Y (height 1, diameter 1, centred) with <paramref name="sides"/> sides; the bottom and
        /// top rings are scaled by the two ratios (0 makes a point: a cone or pyramid).
        /// </summary>
        private static void Prism(MeshBuilder b, int sides, float bottom, float top, Matrix4x4 m, Color color)
        {
            long key = sides * 1000000L + Mathf.RoundToInt(bottom * 1000f) * 1000L + Mathf.RoundToInt(top * 1000f);
            if (!PrismCache.TryGetValue(key, out var shape))
            {
                var v = new List<Vector3>();
                var t = new List<int>();
                int Ring(float y, float ratio)
                {
                    int start = v.Count;
                    if (ratio < 0.001f)
                    {
                        v.Add(new Vector3(0f, y, 0f));
                        return start;
                    }
                    for (int i = 0; i < sides; i++)
                    {
                        float a = i / (float)sides * Mathf.PI * 2f;
                        v.Add(new Vector3(Mathf.Cos(a) * 0.5f * ratio, y, Mathf.Sin(a) * 0.5f * ratio));
                    }
                    return start;
                }
                int lo = Ring(-0.5f, bottom);
                int hi = Ring(0.5f, top);
                bool loPoint = bottom < 0.001f, hiPoint = top < 0.001f;
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    int a = loPoint ? lo : lo + i, bIdx = loPoint ? lo : lo + j;
                    int c = hiPoint ? hi : hi + j, d = hiPoint ? hi : hi + i;
                    if (!loPoint) { t.Add(a); t.Add(bIdx); t.Add(c); }
                    if (!hiPoint) { t.Add(a); t.Add(c); t.Add(d); }
                    if (loPoint) { t.Add(a); t.Add(c); t.Add(d); }
                }
                // Caps.
                if (!loPoint) for (int i = 1; i < sides - 1; i++) { t.Add(lo); t.Add(lo + i); t.Add(lo + i + 1); }
                if (!hiPoint) for (int i = 1; i < sides - 1; i++) { t.Add(hi); t.Add(hi + i); t.Add(hi + i + 1); }
                shape = (v.ToArray(), t.ToArray());
                PrismCache[key] = shape;
            }
            Convex(b, shape.Item1, shape.Item2, m, color);
        }

        /// <summary>
        /// Adds a convex shape as flat-shaded triangles, turning each one to face away from the shape's centre
        /// (so primitives never need hand-checked winding).
        /// </summary>
        private static void Convex(MeshBuilder b, Vector3[] verts, int[] tris, Matrix4x4 m, Color color)
        {
            var w = new Vector3[verts.Length];
            var center = Vector3.zero;
            for (int i = 0; i < verts.Length; i++)
            {
                w[i] = m.MultiplyPoint3x4(verts[i]);
                center += w[i];
            }
            center /= verts.Length;
            for (int i = 0; i < tris.Length; i += 3)
            {
                var p0 = w[tris[i]];
                var p1 = w[tris[i + 1]];
                var p2 = w[tris[i + 2]];
                var n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.sqrMagnitude < 1e-12f) continue;
                if (Vector3.Dot(n, (p0 + p1 + p2) / 3f - center) < 0f) b.FlatTriangle(p0, p2, p1, color);
                else b.FlatTriangle(p0, p1, p2, color);
            }
        }
    }
}
