using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>Flat-shaded primitive shapes appended to a <see cref="MeshBuilder"/>.</summary>
    public static class PrismGeo
    {
        public static void Quad(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            mb.FlatTriangle(a, b, c, col);
            mb.FlatTriangle(a, c, d, col);
        }

        /// <summary>Quad with a vertical color gradient (ca at a/d, cb at b/c), one face normal.</summary>
        public static void QuadGrad(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i = mb.Vertex(a, ca, n);
            mb.Vertex(b, cb, n);
            mb.Vertex(c, cb, n);
            mb.Vertex(d, ca, n);
            mb.Quad(i, i + 1, i + 2, i + 3);
        }

        /// <summary>An oriented box: centre, rotation, full size. Faces: top color, sides color, bottom darker.</summary>
        public static void Box(MeshBuilder mb, Vector3 center, Quaternion rot, Vector3 size, Color top, Color side)
        {
            Vector3 hx = rot * new Vector3(size.x * 0.5f, 0, 0), hy = rot * new Vector3(0, size.y * 0.5f, 0), hz = rot * new Vector3(0, 0, size.z * 0.5f);
            Vector3 P(int sx, int sy, int sz) => center + hx * sx + hy * sy + hz * sz;
            var bottom = side * 0.7f;
            bottom.a = side.a;
            Quad(mb, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), top);
            Quad(mb, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), bottom);
            QuadGrad(mb, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), Shade(side, 0.82f), side);
            QuadGrad(mb, P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), Shade(side, 0.82f), side);
            QuadGrad(mb, P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), Shade(side, 0.82f), side);
            QuadGrad(mb, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Shade(side, 0.82f), side);
        }

        /// <summary>
        /// An n-sided prism from base along axis, optional pointed tip (crystal) of length <paramref name="tip"/>.
        /// Colors blend base to top; tipAlpha below 1 makes the tip glow.
        /// </summary>
        public static void Prism(MeshBuilder mb, Vector3 basePos, Vector3 axis, float radius, float length, int sides, float tip,
            Color baseCol, Color topCol, float twist = 0f, float tipAlpha = 1f, float topRadiusScale = 1f)
        {
            axis = axis.normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(side, axis);
            var top = basePos + axis * length;
            var ring0 = new Vector3[sides];
            var ring1 = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = twist + i / (float)sides * Mathf.PI * 2f;
                var d = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                ring0[i] = basePos + d * radius;
                ring1[i] = top + d * radius * topRadiusScale;
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                var shade = 0.88f + 0.12f * Mathf.Cos(i * 2.1f);
                QuadGrad(mb, ring0[i], ring1[i], ring1[j], ring0[j], Shade(baseCol, shade), Shade(topCol, shade));
            }
            if (tip > 0f)
            {
                var apex = top + axis * tip;
                var tc = topCol;
                var ac = Color.Lerp(topCol, Color.white, 0.35f);
                ac.a = tipAlpha;
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    var n = Vector3.Cross(ring1[j] - ring1[i], apex - ring1[i]).normalized;
                    int k = mb.Vertex(ring1[i], tc, -n);
                    mb.Vertex(apex, ac, -n);
                    mb.Vertex(ring1[j], tc, -n);
                    mb.Triangle(k, k + 1, k + 2);
                }
            }
            else
            {
                for (int i = 1; i < sides - 1; i++) mb.FlatTriangle(ring1[0], ring1[i + 1], ring1[i], topCol);
            }
        }

        /// <summary>An annular sector slab (a spiral stair step): angles in radians, top at y.</summary>
        public static void Sector(MeshBuilder mb, Vector3 center, float r0, float r1, float a0, float a1, float y, float thickness, Color top, Color side)
        {
            Vector3 P(float r, float a, float h) => new Vector3(center.x + Mathf.Cos(a) * r, h, center.z + Mathf.Sin(a) * r);
            float yb = y - thickness;
            int segs = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / 0.35f));
            for (int s = 0; s < segs; s++)
            {
                float b0 = Mathf.Lerp(a0, a1, s / (float)segs), b1 = Mathf.Lerp(a0, a1, (s + 1) / (float)segs);
                Quad(mb, P(r0, b0, y), P(r0, b1, y), P(r1, b1, y), P(r1, b0, y), top);
                Quad(mb, P(r0, b0, yb), P(r1, b0, yb), P(r1, b1, yb), P(r0, b1, yb), Shade(side, 0.6f));
                QuadGrad(mb, P(r1, b0, yb), P(r1, b0, y), P(r1, b1, y), P(r1, b1, yb), Shade(side, 0.8f), side);
                QuadGrad(mb, P(r0, b1, yb), P(r0, b1, y), P(r0, b0, y), P(r0, b0, yb), Shade(side, 0.8f), side);
            }
            QuadGrad(mb, P(r0, a0, yb), P(r0, a0, y), P(r1, a0, y), P(r1, a0, yb), Shade(side, 0.8f), side);
            QuadGrad(mb, P(r1, a1, yb), P(r1, a1, y), P(r0, a1, y), P(r0, a1, yb), Shade(side, 0.8f), side);
        }

        /// <summary>A smooth-shaded sphere-ish blob with a vertical color gradient (bottom darker), optional flat facets.</summary>
        public static void Ball(MeshBuilder mb, Vector3 center, Vector3 radius, Color bottom, Color top, ref ZoneRandom rng, float jitter = 0.12f, int subdiv = 1, bool flat = true, float alpha = 1f)
        {
            var (verts, tris) = MeshBuilder.Icosphere(subdiv);
            var p = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                float s = 1f + rng.Range(-jitter, jitter);
                p[i] = center + Vector3.Scale(verts[i] * s, radius);
            }
            if (flat)
            {
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var a = p[tris[i]];
                    var b = p[tris[i + 1]];
                    var c = p[tris[i + 2]];
                    float t = Mathf.Clamp01(((a.y + b.y + c.y) / 3f - (center.y - radius.y)) / (2f * radius.y));
                    var col = Color.Lerp(bottom, top, t);
                    col.a = alpha;
                    mb.FlatTriangle(a, b, c, col);
                }
            }
            else
            {
                int start = mb.VertexCount;
                for (int i = 0; i < p.Length; i++)
                {
                    float t = Mathf.Clamp01((p[i].y - (center.y - radius.y)) / (2f * radius.y));
                    var col = Color.Lerp(bottom, top, t);
                    col.a = alpha;
                    mb.Vertex(p[i], col, (p[i] - center).normalized);
                }
                for (int i = 0; i < tris.Length; i += 3) mb.Triangle(start + tris[i], start + tris[i + 1], start + tris[i + 2]);
            }
        }

        /// <summary>A cone (open bottom capped) with n sides, flat shaded, color base to tip.</summary>
        public static void Cone(MeshBuilder mb, Vector3 basePos, float radius, float height, int sides, Color baseCol, Color tipCol, float rot = 0f, float bottomDrop = 0f)
        {
            var apex = basePos + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = rot + i / (float)sides * Mathf.PI * 2f, a1 = rot + (i + 1) / (float)sides * Mathf.PI * 2f;
                var p0 = basePos + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                var p1 = basePos + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                var n = Vector3.Cross(apex - p0, p1 - p0).normalized;
                var m = (p0 + p1 + apex) / 3f;
                var radial = m - new Vector3(basePos.x, m.y, basePos.z);
                float sh = 0.9f + 0.1f * Mathf.Cos(i * 2.3f);
                if (Vector3.Dot(n, radial) >= 0f)
                {
                    int k = mb.Vertex(p0, Shade(baseCol, sh), n);
                    mb.Vertex(apex, Shade(tipCol, sh), n);
                    mb.Vertex(p1, Shade(baseCol, sh), n);
                    mb.Triangle(k, k + 1, k + 2);
                }
                else
                {
                    int k = mb.Vertex(p1, Shade(baseCol, sh), -n);
                    mb.Vertex(apex, Shade(tipCol, sh), -n);
                    mb.Vertex(p0, Shade(baseCol, sh), -n);
                    mb.Triangle(k, k + 1, k + 2);
                }
                var bc = basePos + Vector3.down * bottomDrop * Mathf.Sign(height);
                var capN = Vector3.Cross(p1 - p0, bc - p0);
                if (Vector3.Dot(capN, Vector3.up * -Mathf.Sign(height)) >= 0f) mb.FlatTriangle(p0, p1, bc, Shade(baseCol, 0.6f));
                else mb.FlatTriangle(p1, p0, bc, Shade(baseCol, 0.6f));
            }
        }

        public static Color Shade(Color c, float f)
        {
            return new Color(c.r * f, c.g * f, c.b * f, c.a);
        }

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
