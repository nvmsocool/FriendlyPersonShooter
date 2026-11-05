using System;
using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// A height grid over one zone, built from your height function, turned into chunked terrain meshes
    /// with colliders. The grid covers the zone's square build box; meshes keep only the cells that touch the
    /// zone's outline (a cell of overlap, which sits on the shared border height). Optional: you can build
    /// ground any way you like (meshes, voxels, floating islands).
    /// </summary>
    public sealed class Heightfield
    {
        public readonly Vector3 Origin;
        public readonly float Size;
        /// <summary>Samples per side (cells per side + 1).</summary>
        public readonly int Resolution;
        public readonly float Step;
        public readonly float[] Heights;
        private readonly ZoneRequest _zone;

        /// <param name="cellSize">Metres between samples (1 to 2 is a good range for a 250 m zone).</param>
        public Heightfield(ZoneRequest zone, float cellSize, Func<float, float, float> heightAt)
        {
            _zone = zone;
            Origin = zone.Origin;
            Size = zone.Size;
            Resolution = Mathf.RoundToInt(zone.Size / cellSize) + 1;
            Step = zone.Size / (Resolution - 1);
            Heights = new float[Resolution * Resolution];
            for (int z = 0; z < Resolution; z++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    Heights[z * Resolution + x] = heightAt(Origin.x + x * Step, Origin.z + z * Step);
                }
            }
        }

        public float this[int x, int z] => Heights[Mathf.Clamp(z, 0, Resolution - 1) * Resolution + Mathf.Clamp(x, 0, Resolution - 1)];

        /// <summary>Bilinear height at a world position.</summary>
        public float HeightAt(float worldX, float worldZ)
        {
            float fx = (worldX - Origin.x) / Step, fz = (worldZ - Origin.z) / Step;
            int x = Mathf.Clamp(Mathf.FloorToInt(fx), 0, Resolution - 2), z = Mathf.Clamp(Mathf.FloorToInt(fz), 0, Resolution - 2);
            float tx = Mathf.Clamp01(fx - x), tz = Mathf.Clamp01(fz - z);
            float a = Mathf.Lerp(this[x, z], this[x + 1, z], tx);
            float b = Mathf.Lerp(this[x, z + 1], this[x + 1, z + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>Surface normal at a world position (from central differences).</summary>
        public Vector3 NormalAt(float worldX, float worldZ)
        {
            float e = Step;
            float hl = HeightAt(worldX - e, worldZ), hr = HeightAt(worldX + e, worldZ);
            float hd = HeightAt(worldX, worldZ - e), hu = HeightAt(worldX, worldZ + e);
            return new Vector3(hl - hr, 2f * e, hd - hu).normalized;
        }

        /// <summary>Slope in degrees at a world position.</summary>
        public float SlopeAt(float worldX, float worldZ) => Vector3.Angle(NormalAt(worldX, worldZ), Vector3.up);

        /// <summary>
        /// Builds the terrain as square chunks (each a child with a MeshCollider). <paramref name="colorAt"/>
        /// gets (world position, normal) and returns the vertex color. Smooth shading; for faceted
        /// low-poly looks pass <paramref name="flat"/> = true.
        /// </summary>
        public GameObject Build(Transform parent, Material material, Func<Vector3, Vector3, Color> colorAt, int chunkCells = 64, bool flat = false, string name = "Terrain")
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            int cells = Resolution - 1;
            var mb = new MeshBuilder();
            for (int cz = 0; cz < cells; cz += chunkCells)
            {
                for (int cx = 0; cx < cells; cx += chunkCells)
                {
                    mb.Clear();
                    int ex = Mathf.Min(cx + chunkCells, cells), ez = Mathf.Min(cz + chunkCells, cells);
                    if (flat) BuildFlat(mb, cx, cz, ex, ez, colorAt);
                    else BuildSmooth(mb, cx, cz, ex, ez, colorAt);
                    if (mb.TriangleCount == 0) continue;
                    mb.ToGameObject($"{name} {cx / chunkCells},{cz / chunkCells}", root.transform, material, collider: true);
                }
            }
            return root;
        }

        /// <summary>True for cells inside the zone or within one cell of its outline.</summary>
        private bool KeepCell(int x, int z) => _zone.EdgeDistance(Origin.x + (x + 0.5f) * Step, Origin.z + (z + 0.5f) * Step) > -Step;

        private Vector3 P(int x, int z) => new Vector3(Origin.x + x * Step, this[x, z], Origin.z + z * Step);

        private void BuildSmooth(MeshBuilder mb, int x0, int z0, int x1, int z1, Func<Vector3, Vector3, Color> colorAt)
        {
            int w = x1 - x0 + 1;
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = P(x, z);
                    var n = NormalAt(p.x, p.z);
                    mb.Vertex(p, colorAt(p, n), n);
                }
            }
            for (int z = 0; z < z1 - z0; z++)
            {
                for (int x = 0; x < x1 - x0; x++)
                {
                    if (!KeepCell(x0 + x, z0 + z)) continue;
                    int i = z * w + x;
                    mb.Quad(i, i + w, i + w + 1, i + 1);
                }
            }
        }

        private void BuildFlat(MeshBuilder mb, int x0, int z0, int x1, int z1, Func<Vector3, Vector3, Color> colorAt)
        {
            for (int z = z0; z < z1; z++)
            {
                for (int x = x0; x < x1; x++)
                {
                    if (!KeepCell(x, z)) continue;
                    var a = P(x, z);
                    var b = P(x, z + 1);
                    var c = P(x + 1, z + 1);
                    var d = P(x + 1, z);
                    bool alt = ((x + z) & 1) == 0;
                    if (alt)
                    {
                        Tri(mb, a, b, c, colorAt);
                        Tri(mb, a, c, d, colorAt);
                    }
                    else
                    {
                        Tri(mb, a, b, d, colorAt);
                        Tri(mb, b, c, d, colorAt);
                    }
                }
            }
        }

        private static void Tri(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Func<Vector3, Vector3, Color> colorAt)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            mb.FlatTriangle(a, b, c, colorAt((a + b + c) / 3f, n));
        }
    }
}
