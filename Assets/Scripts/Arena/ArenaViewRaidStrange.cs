using System.Collections.Generic;
using System.Text;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.ZoneArt;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The strange rooms in the view (Docs/RaidDevPlan.md, phase 3): the dark of Lanterns with a light around
    /// each glowing player, the Sentinel's turning shell and its eye, the Weaver's falling floor and the
    /// threads it ties, and the Fog modifier. Fog and the dark override the zones' look for the frame
    /// (ZoneWorld.AfterLook), so they end as soon as the player leaves.
    /// </summary>
    public partial class ArenaView
    {
        private static readonly Color LanternColor = new Color(1f, 0.82f, 0.45f);
        private static readonly Color SilkColor = new Color(0.82f, 0.95f, 1f);
        private static readonly Color FogColor = new Color(0.55f, 0.56f, 0.62f);
        private const int LightPool = 8;

        private Material _lanternMat, _lanternRingMat, _tetherMat, _tetherWornMat, _crackMat, _eyeShellMat, _eyeOpenMat, _eyeWarnMat, _silkMat;
        private readonly List<Matrix4x4> _sLantern = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _sLanternRing = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _sTether = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _sTetherWorn = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _sSilk = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qEyeShell = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qEyeOpen = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qEyeWarn = new List<Matrix4x4>();
        private readonly Dictionary<int, Vector3> _playerFeet = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, float> _playerGlow = new Dictionary<int, float>();
        private Light[] _lanternLights;

        /// <summary>0 = the zones' own look, 1 = the Fog modifier, 2 = the dark of Lanterns.</summary>
        private int _raidLook;
        private bool _lookHooked;
        /// <summary>This frame: the Weaver's fight is on (its floor and threads are live).</summary>
        private bool _weaverLive;
        /// <summary>Where the raid boss was last drawn (the Weaver's end of a thread).</summary>
        private Vector3 _bossPos;
        private int _localNetId;

        private void CreateStrangeMaterials()
        {
            _lanternMat = MakeGlow(LanternColor, 3f);
            _lanternRingMat = MakeGlow(LanternColor * 0.7f, 0.9f);
            _tetherMat = MakeGlow(SilkColor, 2.6f);
            _tetherWornMat = MakeGlow(KeyRedColor, 2.4f);
            _crackMat = MakeGlow(PlateWarnColor, 1.6f);
            _eyeShellMat = MakeMat(new Color(0.2f, 0.18f, 0.24f));
            _eyeOpenMat = MakeGlow(new Color(1f, 0.3f, 0.2f), 3.2f);
            _eyeWarnMat = MakeGlow(PlateWarnColor, 1.6f);
            _silkMat = MakeGlow(SilkColor * 0.8f, 0.9f);
        }

        /// <summary>Called from DrawRaid each frame: the Weaver's floor and threads, the lanterns, and which look to use.</summary>
        private void DrawStrange(ExpeditionStatus s)
        {
            if (_lanternMat == null) CreateStrangeMaterials();
            if (!_lookHooked)
            {
                ZoneWorld.AfterLook += ApplyRaidLook;
                _lookHooked = true;
            }
            bool inRun = s.InRaid && s.RaidStageCount > 0;
            _weaverLive = inRun && s.RaidRoom == RaidRoomKind.Boss && s.RaidBoss == RaidBossKind.Weaver;
            if (_weaverLive) RaidArena.SetBossFloor(s.RaidTilesFallen);

            bool lanterns = inRun && s.RaidRoom == RaidRoomKind.Lanterns;
            _raidLook = lanterns && s.RaidPortal < -1.5f ? 2 : inRun && s.RaidModifier == RaidModifier.Fog ? 1 : 0;

            foreach (var l in new[] { _sLantern, _sLanternRing, _sTether, _sTetherWorn, _sSilk }) l.Clear();
            GatherPlayers();
            DrawLanterns(lanterns);
            if (_weaverLive) DrawThreads(s);
            DrawList(_sLantern, _lanternMat);
            DrawList(_sLanternRing, _lanternRingMat);
            DrawList(_sTether, _tetherMat);
            DrawList(_sTetherWorn, _tetherWornMat);
            DrawList(_sSilk, _silkMat);
        }

        private void OnDestroy()
        {
            if (_lookHooked) ZoneWorld.AfterLook -= ApplyRaidLook;
        }

        /// <summary>Every player's feet and glow, by network id (for lanterns and threads).</summary>
        private void GatherPlayers()
        {
            _playerFeet.Clear();
            _playerGlow.Clear();
            _localNetId = 0;
            var world = ClientServerBootstrap.ClientWorld;
            if (world == null || !world.IsCreated || _queryWorld != world) return;
            var em = world.EntityManager;
            using (var entities = _players.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    int id = em.GetComponentData<GhostOwner>(e).NetworkId;
                    _playerFeet[id] = Near(em.GetComponentData<LocalTransform>(e).Position);
                    _playerGlow[id] = em.HasComponent<ArenaBuffs>(e) ? em.GetComponentData<ArenaBuffs>(e).Glow : 0f;
                    if (em.HasComponent<GhostOwnerIsLocal>(e)) _localNetId = id;
                }
            }
        }

        /// <summary>
        /// Lanterns: a lantern over each glowing player, a ring on the floor where its light reaches, and a
        /// real light. Lights fade in the glow's last two seconds.
        /// </summary>
        private void DrawLanterns(bool lanterns)
        {
            if (_lanternLights == null)
            {
                _lanternLights = new Light[LightPool];
                for (int i = 0; i < LightPool; i++)
                {
                    var go = new GameObject("Lantern light");
                    go.transform.SetParent(transform, false);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = LanternColor;
                    light.shadows = LightShadows.None;
                    light.range = Lanterns.LightRadius + 2.5f;
                    light.enabled = false;
                    _lanternLights[i] = light;
                }
            }
            int used = 0;
            float t = Time.time;
            if (lanterns)
            {
                foreach (var kv in _playerGlow)
                {
                    if (kv.Value <= 0f || !_playerFeet.TryGetValue(kv.Key, out var feet)) continue;
                    float fade = math.saturate(kv.Value / 2f);
                    var lamp = feet + new Vector3(0f, 2.3f + 0.08f * Mathf.Sin(t * 3f + kv.Key), 0f);
                    Box(_sLantern, lamp, Quaternion.Euler(45f, t * 70f, 45f), Vector3.one * (0.22f + 0.12f * fade));
                    const int segments = 24;
                    float seg = 2f * Mathf.PI * Lanterns.LightRadius / segments * 0.5f;
                    for (int k = 0; k < segments; k++)
                    {
                        float a = 2f * Mathf.PI * (k + 0.5f) / segments + t * 0.2f;
                        var at = feet + new Vector3(Mathf.Sin(a) * Lanterns.LightRadius, 0.06f, Mathf.Cos(a) * Lanterns.LightRadius);
                        Box(_sLanternRing, at, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), new Vector3(seg, 0.04f, 0.12f));
                    }
                    if (used >= LightPool) continue;
                    var light = _lanternLights[used++];
                    light.transform.position = feet + new Vector3(0f, Lanterns.LightHeight + 0.6f, 0f);
                    light.intensity = 4f * fade;
                    light.enabled = true;
                }
            }
            for (int i = used; i < LightPool; i++) _lanternLights[i].enabled = false;
        }

        /// <summary>The Weaver's threads: a glowing line between their ends, reddening as they're shot.</summary>
        private void DrawThreads(ExpeditionStatus s)
        {
            float t = Time.time;
            for (int i = 0; i < RaidWeaver.MaxTethers; i++)
            {
                if (!RaidWeaver.UnpackTether(s.RaidTethers, i, out int a, out int b, out int hits)) continue;
                if (!_playerFeet.TryGetValue(a, out var feetA)) continue;
                var from = feetA + Vector3.up * 1.1f;
                Vector3 to;
                if (b == WeaverTether.WeaverEnd) to = _bossPos;
                else if (_playerFeet.TryGetValue(b, out var feetB)) to = feetB + Vector3.up * 1.1f;
                else continue;
                var list = hits <= RaidWeaver.TetherHits / 2 ? _sTetherWorn : _sTether;
                var d = to - from;
                float len = d.magnitude;
                if (len < 0.1f) continue;
                float wobble = 0.06f * Mathf.Sin(t * 9f + i);
                var mid = (from + to) * 0.5f;
                Box(list, mid, Quaternion.LookRotation(d / len), new Vector3(0.09f + wobble, 0.09f - wobble, len));
                // Knots along it, one per hit it still needs.
                for (int k = 0; k < hits; k++)
                {
                    var at = from + d * ((k + 1f) / (hits + 1f));
                    Box(list, at, Quaternion.Euler(45f, t * 120f + k * 40f, 45f), Vector3.one * 0.22f);
                }
            }
            // Silk from the Weaver to the corner spires: it hangs in its web.
            var c = Near(RaidArena.Center);
            float hs = RaidArena.HalfSize + 0.5f;
            for (int k = 0; k < 4; k++)
            {
                var spire = c + new Vector3(((k & 1) == 0 ? 1f : -1f) * hs, 8.6f, ((k & 2) == 0 ? 1f : -1f) * hs);
                var d = spire - _bossPos;
                if (d.sqrMagnitude < 1f) continue;
                Box(_sSilk, (spire + _bossPos) * 0.5f, Quaternion.LookRotation(d.normalized), new Vector3(0.05f, 0.05f, d.magnitude));
            }
        }

        /// <summary>
        /// The Weaver's floor in DrawArena: standing tiles as slabs (cracking ones shake and glow orange),
        /// nothing where a tile fell. True if it drew the floor (so the solid one is skipped).
        /// </summary>
        private bool DrawWeaverFloor(ExpeditionStatus s, Vector3 c, List<Matrix4x4> slabs, List<Matrix4x4> tileA, List<Matrix4x4> tileB, List<Matrix4x4> cracks)
        {
            if (!_weaverLive) return false;
            float t = Time.time;
            float ts = RaidWeaver.TileSize;
            for (int i = 0; i < RaidWeaver.Grid * RaidWeaver.Grid; i++)
            {
                if (RaidWeaver.IsSet(s.RaidTilesFallen, i)) continue;
                var tc = RaidWeaver.TileCenter(i);
                bool cracking = RaidWeaver.IsSet(s.RaidTilesCracking, i);
                var shake = cracking ? new Vector3(Mathf.Sin(t * 37f + i) * 0.05f, 0f, Mathf.Cos(t * 41f + i) * 0.05f) : Vector3.zero;
                var pos = c + new Vector3(tc.X, 0f, tc.Z) + shake;
                Box(slabs, pos + new Vector3(0f, -1f, 0f), Quaternion.identity, new Vector3(ts - 0.06f, 2f, ts - 0.06f));
                int gx = i % RaidWeaver.Grid, gz = i / RaidWeaver.Grid;
                Box(cracking ? cracks : (gx + gz) % 2 == 0 ? tileA : tileB, pos + new Vector3(0f, 0.02f, 0f), Quaternion.identity,
                    new Vector3(ts - 0.3f, 0.04f, ts - 0.3f));
                // A thin rock tooth under each tile, so the floor reads as hanging pieces.
                Box(_rRock, pos + new Vector3(0f, -2.6f, 0f), Quaternion.Euler(0f, i * 23f, 0f), new Vector3(ts * 0.5f, 1.4f, ts * 0.5f));
            }
            return true;
        }

        /// <summary>True for a floor point over a fallen tile of the Weaver's floor (decoration there is skipped).</summary>
        private bool OverHole(ExpeditionStatus s, Vector3 local) =>
            _weaverLive && RaidWeaver.IsSet(s.RaidTilesFallen, RaidWeaver.TileAt(local.x, local.z));

        /// <summary>
        /// Sentinel Eye: a faceted stone ball turning over the room, banded with glow; the eye's spot glows
        /// orange as a warning, then the eye opens there, big and red. The shell turns on the room clock.
        /// </summary>
        private void DrawEye(Vector3 c, ExpeditionStatus s, bool live, float t)
        {
            if (_lanternMat == null) CreateStrangeMaterials();
            _qEyeShell.Clear();
            _qEyeOpen.Clear();
            _qEyeWarn.Clear();
            float clock = live ? RoomClock(s) : t;
            var center = c + new Vector3(0f, SentinelEye.ShellHeight, 0f);
            float yaw = SentinelEye.ShellYaw(clock) * Mathf.Rad2Deg;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float R = SentinelEye.ShellRadius;
            Box(_qEyeShell, center, rot, Vector3.one * R * 1.55f);
            Box(_qEyeShell, center, rot * Quaternion.Euler(45f, 0f, 45f), Vector3.one * R * 1.55f);
            Box(_qEyeShell, center, rot * Quaternion.Euler(0f, 45f, 0f), new Vector3(R * 1.55f, R * 1.7f, R * 1.55f));
            Box(_qEyeShell, center, rot * Quaternion.Euler(45f, 45f, 0f), Vector3.one * R * 1.5f);
            // A glowing band around its middle and a slow ring below, so its turning is easy to see.
            for (int k = 0; k < 12; k++)
            {
                var a = rot * Quaternion.Euler(0f, k * 30f, 0f);
                Box(_qGlow, center + a * new Vector3(0f, 0f, R * 1.02f), a, new Vector3(0.9f, 0.14f, 0.1f));
            }
            Box(_qGlow, c + new Vector3(0f, 0.06f, 0f), Quaternion.Euler(0f, -yaw, 0f), new Vector3(6f, 0.05f, 6f));
            if (live && s.RaidPortal < -1.5f)
            {
                int packed = s.RaidRoomData;
                bool open = SentinelEye.OpenOf(packed), warn = SentinelEye.WarningOf(packed);
                if (open || warn)
                {
                    var spot = SentinelEye.EyeAt(SentinelEye.AzimuthOf(packed), SentinelEye.ElevationOf(packed), clock);
                    var p = c + new Vector3(spot.X, spot.Y, spot.Z);
                    var outward = (p - center).normalized;
                    var face = Quaternion.LookRotation(outward);
                    if (open)
                    {
                        float size = SentinelEye.EyeRadius * 2f;
                        Box(_qEyeOpen, p, face * Quaternion.Euler(0f, 0f, 45f + t * 90f), new Vector3(size, size, size * 0.5f));
                        Box(_qEyeShell, p + outward * (size * 0.28f), face, new Vector3(size * 0.35f, size * 0.35f, 0.1f));
                        // A beam out of the eye so it's found at a glance.
                        Box(_qEyeOpen, p + outward * 2f, face, new Vector3(0.12f, 0.12f, 4f));
                    }
                    else
                    {
                        float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * 14f));
                        Box(_qEyeWarn, p - outward * 0.05f, face * Quaternion.Euler(0f, 0f, 45f), new Vector3(0.9f * pulse, 0.9f * pulse, 0.2f));
                    }
                }
            }
            DrawList(_qEyeShell, _eyeShellMat);
            DrawList(_qEyeOpen, _eyeOpenMat);
            DrawList(_qEyeWarn, _eyeWarnMat);
        }

        /// <summary>The ZoneWorld hook: the raid's fog or dark over the zones' look, for this frame.</summary>
        private void ApplyRaidLook()
        {
            if (this == null || _raidLook == 0 || viewCamera == null) return;
            bool dark = _raidLook == 2;
            var color = dark ? new Color(0.01f, 0.01f, 0.025f) : FogColor;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = color;
            RenderSettings.fogStartDistance = dark ? 3f : 6f;
            RenderSettings.fogEndDistance = dark ? 24f : 34f;
            if (dark)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.035f, 0.035f, 0.06f);
                RenderSettings.ambientIntensity = 1f;
                if (RenderSettings.sun != null) RenderSettings.sun.intensity = 0.02f;
            }
            viewCamera.clearFlags = CameraClearFlags.SolidColor;
            viewCamera.backgroundColor = color;
        }

        /// <summary>HUD lines for the strange rooms and the Weaver.</summary>
        private void WriteStrangeHud(ExpeditionStatus s, StringBuilder e)
        {
            switch (s.RaidRoom)
            {
                case RaidRoomKind.Lanterns:
                {
                    int kills = Lanterns.KillsOf(s.RaidRoomData), target = Lanterns.TargetOf(s.RaidRoomData);
                    float glow = s.RaidRoomTimer;
                    string you = glow > 0f
                        ? $"<color=#FFD27A>you glow ({Mathf.CeilToInt(glow)}s)</color>"
                        : "<color=#FF9A40>you're dark: an ally's payload hit lights you</color>";
                    e.Append($"Shades {kills}/{target}  {Bar(target > 0 ? (float)kills / target : 0f, 20)}  {you}\n");
                    break;
                }
                case RaidRoomKind.SentinelEye:
                {
                    int hits = SentinelEye.HitsOf(s.RaidRoomData), target = SentinelEye.TargetOf(s.RaidRoomData);
                    string state = SentinelEye.OpenOf(s.RaidRoomData) ? "<color=#FF6B5B><b>EYE OPEN: shoot it!</b></color>"
                        : SentinelEye.WarningOf(s.RaidRoomData) ? "<color=#FF9A40>it's about to open (orange spot)</color>"
                        : "<color=#B8B0D8>watch the shell</color>";
                    e.Append($"Eye hits {hits}/{target}  {Bar(target > 0 ? (float)hits / target : 0f, 20)}  {state}\n");
                    break;
                }
                case RaidRoomKind.Boss:
                {
                    if (s.RaidBoss != RaidBossKind.Weaver) break;
                    int threads = 0;
                    string tiedTo = null;
                    for (int i = 0; i < RaidWeaver.MaxTethers; i++)
                    {
                        if (!RaidWeaver.UnpackTether(s.RaidTethers, i, out int a, out int b, out _)) continue;
                        threads++;
                        if (a == _localNetId) tiedTo = b == WeaverTether.WeaverEnd ? "the Weaver" : PlayerColors.Tag(b);
                        else if (b == _localNetId) tiedTo = PlayerColors.Tag(a);
                    }
                    if (threads > 0)
                        e.Append($"<color=#D0F0FF><b>{threads} THREAD{(threads == 1 ? "" : "S")}</b>: the Weaver takes 1/4 damage</color>\n");
                    if (tiedTo != null)
                        e.Append($"<color=#FF9A40><b>YOU'RE TIED</b> to {tiedTo}: someone else must shoot your thread before it snaps</color>\n");
                    break;
                }
            }
        }

        private string StrangeAnnouncement(ArenaAnnounce m)
        {
            switch (m.Kind)
            {
                case AnnounceKind.TetherTied:
                    return $"<color=#D0F0FF><b>THE WEAVER TIES YOU!</b> {m.Value} thread{(m.Value == 1 ? "" : "s")}: shoot the others' threads apart</color>\n";
                case AnnounceKind.TetherSnapped:
                    return m.OtherNetworkId == 0
                        ? $"<color=#FF8080>A thread snapped on {PlayerColors.Tag(m.PlayerNetworkId)}</color>\n"
                        : $"<color=#FF8080>A thread snapped on {PlayerColors.Tag(m.PlayerNetworkId)} and {PlayerColors.Tag(m.OtherNetworkId)}</color>\n";
                case AnnounceKind.TetherCut:
                    return $"<color=#D0F0FF>{PlayerColors.Tag(m.PlayerNetworkId)} cut a thread</color>\n";
                default:
                    return null;
            }
        }
    }
}
