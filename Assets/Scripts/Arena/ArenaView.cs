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
        private Material _floorMat, _wallMat, _pillarMat;
        private readonly List<Matrix4x4> _batch = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _static = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _walls = new List<Matrix4x4>();
        private ArenaHud _hud;
        private Text _crosshair, _center;
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
            // The pipeline's default material only exists in the Editor; builds use this copy (Arena/Resources).
            if (_baseMat == null) _baseMat = Resources.Load<Material>("ArenaBase");
            if (_baseMat == null)
            {
                Debug.LogError("[Arena] No render pipeline default material found.");
                enabled = false;
                return;
            }
            _floorMat = MakeMat(new Color(0.35f, 0.37f, 0.33f));
            _wallMat = MakeMat(new Color(0.25f, 0.26f, 0.3f));
            _pillarMat = MakeMat(new Color(0.45f, 0.42f, 0.38f));
            CreateCombatMaterials();
            CreateExpeditionMaterials();
            CreateFeedbackMaterials();
            CreateRaidMaterials();
            CreateLootMaterials();
            CreateGunMaterials();
            CreateRobotMaterials();
            if (viewCamera != null) viewCamera.farClipPlane = FriendlyPersonShooter.ZoneArt.ZoneWorld.ViewFogEnd + 40f;

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
            _hud = new ArenaHud(transform);
            _crosshair = _hud.Crosshair;
            _center = _hud.Center;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            // Clicking into the game captures the mouse, except on an overlay button (the session's Leave and Copy).
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked &&
                !(UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()))
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
            _hud.Clear();
            if (world == null || !world.IsCreated)
            {
                _hud.Apply();
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
            if (_wrap && ExpeditionTerrain.Active && eye.HasValue) UpdateZoneView();
            if (_wrap)
            {
                DrawExpeditionWorld();
                DrawRaid();
            }

            DrawPlayers(world, local);
            DrawCombat(world, local);
            DrawLoot(world, local);
            DrawFeedback(world, local);
            DrawPings();

            if (eye.HasValue && viewCamera != null)
            {
                var look = ArenaLocalInputSystem.LocalLook;
                viewCamera.orthographic = false;
                // Right mouse zooms by the gun's zoom (Scope mods add more).
                float zoom = ArenaLocalInputSystem.Zooming && _localZoom > 1f ? _localZoom : 1f;
                ArenaLocalInputSystem.ZoomFactor = zoom;
                float fov = 2f * Mathf.Atan(Mathf.Tan(40f * Mathf.Deg2Rad) / zoom) * Mathf.Rad2Deg;
                viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, fov, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
                viewCamera.nearClipPlane = 0.05f;
                viewCamera.transform.SetPositionAndRotation(eye.Value, Quaternion.Euler(-look.y, look.x, 0f));
            }
            // The gun in hand is posed after the camera moves, so it never trails a turn.
            if (local != Entity.Null && world.EntityManager.HasComponent<ArenaLoadout>(local))
            {
                var em = world.EntityManager;
                int myId = em.GetComponentData<GhostOwner>(local).NetworkId;
                AddHeldGun(em.GetComponentData<ArenaLoadout>(local), em.GetComponentData<ArenaVitals>(local).Health > 0f,
                    SeedOf(em, local, myId), PlayerColors.Index(myId));
            }
            DrawGuns();

            _center.text = "";
            if (_wrap) WriteExpeditionHud();
            WriteCombatHud(world, local);
            WriteFeedbackHud(world, local);
            _hud.Apply();
        }

        private Light[] _sceneLights;

        /// <summary>Tells the zone world where the camera is (zones stream in and take over the sky and sun around it).</summary>
        private void UpdateZoneView()
        {
            var zones = ExpeditionTerrain.World;
            zones.ViewPoint = (Vector3)_camRef;
            if (zones.ReadyCount == 0 || viewCamera == null) return;
            viewCamera.clearFlags = CameraClearFlags.Skybox;
            // The zones' look relies on post-processing (tone mapping, bloom, colour grading).
            var urp = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(viewCamera);
            if (!urp.renderPostProcessing)
            {
                urp.renderPostProcessing = true;
                urp.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
            if (_sceneLights == null)
            {
                // The zones bring their own sun.
                _sceneLights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var l in _sceneLights)
                {
                    if (l.gameObject.scene == gameObject.scene) l.enabled = false;
                }
            }
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
            using var owners = _players.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            BeginRobots();
            var em = world.EntityManager;
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] == local) continue;
                var e = entities[i];
                int id = owners[i].NetworkId;
                bool downed = em.HasComponent<ArenaVitals>(e) && em.GetComponentData<ArenaVitals>(e).Health <= 0f;
                ArenaLoadout? lo = em.HasComponent<ArenaLoadout>(e) ? em.GetComponentData<ArenaLoadout>(e) : (ArenaLoadout?)null;
                DrawRobot(e, id, Near(transforms[i].Position), looks[i].Yaw, looks[i].Pitch, SeedOf(em, e, id),
                    PlayerColors.Index(id), lo, downed);
            }
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
