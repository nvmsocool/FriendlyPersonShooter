using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// Debug view for the networking test: draws the zone tiles and the player cubes the local
    /// client can see, follows the local player with the camera, and shows lobby stats.
    /// Uses instanced drawing, so no Entities Graphics is needed.
    /// </summary>
    public class NetTestView : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 60f, -45f);

        private Mesh _cube;
        private Material _localMat, _otherMat, _tileMatA, _tileMatB;
        private readonly List<Matrix4x4> _others = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _tilesA = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _tilesB = new List<Matrix4x4>();
        private Text _stats;
        private EntityQuery _playerQuery;
        private World _queryWorld;
        private float _fps;

        private void Start()
        {
            // Headless server and bot processes have nothing to draw.
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }
            if (viewCamera == null) viewCamera = Camera.main;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cube = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);

            var baseMat = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultMaterial
                : null;
            if (baseMat == null)
            {
                Debug.LogError("[NetTest] No render pipeline default material found.");
                enabled = false;
                return;
            }
            _localMat = MakeMat(baseMat, new Color(1f, 0.75f, 0.1f));
            _otherMat = MakeMat(baseMat, new Color(0.25f, 0.6f, 1f));
            _tileMatA = MakeMat(baseMat, new Color(0.32f, 0.34f, 0.3f));
            _tileMatB = MakeMat(baseMat, new Color(0.26f, 0.28f, 0.25f));

            BuildTiles();
            BuildStatsPanel();
        }

        private static Material MakeMat(Material source, Color color)
        {
            var mat = new Material(source) { enableInstancing = true, color = color };
            return mat;
        }

        private void BuildTiles()
        {
            float zone = NetTestSettings.ZoneSize;
            int grid = NetTestSettings.GridSize;
            for (int x = 0; x < grid; x++)
            {
                for (int z = 0; z < grid; z++)
                {
                    var m = Matrix4x4.TRS(new Vector3((x + 0.5f) * zone, -0.1f, (z + 0.5f) * zone),
                        Quaternion.identity, new Vector3(zone, 0.2f, zone));
                    ((x + z) % 2 == 0 ? _tilesA : _tilesB).Add(m);
                }
            }
        }

        private void BuildStatsPanel()
        {
            var canvasGo = new GameObject("NetTestStats", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var textGo = new GameObject("Text", typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            _stats = textGo.GetComponent<Text>();
            _stats.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _stats.fontSize = 18;
            _stats.color = Color.white;
            var rect = _stats.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, -12f);
            rect.sizeDelta = new Vector2(520f, 220f);
        }

        private World FindClientWorld()
        {
            foreach (var world in World.All)
            {
                if (world.IsClient() && !world.IsThinClient()) return world;
            }
            return null;
        }

        private void LateUpdate()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f);
            // Draw the tiles 3x3 times so the wrapping world has no visible edge.
            float size = NetTestSettings.WorldSize;
            for (int ox = -1; ox <= 1; ox++)
            {
                for (int oz = -1; oz <= 1; oz++)
                {
                    var offset = Matrix4x4.Translate(new Vector3(ox * size, 0f, oz * size));
                    DrawList(_tilesA, _tileMatA, offset);
                    DrawList(_tilesB, _tileMatB, offset);
                }
            }

            var world = FindClientWorld();
            int myId = -1;
            int visible = 0;
            Vector3? localPos = null;

            if (world != null && world.IsCreated)
            {
                if (_queryWorld != world)
                {
                    _queryWorld = world;
                    _playerQuery = world.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<LocalTransform>(),
                        ComponentType.ReadOnly<GhostOwner>());
                }

                var em = world.EntityManager;
                using (var idQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>()))
                {
                    if (idQuery.CalculateEntityCount() == 1) myId = idQuery.GetSingleton<NetworkId>().Value;
                }

                _others.Clear();
                using (var transforms = _playerQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                using (var owners = _playerQuery.ToComponentDataArray<GhostOwner>(Allocator.Temp))
                {
                    visible = transforms.Length;
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        if (owners[i].NetworkId == myId) localPos = transforms[i].Position;
                    }
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        Vector3 p = transforms[i].Position;
                        // Show each other player at whichever wrapped copy is nearest to us.
                        if (localPos.HasValue) p = NearestCopy(p, localPos.Value, size);
                        var m = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * 1.5f);
                        if (owners[i].NetworkId == myId)
                        {
                            Graphics.RenderMesh(new RenderParams(_localMat), _cube, 0, m);
                        }
                        else
                        {
                            _others.Add(m);
                        }
                    }
                }
                DrawList(_others, _otherMat, Matrix4x4.identity);
            }

            if (localPos.HasValue && viewCamera != null)
            {
                viewCamera.transform.position = localPos.Value + cameraOffset;
                viewCamera.transform.LookAt(localPos.Value);
            }

            if (_stats != null)
            {
                int myLobby = 0;
                if (myId >= 0) LobbyStats.LobbyOfPlayer.TryGetValue(myId, out myLobby);
                string zone = localPos.HasValue
                    ? NetTestSettings.CreateGrid().ZoneAt(localPos.Value.x, localPos.Value.z).ToString()
                    : "-";
                _stats.text =
                    $"Networking test   {_fps:0} fps\n" +
                    $"Me: player {myId}, lobby {myLobby}, zone {zone}\n" +
                    $"Cubes I can see: {visible} (incl. me)\n" +
                    $"Server: {LobbyStats.Players} players, {LobbyStats.Lobbies} lobbies, " +
                    $"avg {LobbyStats.AverageSize:0.0}, largest {LobbyStats.LargestLobby}\n" +
                    $"Splits {LobbyStats.TotalSplits}, merges {LobbyStats.TotalMerges}\n" +
                    "WASD to move";
            }
        }

        private readonly List<Matrix4x4> _scratch = new List<Matrix4x4>();

        private void DrawList(List<Matrix4x4> matrices, Material mat, Matrix4x4 offset)
        {
            if (matrices.Count == 0) return;
            if (offset == Matrix4x4.identity)
            {
                Graphics.RenderMeshInstanced(new RenderParams(mat), _cube, 0, matrices);
                return;
            }
            _scratch.Clear();
            foreach (var m in matrices) _scratch.Add(offset * m);
            Graphics.RenderMeshInstanced(new RenderParams(mat), _cube, 0, _scratch);
        }

        private static Vector3 NearestCopy(Vector3 p, Vector3 viewer, float size)
        {
            p.x = viewer.x + Mathf.Repeat(p.x - viewer.x + size * 0.5f, size) - size * 0.5f;
            p.z = viewer.z + Mathf.Repeat(p.z - viewer.z + size * 0.5f, size) - size * 0.5f;
            return p;
        }
    }
}
