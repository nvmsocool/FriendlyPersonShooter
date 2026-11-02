using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendlyPersonShooter.ZoneArt.C
{
    /// <summary>One render vertex: position, normal and a half-precision color (what PrismLit and PrismWater read).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PrismVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public half4 Color;
    }

    /// <summary>
    /// Mesh data built on a worker thread: packed render vertices (no UVs) or a welded collision mesh.
    /// Turned into a <see cref="Mesh"/> on the main thread with one buffer copy.
    /// </summary>
    public sealed class PrismMesh
    {
        public string Name;
        public PrismVertex[] Vertices = new PrismVertex[0];
        public int VertexCount;
        public int[] Indices = new int[0];
        public int IndexCount;
        public Vector3 Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), Max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        public PrismMesh(string name, int vertexCapacity = 1024)
        {
            Name = name;
            Vertices = new PrismVertex[Mathf.Max(vertexCapacity, 16)];
            Indices = new int[Mathf.Max(vertexCapacity, 16)];
        }

        public bool Empty => IndexCount == 0;

        private void EnsureVertices(int extra)
        {
            if (VertexCount + extra <= Vertices.Length) return;
            System.Array.Resize(ref Vertices, Mathf.Max(Vertices.Length * 2, VertexCount + extra));
        }

        private void EnsureIndices(int extra)
        {
            if (IndexCount + extra <= Indices.Length) return;
            System.Array.Resize(ref Indices, Mathf.Max(Indices.Length * 2, IndexCount + extra));
        }

        public int Vertex(Vector3 p, Vector3 n, Color c)
        {
            EnsureVertices(1);
            Vertices[VertexCount] = new PrismVertex { Position = p, Normal = n, Color = new half4(new float4(c.r, c.g, c.b, c.a)) };
            Min = Vector3.Min(Min, p);
            Max = Vector3.Max(Max, p);
            return VertexCount++;
        }

        public void Triangle(int a, int b, int c)
        {
            EnsureIndices(3);
            Indices[IndexCount++] = a;
            Indices[IndexCount++] = b;
            Indices[IndexCount++] = c;
        }

        public void FlatTriangle(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i = Vertex(a, n, col);
            Vertex(b, n, col);
            Vertex(c, n, col);
            Triangle(i, i + 1, i + 2);
        }

        /// <summary>Appends a builder's triangles as they are (world-space vertices).</summary>
        public void Append(MeshBuilder mb)
        {
            int baseIndex = VertexCount;
            EnsureVertices(mb.VertexCount);
            for (int i = 0; i < mb.VertexCount; i++) Vertex(mb.Vertices[i], mb.Normals[i], mb.Colors[i]);
            EnsureIndices(mb.Triangles.Count);
            var t = mb.Triangles;
            for (int i = 0; i < t.Count; i++) Indices[IndexCount++] = baseIndex + t[i];
        }

        /// <summary>Appends a template transformed by <paramref name="m"/>, its colors multiplied by <paramref name="tint"/>.</summary>
        public void Append(MeshBuilder other, Matrix4x4 m, Color tint)
        {
            int baseIndex = VertexCount;
            var nm = m.inverse.transpose;
            EnsureVertices(other.VertexCount);
            for (int i = 0; i < other.VertexCount; i++)
            {
                var n = other.Normals[i];
                Vertex(m.MultiplyPoint3x4(other.Vertices[i]), n == Vector3.zero ? Vector3.zero : nm.MultiplyVector(n).normalized, other.Colors[i] * tint);
            }
            bool flip = m.determinant < 0f;
            var t = other.Triangles;
            EnsureIndices(t.Count);
            for (int i = 0; i < t.Count; i += 3)
            {
                Indices[IndexCount++] = baseIndex + t[i];
                Indices[IndexCount++] = baseIndex + (flip ? t[i + 2] : t[i + 1]);
                Indices[IndexCount++] = baseIndex + (flip ? t[i + 1] : t[i + 2]);
            }
        }

        /// <summary>Fills zero normals with area-weighted vertex normals (what Mesh.RecalculateNormals does for shared vertices).</summary>
        public void FixMissingNormals()
        {
            bool any = false;
            for (int i = 0; i < VertexCount && !any; i++) any = Vertices[i].Normal == Vector3.zero;
            if (!any) return;
            var acc = new Vector3[VertexCount];
            for (int i = 0; i < IndexCount; i += 3)
            {
                int a = Indices[i], b = Indices[i + 1], c = Indices[i + 2];
                var n = Vector3.Cross(Vertices[b].Position - Vertices[a].Position, Vertices[c].Position - Vertices[a].Position);
                acc[a] += n;
                acc[b] += n;
                acc[c] += n;
            }
            for (int i = 0; i < VertexCount; i++) if (Vertices[i].Normal == Vector3.zero) Vertices[i].Normal = acc[i].normalized;
        }

        private static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float16, 4),
        };

        private const MeshUpdateFlags Quiet = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        /// <summary>Main thread: the render mesh (its CPU copy is released after upload).</summary>
        public Mesh ToMesh(bool keepReadable = false)
        {
            var mesh = new Mesh { name = Name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertexBufferParams(VertexCount, Layout);
            mesh.SetVertexBufferData(Vertices, 0, 0, VertexCount, 0, Quiet);
            mesh.SetIndexBufferParams(IndexCount, IndexFormat.UInt32);
            mesh.SetIndexBufferData(Indices, 0, 0, IndexCount, Quiet);
            var bounds = new Bounds((Min + Max) * 0.5f, Max - Min);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, IndexCount) { bounds = bounds, firstVertex = 0, vertexCount = VertexCount }, Quiet);
            mesh.bounds = bounds;
            if (!keepReadable) mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Main thread: a child object drawing this mesh.</summary>
        public GameObject ToRenderObject(Transform parent, Material material, bool castShadows = true)
        {
            var go = new GameObject(Name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
    }

    /// <summary>A collision mesh (welded positions and triangles), identical in full and headless builds.</summary>
    public sealed class PrismCollider
    {
        /// <summary>The cooking options used for every collider (Unity's defaults, spelled out so baking and assigning agree).</summary>
        public const MeshColliderCookingOptions Cooking = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning |
                                                         MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;

        public string Name;
        public Vector3[] Positions;
        public int[] Indices;
        public int Tag;
        public Mesh Mesh;      // main thread
        public EntityId MeshId; // for baking on a worker thread

        public PrismCollider(string name, Vector3[] positions, int[] indices, int tag)
        {
            Name = name;
            Positions = positions;
            Indices = indices;
            Tag = tag;
        }

        /// <summary>Welds a builder's triangles (exact position matches) into a collider.</summary>
        public static PrismCollider FromBuilder(string name, MeshBuilder mb, int tag)
        {
            var map = new Dictionary<Vector3, int>(mb.VertexCount / 2 + 16);
            var pos = new List<Vector3>(mb.VertexCount / 2 + 16);
            var idx = new int[mb.Triangles.Count];
            for (int i = 0; i < idx.Length; i++)
            {
                var p = mb.Vertices[mb.Triangles[i]];
                if (!map.TryGetValue(p, out int k))
                {
                    k = pos.Count;
                    pos.Add(p);
                    map.Add(p, k);
                }
                idx[i] = k;
            }
            return new PrismCollider(name, pos.ToArray(), idx, tag);
        }

        public bool Empty => Indices.Length == 0;

        public void AddTo(PrismCollision world) => world.Add(Positions, Indices, Tag);

        /// <summary>Main thread: creates the (readable) mesh to bake.</summary>
        public void CreateMesh()
        {
            Mesh = new Mesh { name = Name + " collider", indexFormat = IndexFormat.UInt32 };
            Mesh.SetVertices(Positions);
            Mesh.SetIndices(Indices, MeshTopology.Triangles, 0, true);
            MeshId = Mesh.GetEntityId();
        }

        /// <summary>Any thread: cooks the PhysX mesh so assigning it later is cheap.</summary>
        public void Bake() => Physics.BakeMesh(MeshId, false, Cooking);

        /// <summary>Main thread: a collider-only child object.</summary>
        public GameObject ToColliderObject(Transform parent)
        {
            var go = new GameObject(Name);
            go.transform.SetParent(parent, false);
            var mc = go.AddComponent<MeshCollider>();
            mc.cookingOptions = Cooking;
            mc.sharedMesh = Mesh;
            return go;
        }
    }
}
