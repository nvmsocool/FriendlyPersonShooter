using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// Collects triangles with per-vertex colors and builds a <see cref="Mesh"/> (32-bit indices, so any size).
    /// Typical use: one builder per material per chunk, append thousands of plants/rocks with
    /// <see cref="Append(Mesh, Matrix4x4, Color)"/>, then <see cref="ToGameObject"/>.
    /// Vertex color alpha is free for shaders (e.g. emission or wind weight in the starter shader: emission).
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();

        public int VertexCount => Vertices.Count;
        public int TriangleCount => Triangles.Count / 3;

        public void Clear()
        {
            Vertices.Clear();
            Normals.Clear();
            Colors.Clear();
            Uvs.Clear();
            Triangles.Clear();
        }

        /// <summary>Adds a vertex and returns its index. Pass a zero normal to have normals recalculated at build.</summary>
        public int Vertex(Vector3 position, Color color, Vector3 normal = default, Vector2 uv = default)
        {
            Vertices.Add(position);
            Normals.Add(normal);
            Colors.Add(color);
            Uvs.Add(uv);
            return Vertices.Count - 1;
        }

        public void Triangle(int a, int b, int c)
        {
            Triangles.Add(a);
            Triangles.Add(b);
            Triangles.Add(c);
        }

        public void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        /// <summary>A flat-shaded triangle (own vertices, face normal).</summary>
        public void FlatTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i = Vertex(a, color, n);
            Vertex(b, color, n);
            Vertex(c, color, n);
            Triangle(i, i + 1, i + 2);
        }

        /// <summary>Appends a whole mesh transformed by <paramref name="m"/>, multiplying its vertex colors (white if none) by <paramref name="tint"/>.</summary>
        public void Append(Mesh mesh, Matrix4x4 m, Color tint)
        {
            var verts = mesh.vertices;
            var norms = mesh.normals;
            var cols = mesh.colors;
            var uvs = mesh.uv;
            var tris = mesh.triangles;
            int baseIndex = Vertices.Count;
            var nm = m.inverse.transpose;
            for (int i = 0; i < verts.Length; i++)
            {
                Vertices.Add(m.MultiplyPoint3x4(verts[i]));
                Normals.Add(norms.Length == verts.Length ? nm.MultiplyVector(norms[i]).normalized : Vector3.zero);
                Colors.Add(cols.Length == verts.Length ? cols[i] * tint : tint);
                Uvs.Add(uvs.Length == verts.Length ? uvs[i] : Vector2.zero);
            }
            bool flip = m.determinant < 0f;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Triangles.Add(baseIndex + tris[i]);
                Triangles.Add(baseIndex + (flip ? tris[i + 2] : tris[i + 1]));
                Triangles.Add(baseIndex + (flip ? tris[i + 1] : tris[i + 2]));
            }
        }

        /// <summary>Appends another builder's content transformed by <paramref name="m"/> and tinted.</summary>
        public void Append(MeshBuilder other, Matrix4x4 m, Color tint)
        {
            int baseIndex = Vertices.Count;
            var nm = m.inverse.transpose;
            for (int i = 0; i < other.Vertices.Count; i++)
            {
                Vertices.Add(m.MultiplyPoint3x4(other.Vertices[i]));
                var n = other.Normals[i];
                Normals.Add(n == Vector3.zero ? Vector3.zero : nm.MultiplyVector(n).normalized);
                Colors.Add(other.Colors[i] * tint);
                Uvs.Add(other.Uvs[i]);
            }
            bool flip = m.determinant < 0f;
            var t = other.Triangles;
            for (int i = 0; i < t.Count; i += 3)
            {
                Triangles.Add(baseIndex + t[i]);
                Triangles.Add(baseIndex + (flip ? t[i + 2] : t[i + 1]));
                Triangles.Add(baseIndex + (flip ? t[i + 1] : t[i + 2]));
            }
        }

        public Mesh ToMesh(string name = "ZoneMesh")
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(Vertices);
            mesh.SetColors(Colors);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Triangles, 0, true);
            bool anyMissing = false;
            foreach (var n in Normals)
            {
                if (n == Vector3.zero)
                {
                    anyMissing = true;
                    break;
                }
            }
            if (anyMissing) mesh.RecalculateNormals();
            else mesh.SetNormals(Normals);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Builds a child object with this mesh. Adds a MeshCollider when <paramref name="collider"/> is true.</summary>
        public GameObject ToGameObject(string name, Transform parent, Material material, bool collider, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = ToMesh(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        // ---- Primitive shapes (local space), handy for building plant and rock templates ----

        /// <summary>A tapered cylinder/cone from <paramref name="a"/> to <paramref name="b"/> (smooth sides, capped bottom).</summary>
        public void Tube(Vector3 a, Vector3 b, float radiusA, float radiusB, int sides, Color colorA, Color colorB)
        {
            var axis = (b - a).normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(side, axis);
            int start = Vertices.Count;
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2f;
                var dir = side * Mathf.Cos(t) + up * Mathf.Sin(t);
                Vertex(a + dir * radiusA, colorA, dir);
                Vertex(b + dir * radiusB, colorB, dir);
            }
            for (int i = 0; i < sides; i++)
            {
                int k = start + i * 2;
                Quad(k, k + 1, k + 3, k + 2);
            }
        }

        /// <summary>A low-poly blob: an icosphere-ish ball with jittered radius (flat shaded). Good for canopies and boulders.</summary>
        public void Blob(Vector3 center, Vector3 radius, Color color, ref ZoneRandom random, float jitter = 0.2f, int subdivisions = 1)
        {
            var (verts, tris) = Icosphere(subdivisions);
            var displaced = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                float s = 1f + random.Range(-jitter, jitter);
                displaced[i] = center + Vector3.Scale(verts[i] * s, radius);
            }
            for (int i = 0; i < tris.Length; i += 3) FlatTriangle(displaced[tris[i]], displaced[tris[i + 1]], displaced[tris[i + 2]], color);
        }

        /// <summary>A double-sided vertical blade (grass, reeds, crystal shards) from base to tip.</summary>
        public void Blade(Vector3 basePos, Vector3 tip, float width, Color baseColor, Color tipColor)
        {
            var dir = (tip - basePos).normalized;
            var side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.forward).normalized * (width * 0.5f);
            var n = Vector3.Cross(side, dir).normalized;
            int i = Vertex(basePos - side, baseColor, n);
            Vertex(basePos + side, baseColor, n);
            Vertex(tip, tipColor, n);
            Triangle(i, i + 1, i + 2);
            int j = Vertex(basePos - side, baseColor, -n);
            Vertex(basePos + side, baseColor, -n);
            Vertex(tip, tipColor, -n);
            Triangle(j, j + 2, j + 1);
        }

        private static (Vector3[] verts, int[] tris) _ico0, _ico1, _ico2;

        public static (Vector3[] verts, int[] tris) Icosphere(int subdivisions)
        {
            subdivisions = Mathf.Clamp(subdivisions, 0, 2);
            if (subdivisions == 0 && _ico0.verts != null) return _ico0;
            if (subdivisions == 1 && _ico1.verts != null) return _ico1;
            if (subdivisions == 2 && _ico2.verts != null) return _ico2;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                var nf = new List<int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int idx)) return idx;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized);
                    cache[key] = v.Count - 1;
                    return v.Count - 1;
                }
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            var result = (v.ToArray(), f.ToArray());
            if (subdivisions == 0) _ico0 = result;
            else if (subdivisions == 1) _ico1 = result;
            else _ico2 = result;
            return result;
        }
    }
}
