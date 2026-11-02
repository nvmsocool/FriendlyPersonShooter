using System.Collections.Generic;
using System.Text;
using FriendlyPersonShooter.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// First-person view of the arena for the human player: camera at the local player's eyes,
    /// placeholder shapes for the floor, cover, players and enemies, a crosshair and a HUD.
    /// Uses instanced drawing, so no Entities Graphics is needed. Click to capture the mouse, Esc to release.
    /// </summary>
    public partial class ArenaView : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;

        private Mesh _cube;
        private Material _floorMat, _wallMat, _pillarMat, _allyMat, _allyHeadMat;
        private readonly List<Matrix4x4> _batch = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _static = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _walls = new List<Matrix4x4>();
        private readonly StringBuilder _text = new StringBuilder();
        private Text _hud, _crosshair, _center;
        private World _queryWorld;
        private EntityQuery _players, _localPlayer;
        private Material _baseMat;

        private void Start()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }
            if (viewCamera == null) viewCamera = Camera.main;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cube = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);

            _baseMat = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultMaterial
                : null;
            if (_baseMat == null)
            {
                Debug.LogError("[Arena] No render pipeline default material found.");
                enabled = false;
                return;
            }
            _floorMat = MakeMat(new Color(0.35f, 0.37f, 0.33f));
            _wallMat = MakeMat(new Color(0.25f, 0.26f, 0.3f));
            _pillarMat = MakeMat(new Color(0.45f, 0.42f, 0.38f));
            _allyMat = MakeMat(new Color(0.25f, 0.6f, 1f));
            _allyHeadMat = MakeMat(new Color(0.85f, 0.9f, 1f));
            CreateCombatMaterials();
            CreateExpeditionMaterials();
            if (viewCamera != null) viewCamera.farClipPlane = 1500f;

            BuildStatic();
            BuildUi();
        }

        private Material MakeMat(Color color)
        {
            return new Material(_baseMat) { enableInstancing = true, color = color };
        }

        private void BuildStatic()
        {
            float s = ArenaSettings.Size;
            _static.Add(Matrix4x4.TRS(new Vector3(0f, -0.25f, 0f), Quaternion.identity, new Vector3(s, 0.5f, s)));
            float h = 4f;
            _walls.Add(Matrix4x4.TRS(new Vector3(0f, h / 2, s / 2 + 0.5f), Quaternion.identity, new Vector3(s + 2f, h, 1f)));
            _walls.Add(Matrix4x4.TRS(new Vector3(0f, h / 2, -s / 2 - 0.5f), Quaternion.identity, new Vector3(s + 2f, h, 1f)));
            _walls.Add(Matrix4x4.TRS(new Vector3(s / 2 + 0.5f, h / 2, 0f), Quaternion.identity, new Vector3(1f, h, s)));
            _walls.Add(Matrix4x4.TRS(new Vector3(-s / 2 - 0.5f, h / 2, 0f), Quaternion.identity, new Vector3(1f, h, s)));
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("ArenaHud", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _hud = MakeText(canvasGo.transform, font, 16, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(12, -10));
            _crosshair = MakeText(canvasGo.transform, font, 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero);
            _crosshair.text = "+";
            _center = MakeText(canvasGo.transform, font, 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -60));
        }

        private static Text MakeText(Transform parent, Font font, int size, TextAnchor anchor, Vector2 pivot, Vector2 offset)
        {
            var go = new GameObject("Text", typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = pivot;
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(700, 400);
            return text;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;

            _wrap = ExpeditionWorld.Active;
            if (!_wrap)
            {
                DrawList(_static, _floorMat);
                DrawList(_walls, _wallMat);
                DrawCover();
            }

            var world = ClientServerBootstrap.ClientWorld;
            _text.Clear();
            if (world == null || !world.IsCreated)
            {
                _hud.text = "No client world.";
                return;
            }
            if (_queryWorld != world)
            {
                _queryWorld = world;
                var em = world.EntityManager;
                _players = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaPlayer>(), ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<ArenaLook>(), ComponentType.ReadOnly<GhostOwner>());
                _localPlayer = em.CreateEntityQuery(ComponentType.ReadOnly<ArenaPlayer>(), ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>());
                CreateCombatQueries(em);
                CreateExpeditionQueries(em);
            }

            Vector3? eye = null;
            Entity local = Entity.Null;
            if (_localPlayer.CalculateEntityCount() == 1)
            {
                using (var locals = _localPlayer.ToEntityArray(Allocator.Temp)) local = locals[0];
                Vector3 feet = world.EntityManager.GetComponentData<LocalTransform>(local).Position;
                eye = feet + Vector3.up * ArenaSettings.EyeHeight;
                _camRef = feet;
            }
            if (_wrap) DrawExpeditionWorld();

            DrawPlayers(world, local);
            DrawCombat(world, local);

            if (eye.HasValue && viewCamera != null)
            {
                var look = ArenaLocalInputSystem.LocalLook;
                viewCamera.orthographic = false;
                viewCamera.fieldOfView = 80f;
                viewCamera.nearClipPlane = 0.05f;
                viewCamera.transform.SetPositionAndRotation(eye.Value, Quaternion.Euler(-look.y, look.x, 0f));
            }

            _text.Append(_wrap ? "Expedition   (click: capture mouse, Esc: release)\n" : "Arena   (click: capture mouse, Esc: release)\n");
            _text.Append("WASD move, Space jump, LMB fire, Q cycle payload (Vigor, Might, Haste, Relay)\n");
            if (_wrap) WriteExpeditionHud();
            WriteCombatHud(world, local);
            _hud.text = _text.ToString();
        }

        private void DrawCover()
        {
            _batch.Clear();
            foreach (var c in ArenaLayout.Cover)
            {
                _batch.Add(Matrix4x4.TRS(new Vector3(c.Center.x, c.Size.y / 2, c.Center.y), Quaternion.identity,
                    new Vector3(c.Size.x, c.Size.y, c.Size.z)));
            }
            DrawList(_batch, _pillarMat);
        }

        private void DrawPlayers(World world, Entity local)
        {
            using var entities = _players.ToEntityArray(Allocator.Temp);
            using var transforms = _players.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var looks = _players.ToComponentDataArray<ArenaLook>(Allocator.Temp);
            _batch.Clear();
            var heads = new List<Matrix4x4>();
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == local) continue;
                // Downed players are drawn lying down by the combat view.
                var em = world.EntityManager;
                if (em.HasComponent<ArenaVitals>(entities[i]) && em.GetComponentData<ArenaVitals>(entities[i]).Health <= 0f) continue;
                Vector3 p = Near(transforms[i].Position);
                var yaw = Quaternion.Euler(0f, looks[i].Yaw, 0f);
                _batch.Add(Matrix4x4.TRS(p + Vector3.up * 0.8f, yaw, new Vector3(0.8f, 1.6f, 0.8f)));
                heads.Add(Matrix4x4.TRS(p + Vector3.up * 1.6f + yaw * Vector3.forward * 0.3f, yaw, new Vector3(0.5f, 0.3f, 0.3f)));
            }
            DrawList(_batch, _allyMat);
            DrawList(heads, _allyHeadMat);
        }

        private void DrawList(List<Matrix4x4> list, Material mat)
        {
            if (list.Count == 0) return;
            var rp = new RenderParams(mat);
            for (int start = 0; start < list.Count; start += 1023)
            {
                int count = Mathf.Min(1023, list.Count - start);
                Graphics.RenderMeshInstanced(rp, _cube, 0, list, count, start);
            }
        }
    }
}
