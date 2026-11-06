using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using Unity.Mathematics;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The raid half of the view (MVP step 6): the raid door on the ground with a beam over it, the
    /// floating arena, the Bulwark with its turning plate, and the raid lines of the HUD.
    /// Glowing parts use the emissive ArenaGlow material, so the zones' bloom picks them up.
    /// The arena's crystals and the boss's dais glow in the plate's state (shut, opening, open), so
    /// players can read the plate from anywhere on the platform.
    /// </summary>
    public partial class ArenaView
    {
        private static readonly Color RaidViolet = new Color(0.72f, 0.42f, 1f);
        private static readonly Color PlateWarnColor = new Color(1f, 0.5f, 0.12f);
        private static readonly Color PlateOpenColor = new Color(1f, 0.82f, 0.25f);
        private static readonly Color EnragedColor = new Color(1f, 0.22f, 0.3f);
        private static readonly Color KeyRedColor = new Color(1f, 0.3f, 0.25f);
        private static readonly Color KeyGreenColor = new Color(0.35f, 1f, 0.4f);

        private Material _glowBase;
        private Material _raidFloorMat, _raidTileAMat, _raidTileBMat, _raidWallMat, _raidRockMat;
        private Material _raidGlowMat, _raidStateMat, _raidBeamMat;
        private Material _doorMat, _doorGlowMat, _doorPanelMat, _doorRingMat, _runeOffMat, _runeRedMat, _runeGreenMat;
        private Material _bossMat, _bossArmorMat, _bossSeamMat, _bossThrustMat, _plateShutMat, _plateWarnMat, _plateOpenMat;

        // Arena
        private readonly List<Matrix4x4> _rFloor = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _rTileA = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _rTileB = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _rWall = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _rRock = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _rGlow = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _rState = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _rBeam = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _rCrack = new List<Matrix4x4>();
        // Door
        private readonly List<Matrix4x4> _dStone = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _dGlow = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _dPanel = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _dRing = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _dRuneOff = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _dRuneRed = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _dRuneGreen = new List<Matrix4x4>();
        private float[] _doorRingGround;
        private System.Numerics.Vector2 _doorRingFor;
        // Boss
        private readonly List<Matrix4x4> _bossBodies = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _bossTelegraph = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _bossArmor = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _bossSeams = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _bossThrust = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _bossColumn = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _platesShut = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _platesWarn = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _platesOpen = new List<Matrix4x4>();

        private static readonly RaidSettings RaidRules = new RaidSettings();
        private static readonly BulwarkSettings BossLook = new BulwarkSettings();
        private float _bossHealth, _bossMax;
        private readonly float[] _twinPlateAnim = new float[2];
        private readonly float[] _twinHealth = new float[2];
        private readonly bool[] _twinOpen = new bool[2];
        private float _twinMax;
        private bool _bossSeen, _bossOpen, _bossWarn;
        /// <summary>Last frame's boss state, for the arena's state-colored glow (drawn before the enemies).</summary>
        private bool _bossVisible, _bossTele, _bossEnraged;
        /// <summary>0 = plate shut, 1 = fully open; eases so the shutters slide.</summary>
        private float _plateAnim;

        private void CreateRaidMaterials()
        {
            _glowBase = Resources.Load<Material>("ArenaGlow");

            _raidFloorMat = MakeMat(new Color(0.2f, 0.18f, 0.25f));
            _raidTileAMat = MakeMat(new Color(0.33f, 0.3f, 0.4f));
            _raidTileBMat = MakeMat(new Color(0.27f, 0.24f, 0.34f));
            _raidWallMat = MakeMat(new Color(0.42f, 0.36f, 0.52f));
            _raidRockMat = MakeMat(new Color(0.23f, 0.2f, 0.27f));
            _raidGlowMat = MakeGlow(RaidViolet, 1.3f);
            _raidStateMat = MakeGlow(RaidViolet, 1.4f);
            _raidBeamMat = MakeGlow(RaidViolet, 0.5f);

            _doorMat = MakeMat(new Color(0.38f, 0.22f, 0.5f));
            _doorGlowMat = MakeGlow(new Color(0.85f, 0.5f, 1f), 1.3f);
            _doorPanelMat = MakeGlow(new Color(0.5f, 0.22f, 0.85f), 0.9f);
            _doorRingMat = MakeGlow(RaidViolet, 1f);
            _runeOffMat = MakeMat(new Color(0.18f, 0.12f, 0.24f));
            _runeRedMat = MakeGlow(KeyRedColor, 1.8f);
            _runeGreenMat = MakeGlow(KeyGreenColor, 1.8f);

            _bossMat = MakeMat(new Color(0.3f, 0.2f, 0.4f));
            _bossArmorMat = MakeMat(new Color(0.48f, 0.42f, 0.56f));
            _bossSeamMat = MakeGlow(RaidViolet, 1.6f);
            _bossThrustMat = MakeGlow(RaidViolet, 1.4f);
            _plateShutMat = MakeMat(new Color(0.55f, 0.5f, 0.45f));
            _plateWarnMat = MakeGlow(PlateWarnColor, 1.8f);
            _plateOpenMat = MakeGlow(PlateOpenColor, 2.2f);
            CreateStrangeMaterials();
        }

        /// <summary>An emissive material (bloom makes it glow); a plain one if the glow material is missing.</summary>
        private Material MakeGlow(Color color, float intensity)
        {
            if (_glowBase == null) _glowBase = Resources.Load<Material>("ArenaGlow");
            if (_glowBase == null) return MakeMat(color);
            var m = new Material(_glowBase) { enableInstancing = true, color = color };
            m.SetColor("_EmissionColor", color * intensity);
            return m;
        }

        private static void SetGlow(Material m, Color color, float intensity)
        {
            m.color = color;
            m.SetColor("_EmissionColor", color * intensity);
        }

        private static void Box(List<Matrix4x4> list, Vector3 position, Quaternion rotation, Vector3 size)
        {
            list.Add(Matrix4x4.TRS(position, rotation, size));
        }

        /// <summary>Small stable hash in [0, 1) for placing decoration.</summary>
        private static float Hash01(int i, int salt)
        {
            uint h = (uint)(i * 374761393 + salt * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / 4294967296f;
        }

        /// <summary>The door on the ground, and the arena when the camera is up there.</summary>
        private void DrawRaid()
        {
            RaidArena.Ensure();
            TryGetStatus(out var status);
            DrawStrange(status);

            // The arena's state glow follows the boss's plate (last frame's state).
            Color state = RaidViolet;
            float stateGlow = 1.3f;
            if (_bossVisible)
            {
                if (_bossOpen) { state = PlateOpenColor; stateGlow = 2.2f; }
                else if (_bossWarn) { state = PlateWarnColor; stateGlow = 1.3f + 1.1f * Mathf.Abs(Mathf.Sin(Time.time * 9f)); }
                else if (_bossEnraged) state = EnragedColor;
            }
            SetGlow(_raidStateMat, state, stateGlow);

            // Hidden in the fog from the ground anyway, so only drawn when the camera is near.
            if (RaidArena.Contains(_camRef, 200f, 200f)) DrawArena(status);
            if (RaidArena.InRaidSpace(_camRef)) DrawRooms(status);
            DrawDoor(status);
        }

        private void DrawArena(ExpeditionStatus s)
        {
            foreach (var l in new[] { _rFloor, _rTileA, _rTileB, _rWall, _rRock, _rGlow, _rState, _rBeam, _rCrack }) l.Clear();
            var c = Near(RaidArena.Center);
            float t = Time.time;
            float hs = RaidArena.HalfSize;
            var id = Quaternion.identity;
            // The Weaver's floor comes in pieces, some cracking, some gone.
            bool tiles = DrawWeaverFloor(s, c, _rFloor, _rTileA, _rTileB, _rCrack);

            foreach (var b in RaidArena.Boxes)
            {
                if (b.IsFloor && tiles) continue;
                Box(b.IsFloor ? _rFloor : _rWall, Near(b.Center), id, b.Size);
            }

            // Checkered floor tiles with dark grout between them.
            const int n = 8;
            float ts = 2f * hs / n;
            for (int i = 0; i < n && !tiles; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    var pos = c + new Vector3((i + 0.5f) * ts - hs, 0.02f, (j + 0.5f) * ts - hs);
                    Box((i + j) % 2 == 0 ? _rTileA : _rTileB, pos, id, new Vector3(ts - 0.3f, 0.04f, ts - 0.3f));
                }
            }

            // A glowing ring inlaid where players arrive.
            const int ringSegments = 36;
            float ringLength = 2f * Mathf.PI * RaidArena.SpawnRing / ringSegments * 0.62f;
            for (int k = 0; k < ringSegments; k++)
            {
                float a = 2f * Mathf.PI * (k + 0.5f) / ringSegments;
                var local = new Vector3(Mathf.Sin(a) * RaidArena.SpawnRing, 0.05f, Mathf.Cos(a) * RaidArena.SpawnRing);
                if (OverHole(s, local)) continue;
                Box(_rGlow, c + local, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), new Vector3(ringLength, 0.05f, 0.25f));
            }

            // The dais under the boss: an octagon of two slabs and a slowly turning rune ring in the plate's color.
            // (The Weaver hangs in its web over open air instead.)
            if (!tiles)
            {
                Box(_rWall, c + new Vector3(0f, 0.06f, 0f), id, new Vector3(9f, 0.12f, 9f));
                Box(_rWall, c + new Vector3(0f, 0.06f, 0f), Quaternion.Euler(0f, 45f, 0f), new Vector3(9f, 0.12f, 9f));
                Box(_rTileA, c + new Vector3(0f, 0.1f, 0f), id, new Vector3(6f, 0.12f, 6f));
                Box(_rTileA, c + new Vector3(0f, 0.1f, 0f), Quaternion.Euler(0f, 45f, 0f), new Vector3(6f, 0.12f, 6f));
            }
            const int runes = 16;
            for (int k = 0; k < runes && !tiles; k++)
            {
                float a = 2f * Mathf.PI * k / runes + t * 0.25f;
                var pos = c + new Vector3(Mathf.Sin(a) * 5.4f, 0.14f, Mathf.Cos(a) * 5.4f);
                Box(_rState, pos, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), new Vector3(1.1f, 0.06f, 0.35f));
            }

            // Battlements on the walls, with a glowing trim along their inner faces.
            float wallLength = 2f * hs + 2f;
            const int merlons = 14;
            for (int k = 0; k < merlons; k++)
            {
                float along = -wallLength / 2f + wallLength * (k + 0.5f) / merlons;
                float y = RaidArena.WallHeight + 0.4f;
                var size = new Vector3(1.7f, 0.8f, 1.1f);
                Box(_rWall, c + new Vector3(along, y, hs + 0.5f), id, size);
                Box(_rWall, c + new Vector3(along, y, -hs - 0.5f), id, size);
                Box(_rWall, c + new Vector3(hs + 0.5f, y, along), id, new Vector3(size.z, size.y, size.x));
                Box(_rWall, c + new Vector3(-hs - 0.5f, y, along), id, new Vector3(size.z, size.y, size.x));
            }
            float trimY = RaidArena.WallHeight - 0.3f;
            Box(_rGlow, c + new Vector3(0f, trimY, hs - 0.02f), id, new Vector3(2f * hs, 0.1f, 0.06f));
            Box(_rGlow, c + new Vector3(0f, trimY, -hs + 0.02f), id, new Vector3(2f * hs, 0.1f, 0.06f));
            Box(_rGlow, c + new Vector3(hs - 0.02f, trimY, 0f), id, new Vector3(0.06f, 0.1f, 2f * hs));
            Box(_rGlow, c + new Vector3(-hs + 0.02f, trimY, 0f), id, new Vector3(0.06f, 0.1f, 2f * hs));

            // Pillars: a capstone, a glowing band, and a floating crystal in the plate's color.
            for (int k = 0; k < 4; k++)
            {
                float sx = (k & 1) == 0 ? 1f : -1f, sz = (k & 2) == 0 ? 1f : -1f;
                var basePos = c + new Vector3(sx * 10f, 0f, sz * 10f);
                float ph = RaidArena.PillarHeight;
                Box(_rWall, basePos + new Vector3(0f, ph + 0.15f, 0f), id, new Vector3(RaidArena.PillarSize + 0.5f, 0.3f, RaidArena.PillarSize + 0.5f));
                Box(_rWall, basePos + new Vector3(0f, 0.2f, 0f), id, new Vector3(RaidArena.PillarSize + 0.6f, 0.4f, RaidArena.PillarSize + 0.6f));
                Box(_rGlow, basePos + new Vector3(0f, ph - 0.5f, 0f), id, new Vector3(RaidArena.PillarSize + 0.08f, 0.14f, RaidArena.PillarSize + 0.08f));
                float bob = 0.25f * Mathf.Sin(t * 1.4f + k * 1.7f);
                Box(_rState, basePos + new Vector3(0f, ph + 1.5f + bob, 0f), Quaternion.Euler(45f, t * 50f + k * 90f, 35f), Vector3.one * 0.95f);
            }

            // Corner spires with a crystal and a faint beam into the sky.
            for (int k = 0; k < 4; k++)
            {
                float sx = (k & 1) == 0 ? 1f : -1f, sz = (k & 2) == 0 ? 1f : -1f;
                var corner = c + new Vector3(sx * (hs + 0.5f), 0f, sz * (hs + 0.5f));
                Box(_rWall, corner + new Vector3(0f, 1.7f, 0f), Quaternion.Euler(0f, 45f, 0f), new Vector3(2.6f, 3.4f, 2.6f));
                Box(_rWall, corner + new Vector3(0f, 4.4f, 0f), Quaternion.Euler(0f, 45f, 0f), new Vector3(1.4f, 2f, 1.4f));
                Box(_rGlow, corner + new Vector3(0f, 6.6f, 0f), Quaternion.Euler(0f, 45f, 0f), new Vector3(0.35f, 2.4f, 0.35f));
                Box(_rGlow, corner + new Vector3(0f, 8.6f + 0.2f * Mathf.Sin(t * 2f + k), 0f), Quaternion.Euler(45f, -t * 70f, 45f), Vector3.one * 0.7f);
                Box(_rBeam, corner + new Vector3(0f, 50f, 0f), id, new Vector3(0.22f, 80f, 0.22f));
            }

            // The platform floats: a stepped rock root under it, with crystals hanging from its tip.
            float[] widths = { 50f, 42f, 32f, 21f, 10f };
            float[] heights = { 3f, 4f, 5f, 6f, 6f };
            float y0 = -2f;
            for (int k = 0; k < widths.Length && !tiles; k++)
            {
                float yaw = k * 11f;
                Box(_rRock, c + new Vector3(0f, y0 - heights[k] / 2f, 0f), Quaternion.Euler(0f, yaw, 0f), new Vector3(widths[k], heights[k], widths[k]));
                y0 -= heights[k] * 0.85f;
            }
            for (int k = 0; k < 7; k++)
            {
                float a = k * 2f * Mathf.PI / 7f;
                float r = k == 0 ? 0f : 3.5f + Hash01(k, 3) * 4f;
                float len = 3f + Hash01(k, 4) * 4f;
                var top = c + new Vector3(Mathf.Sin(a) * r, y0 + 6f - r * 0.8f, Mathf.Cos(a) * r);
                Box(_rGlow, top - new Vector3(0f, len / 2f, 0f), Quaternion.Euler(Hash01(k, 5) * 16f - 8f, a * Mathf.Rad2Deg, Hash01(k, 6) * 16f - 8f), new Vector3(0.6f, len, 0.6f));
            }

            // Rocks drifting around the platform; some carry a crystal.
            for (int k = 0; k < 14; k++)
            {
                float radius = 34f + Hash01(k, 1) * 18f;
                float a = (Hash01(k, 2) * 360f + t * (1.2f + Hash01(k, 7) * 1.5f)) * Mathf.Deg2Rad;
                float y = -10f + Hash01(k, 8) * 22f + 0.6f * Mathf.Sin(t * 0.5f + k);
                var pos = c + new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);
                float size = 1.8f + Hash01(k, 9) * 3.5f;
                Box(_rRock, pos, Quaternion.Euler(Hash01(k, 10) * 40f, a * Mathf.Rad2Deg * 2f, Hash01(k, 11) * 40f), new Vector3(size, size * 0.7f, size));
                if (k % 3 == 0)
                {
                    Box(_rGlow, pos + new Vector3(0f, size * 0.55f, 0f), Quaternion.Euler(45f, t * 40f + k * 30f, 45f), Vector3.one * 0.55f);
                }
            }

            DrawList(_rFloor, _raidFloorMat);
            DrawList(_rTileA, _raidTileAMat);
            DrawList(_rTileB, _raidTileBMat);
            DrawList(_rWall, _raidWallMat);
            DrawList(_rRock, _raidRockMat);
            DrawList(_rGlow, _raidGlowMat);
            DrawList(_rState, _raidStateMat);
            DrawList(_rBeam, _raidBeamMat);
            DrawList(_rCrack, _crackMat);
        }

        /// <summary>
        /// The door: posts with key runes (red on the left, green on the right, lit as the key fills),
        /// a panel that fills, a ring on the ground where to stand, motes swirling while it gathers, and a beam.
        /// </summary>
        private void DrawDoor(ExpeditionStatus status)
        {
            foreach (var l in new[] { _dStone, _dGlow, _dPanel, _dRing, _dRuneOff, _dRuneRed, _dRuneGreen }) l.Clear();
            var door = RaidArena.Door;
            var p = Near(ExpeditionWorld.ToFloat3(door, GroundAt(door)));
            var id = Quaternion.identity;
            float t = Time.time;
            const float w = 7f, h = 8f;
            bool gathering = status.RaidPhase == RaidPhase.Gathering;

            // Posts with plinths and caps, a lintel with a keystone, and a low step.
            foreach (float side in new[] { -1f, 1f })
            {
                var post = p + new Vector3(side * w * 0.5f, 0f, 0f);
                Box(_dStone, post + new Vector3(0f, h * 0.5f, 0f), id, new Vector3(1.4f, h, 1.4f));
                Box(_dStone, post + new Vector3(0f, 0.45f, 0f), id, new Vector3(2f, 0.9f, 2f));
                Box(_dStone, post + new Vector3(0f, h - 0.2f, 0f), id, new Vector3(1.8f, 0.4f, 1.8f));
            }
            Box(_dStone, p + new Vector3(0f, h + 0.5f, 0f), id, new Vector3(w + 2.4f, 1.2f, 1.6f));
            Box(_dStone, p + new Vector3(0f, h + 1.4f, 0f), Quaternion.Euler(0f, 0f, 45f), new Vector3(1.3f, 1.3f, 1.7f));
            Box(_dGlow, p + new Vector3(0f, h + 1.4f, 0f), Quaternion.Euler(0f, 0f, 45f), new Vector3(0.6f, 0.6f, 1.75f));
            Box(_dStone, p + new Vector3(0f, 0.08f, 0f), id, new Vector3(w + 3f, 0.16f, 4f));

            // Key runes on the front and back of the posts: five each, lit by how full that color is.
            float redFill = gathering ? 1f : status.RaidPhase == RaidPhase.Collecting ? status.KeyRed / (float)RaidRules.RedCost : 0f;
            float greenFill = gathering ? 1f : status.RaidPhase == RaidPhase.Collecting ? status.KeyGreen / (float)RaidRules.GreenCost : 0f;
            for (int k = 0; k < 5; k++)
            {
                float y = 1.6f + k * 1.25f;
                bool redLit = redFill >= (k + 1) / 5f - 0.001f;
                bool greenLit = greenFill >= (k + 1) / 5f - 0.001f;
                foreach (float face in new[] { -0.72f, 0.72f })
                {
                    Box(redLit ? _dRuneRed : _dRuneOff, p + new Vector3(-w * 0.5f, y, face), id, new Vector3(0.7f, 0.7f, 0.08f));
                    Box(greenLit ? _dRuneGreen : _dRuneOff, p + new Vector3(w * 0.5f, y, face), id, new Vector3(0.7f, 0.7f, 0.08f));
                }
            }

            // The panel fills from the bottom as the key fills; it pulses while the door gathers.
            int cost = RaidRules.RedCost + RaidRules.GreenCost;
            float fill = status.RaidPhase == RaidPhase.Collecting ? math.saturate((status.KeyRed + status.KeyGreen) / (float)cost)
                : gathering ? 1f : 0f;
            if (fill > 0f)
            {
                float ph = (h - 0.5f) * fill;
                float pulse = gathering ? 1f + 0.08f * Mathf.Sin(t * 8f) : 1f;
                Box(_dPanel, p + new Vector3(0f, ph * 0.5f, 0f), id, new Vector3((w - 1.4f) * pulse, ph, 0.2f));
            }

            // While gathering: motes spiral up through the doorway.
            if (gathering)
            {
                for (int k = 0; k < 18; k++)
                {
                    float a = t * 2.4f + k * 0.35f;
                    float rise = (t * 0.6f + k / 18f) % 1f;
                    var pos = p + new Vector3(Mathf.Cos(a) * 2f * (1f - rise * 0.5f), 0.5f + rise * (h - 1f), Mathf.Sin(a) * 1.2f);
                    Box(_dGlow, pos, Quaternion.Euler(45f, a * 60f, 45f), Vector3.one * 0.28f);
                }
            }

            // A ring on the ground showing where to stand to pledge and go in.
            const int segments = 28;
            if (_doorRingGround == null || !_doorRingFor.Equals(door))
            {
                _doorRingGround = new float[segments];
                _doorRingFor = door;
                for (int k = 0; k < segments; k++) _doorRingGround[k] = float.NaN;
            }
            float segLength = 2f * Mathf.PI * RaidRules.UseRadius / segments * 0.6f;
            for (int k = 0; k < segments; k++)
            {
                float a = 2f * Mathf.PI * (k + 0.5f) / segments;
                if (float.IsNaN(_doorRingGround[k]) || _doorRingGround[k] == 0f)
                {
                    var at = door + new System.Numerics.Vector2(Mathf.Sin(a) * RaidRules.UseRadius, Mathf.Cos(a) * RaidRules.UseRadius);
                    _doorRingGround[k] = GroundAt(at);
                }
                var pos = Near(new float3(door.X + Mathf.Sin(a) * RaidRules.UseRadius, _doorRingGround[k] + 0.06f, door.Y + Mathf.Cos(a) * RaidRules.UseRadius));
                Box(_dRing, pos, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), new Vector3(segLength, 0.08f, 0.3f));
            }
            float ringGlow = gathering ? 1.5f + 1.2f * Mathf.Abs(Mathf.Sin(t * 5f))
                : status.RaidPhase == RaidPhase.Collecting ? 0.9f : 0.3f;
            SetGlow(_doorRingMat, RaidViolet, ringGlow);

            // A beam so the door can be found from across the zone; wider while it gathers.
            float beam = gathering ? 1.4f + 0.2f * Mathf.Sin(t * 6f) : 0.5f;
            Box(_dGlow, p + Vector3.up * 50f, id, new Vector3(beam, 100f, beam));

            DrawList(_dStone, _doorMat);
            DrawList(_dGlow, _doorGlowMat);
            DrawList(_dPanel, _doorPanelMat);
            DrawList(_dRing, _doorRingMat);
            DrawList(_dRuneOff, _runeOffMat);
            DrawList(_dRuneRed, _runeRedMat);
            DrawList(_dRuneGreen, _runeGreenMat);
        }

        /// <summary>
        /// Collects the boss for drawing (called from DrawEnemies); its health bar is on the HUD.
        /// The shell stays inside its hit sphere; the plate's shutters slide apart to show a gold
        /// crystal that sits where the plate's hit sphere is.
        /// </summary>
        private void AddBoss(ArenaEnemy en, Vector3 p)
        {
            if (en.World)
            {
                AddWorldBoss(en, p);
                return;
            }
            if (TryGetStatus(out var st) && st.RaidBoss == RaidBossKind.Twins)
            {
                // The Twins: west of the middle is twin 0, east is twin 1.
                int twin = p.x < Near(RaidArena.Center).x ? 0 : 1;
                AddBossShape(en, p, ref _twinPlateAnim[twin], Near(RaidArena.Center).y);
                _twinHealth[twin] = en.Health;
                _twinMax = en.MaxHealth;
                _twinOpen[twin] = en.PlateOpen;
            }
            else AddBossShape(en, p, ref _plateAnim, Near(RaidArena.Center).y);
            _bossPos = p;
            _bossSeen = true;
            _bossHealth = en.Health;
            _bossMax = en.MaxHealth;
            _bossOpen = en.PlateOpen;
            _bossWarn = en.PlateWarn;
            _bossTele = en.Telegraph;
            _bossEnraged = en.MaxHealth > 0f && en.Health / en.MaxHealth < 0.5f;
        }

        private static readonly WorldBossSettings WorldBossLook = new WorldBossSettings();
        private static readonly ColossusSettings ColossusLook = new ColossusSettings();
        private bool _colossusSeen, _colossusExposed, _colossusWarn;
        private float _colossusOpen;

        /// <summary>
        /// The Colossus (world boss): a faceted gem of a body ringed by six armor petals, a slit eye that
        /// flares before a volley, and a crystal core on top. When a slam stuns it, the petals swing open and
        /// the core turns gold: that's the time to shoot. Its health is on the HUD from the expedition status.
        /// </summary>
        private void AddWorldBoss(ArenaEnemy en, Vector3 p)
        {
            float R = ColossusLook.BodyRadius;
            float t = Time.time;
            var rot = Quaternion.Euler(0f, en.Yaw, 0f);
            var f = rot * Vector3.forward;
            var u = Vector3.up;
            _colossusOpen = Mathf.MoveTowards(_colossusOpen, en.PlateOpen ? 1f : 0f, Time.deltaTime * 3f);
            float open = _colossusOpen;

            // Body: two crossed blocks make a chunky gem inside the hit sphere.
            Box(en.Telegraph ? _bossTelegraph : _bossBodies, p, rot, Vector3.one * R * 1.45f);
            Box(_bossBodies, p, rot * Quaternion.Euler(45f, 0f, 45f), Vector3.one * R * 1.3f);

            // Six armor petals around the middle, swinging out and down when it's stunned.
            for (int k = 0; k < 6; k++)
            {
                var dir = rot * Quaternion.Euler(0f, k * 60f + 30f, 0f) * Vector3.forward;
                var face = Quaternion.LookRotation(dir) * Quaternion.Euler(-15f - 55f * open, 0f, 0f);
                Box(_bossArmor, p + dir * (R * 0.92f + open * R * 0.45f) - u * (open * R * 0.2f), face, new Vector3(R * 0.95f, R * 1.25f, 0.55f));
                Box(_bossSeams, p + dir * (R * 0.8f) + u * (R * 0.62f), face, new Vector3(R * 0.7f, 0.2f, 0.25f));
            }

            // The eye: a slit on the front that flares before each volley.
            Box(en.Telegraph ? _platesWarn : _bossSeams, p + f * (R * 0.78f) + u * (R * 0.12f), rot, new Vector3(R * 0.62f, R * 0.18f, 0.35f));

            // The core on top: dull when armored, warning-orange during a slam, bright gold when it's open.
            float coreSize = ColossusLook.CoreRadius * (1.35f + 0.15f * open * Mathf.Sin(t * 8f));
            var core = p + u * (R * 0.85f + open * 0.4f);
            Box(en.PlateOpen ? _platesOpen : en.PlateWarn ? _platesWarn : _platesShut, core,
                Quaternion.Euler(0f, t * (en.PlateOpen ? 120f : 40f), 0f) * Quaternion.Euler(45f, 0f, 45f), Vector3.one * coreSize);

            // Four claws under it.
            for (int k = 0; k < 4; k++)
            {
                var dir = rot * Quaternion.Euler(0f, k * 90f + 45f, 0f) * Vector3.forward;
                Box(_bossArmor, p - u * (R * 0.75f) + dir * (R * 0.45f), Quaternion.LookRotation(dir) * Quaternion.Euler(-35f, 0f, 0f), new Vector3(0.7f, R * 0.95f, 0.7f));
            }
            // Thrust glow while it hovers.
            if (!en.PlateOpen) Box(_bossThrust, p - u * (R * 0.98f), rot * Quaternion.Euler(0f, t * 60f, 0f), new Vector3(R * 0.9f, 0.25f, R * 0.9f));

            _colossusSeen = true;
            _colossusExposed = en.PlateOpen;
            _colossusWarn = en.PlateWarn;
        }

        /// <summary>The Bulwark's meshes at raid size around <paramref name="p"/>.</summary>
        private void AddBossShape(ArenaEnemy en, Vector3 p, ref float plateAnim, float floorY)
        {
            float t = Time.time;
            var rot = Quaternion.Euler(0f, en.Yaw, 0f);
            var f = rot * Vector3.forward;
            var r = rot * Vector3.right;
            var u = Vector3.up;
            float health = en.MaxHealth > 0f ? math.saturate(en.Health / en.MaxHealth) : 1f;

            // Core with glowing seams at its edges, armor slabs on the sides, back, top and bottom.
            Box(en.Telegraph ? _bossTelegraph : _bossBodies, p, rot, Vector3.one * 3.6f);
            Box(_bossArmor, p + r * 1.95f, rot, new Vector3(0.4f, 3f, 3f));
            Box(_bossArmor, p - r * 1.95f, rot, new Vector3(0.4f, 3f, 3f));
            Box(_bossArmor, p - f * 1.95f, rot, new Vector3(3f, 3f, 0.4f));
            Box(_bossArmor, p + u * 1.95f, rot, new Vector3(3f, 0.4f, 3f));
            Box(_bossArmor, p - u * 1.95f, rot, new Vector3(3f, 0.4f, 3f));
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    Box(_bossSeams, p + r * (sx * 1.8f) + f * (sz * 1.8f), rot, new Vector3(0.22f, 3.7f, 0.22f));
                    // Crown spikes leaning outward.
                    Box(_bossArmor, p + u * 2.5f + r * (sx * 1.25f) + f * (sz * 1.25f), rot * Quaternion.Euler(sz * 22f, 0f, -sx * 22f), new Vector3(0.4f, 1.4f, 0.4f));
                }
            }

            // Shoulder armor and a keel break up the box outline.
            foreach (float sx in new[] { -1f, 1f })
            {
                Box(_bossArmor, p + r * (sx * 1.9f) + u * 1.5f, rot * Quaternion.Euler(0f, 0f, sx * -35f), new Vector3(1.3f, 0.9f, 3.3f));
                Box(_bossSeams, p + r * (sx * 2.18f) + u * 0.4f, rot, new Vector3(0.1f, 0.18f, 2.6f));
            }
            Box(_bossArmor, p - u * 2.45f, rot * Quaternion.Euler(45f, 0f, 45f), Vector3.one * 1.1f);

            // A frame around the plate on the front face.
            Box(_bossArmor, p + f * 2.05f + u * 1.15f, rot, new Vector3(3f, 0.7f, 0.5f));
            Box(_bossArmor, p + f * 2.05f - u * 1.15f, rot, new Vector3(3f, 0.7f, 0.5f));
            Box(_bossArmor, p + f * 2.05f + r * 1.15f, rot, new Vector3(0.7f, 1.6f, 0.5f));
            Box(_bossArmor, p + f * 2.05f - r * 1.15f, rot, new Vector3(0.7f, 1.6f, 0.5f));

            // Crown of shards circling above: one goes dark for each eighth of health lost.
            int lit = Mathf.CeilToInt(health * 8f - 0.001f);
            for (int k = 0; k < 8; k++)
            {
                float a = (t * 35f + k * 45f) * Mathf.Deg2Rad;
                var pos = p + u * (3.3f + 0.15f * Mathf.Sin(t * 3f + k)) + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.7f;
                Box(k < lit ? _bossSeams : _bossArmor, pos, Quaternion.Euler(45f, t * 90f + k * 20f, 45f), Vector3.one * 0.38f);
            }

            // Hover glow under the shell and a faint column down to the floor.
            Box(_bossThrust, p - u * 2.25f, rot * Quaternion.Euler(0f, 45f + t * 40f, 0f), new Vector3(1.7f, 0.15f, 1.7f));
            float column = p.y - 2.3f - floorY;
            if (column > 0.1f) Box(_bossColumn, new Vector3(p.x, floorY + column / 2f, p.z), rot, new Vector3(0.3f, column, 0.3f));

            // The plate: shutters that slide apart to reveal the gold crystal (the hit area).
            float target = en.PlateOpen ? 1f : 0f;
            plateAnim = Mathf.MoveTowards(plateAnim, target, Time.deltaTime * 5f);
            var plate = p + f * (BossLook.BodyRadius + BossLook.PlateOut);
            float slide = 0.4f + plateAnim * 0.85f;
            var shutterMats = en.PlateWarn ? _platesWarn : _platesShut;
            Box(shutterMats, plate - f * 0.25f + r * slide, rot, new Vector3(0.78f, 1.55f, 0.3f));
            Box(shutterMats, plate - f * 0.25f - r * slide, rot, new Vector3(0.78f, 1.55f, 0.3f));
            if (en.PlateWarn) Box(_platesWarn, plate - f * 0.3f, rot, new Vector3(0.1f, 1.5f, 0.32f));
            if (plateAnim > 0.02f)
            {
                float size = BossLook.PlateRadius * 1.3f * plateAnim;
                Box(_platesOpen, plate, rot * Quaternion.Euler(0f, 0f, 45f + t * 90f), new Vector3(size, size, size));
            }
        }

        private void DrawBosses()
        {
            _bossVisible = _bossSeen;
            if (!_bossSeen) _plateAnim = 0f;
            float t = Time.time;
            // The Weaver: the same armored shell in pale silk and dark stone.
            var calm = _weaverLive ? SilkColor : RaidViolet;
            _bossMat.color = _weaverLive ? new Color(0.1f, 0.12f, 0.16f) : new Color(0.3f, 0.2f, 0.4f);
            _bossArmorMat.color = _weaverLive ? new Color(0.3f, 0.33f, 0.4f) : new Color(0.48f, 0.42f, 0.56f);
            if (_bossTele) SetGlow(_bossSeamMat, PlateWarnColor, 1.6f + 1.2f * Mathf.Abs(Mathf.Sin(t * 14f)));
            else if (_bossEnraged) SetGlow(_bossSeamMat, EnragedColor, 1.5f + 0.6f * Mathf.Sin(t * 6f));
            else SetGlow(_bossSeamMat, calm, 1.4f + 0.3f * Mathf.Sin(t * 2f));
            SetGlow(_bossThrustMat, _bossEnraged ? EnragedColor : calm, 1.4f + 0.4f * Mathf.Sin(t * 7f));
            SetGlow(_plateWarnMat, PlateWarnColor, 1.2f + 1.3f * Mathf.Abs(Mathf.Sin(t * 10f)));

            DrawList(_bossBodies, _bossMat);
            DrawList(_bossTelegraph, _telegraphMat);
            DrawList(_bossArmor, _bossArmorMat);
            DrawList(_bossSeams, _bossSeamMat);
            DrawList(_bossThrust, _bossThrustMat);
            DrawList(_platesShut, _plateShutMat);
            DrawList(_platesWarn, _plateWarnMat);
            DrawList(_platesOpen, _plateOpenMat);
            DrawList(_bossColumn, _raidBeamMat);
            foreach (var l in new[] { _bossColumn, _bossBodies, _bossTelegraph, _bossArmor, _bossSeams, _bossThrust, _platesShut, _platesWarn, _platesOpen }) l.Clear();
        }

        private static readonly GateSettings GateLook = new GateSettings();

        private string ColossusHint()
        {
            if (!_colossusSeen) return "<color=#B8B0D8>dodge its bolts, jump its shockwave</color>";
            if (_colossusExposed) return "<color=#FFD84D><b>STUNNED: shoot now!</b> full damage, core is a weak point</color>";
            if (_colossusWarn) return "<color=#FF6040><b>SLAM: JUMP the shockwave!</b></color>";
            return "<color=#B8B0D8>armored (1/5 damage): sidestep its volleys, wait for the slam</color>";
        }

        /// <summary>The Colossus's line on the HUD: where it is and its health.</summary>
        private void WriteWorldBossHud(ExpeditionStatus s, System.Text.StringBuilder e)
        {
            if (!s.WorldBoss || s.InRaid) return;
            var delta = ExpeditionWorld.Near(new float3(s.WorldBossAt.x, 0f, s.WorldBossAt.y), _camRef) - _camRef;
            float distance = math.length(delta.xz);
            float bearing = math.degrees(math.atan2(delta.x, delta.z));
            int bars = Mathf.RoundToInt(math.saturate(s.WorldBossHealth) * 20f);
            e.Append($"<color=#FF9A40><b>COLOSSUS</b></color> {distance:0} m {Direction(Mathf.DeltaAngle(ArenaLocalInputSystem.LocalLook.x, bearing))}  " +
                     $"[{new string('|', bars)}{new string('.', 20 - bars)}] {s.WorldBossHealth * 100f:0}%  {ColossusHint()}\n");
        }

        /// <summary>Raid lines for the expedition HUD: the boss bar inside, the countdown, the key near the door.</summary>
        private void WriteRaidHud(ExpeditionStatus s, System.Text.StringBuilder e)
        {
            int t = (int)s.RaidTimer;
            if (s.InRaid)
            {
                if (s.RaidStageCount > 0) WriteRoomHud(s, e);
                if (_bossSeen && _twinMax > 0f && s.RaidRoom == RaidRoomKind.Boss && s.RaidBoss == RaidBossKind.Twins)
                {
                    for (int twin = 0; twin < 2; twin++)
                    {
                        bool down = (s.RaidRoomData & (1 << twin)) != 0;
                        float f = down ? 0f : math.saturate(_twinHealth[twin] / _twinMax);
                        int bars = Mathf.RoundToInt(f * 16f);
                        string state = down ? $"<color=#FFD84D><b>DOWN</b>: kill the other in {Mathf.CeilToInt(s.RaidRoomTimer)}s!</color>"
                            : _twinOpen[twin] ? "<color=#FFD84D>PLATE OPEN</color>" : "";
                        e.Append($"<color=#D9B3FF><b>{(twin == 0 ? "WEST" : "EAST")} TWIN</b></color> [{new string('|', bars)}{new string('.', 16 - bars)}] {f * 100f:0}%  {state}\n");
                    }
                }
                else if (_bossSeen && _bossMax > 0f && s.RaidRoom == RaidRoomKind.Boss)
                {
                    float f = math.saturate(_bossHealth / _bossMax);
                    int bars = Mathf.RoundToInt(f * 24f);
                    string plate = _bossOpen ? "<color=#FFD84D>PLATE OPEN: shoot it!</color>"
                        : _bossWarn ? "<color=#FF9A40>plate opening...</color>" : "<color=#B8B0D8>plate shut (hits do 1/5)</color>";
                    string bossName = s.RaidBoss == RaidBossKind.Weaver ? "WEAVER" : "BULWARK";
                    e.Append($"<color=#D9B3FF><b>{bossName}</b></color>  [{new string('|', bars)}{new string('.', 24 - bars)}]  {f * 100f:0}%   {plate}\n");
                }
                if (s.RaidStageCount == 0) e.Append($"<color=#D9B3FF>Raid time left {t / 60}:{t % 60:00}</color>\n");
                return;
            }
            var door = RaidArena.Door;
            var delta = ExpeditionWorld.Near(ExpeditionWorld.ToFloat3(door), _camRef) - _camRef;
            float distance = math.length(delta.xz);
            float bearing = math.degrees(math.atan2(delta.x, delta.z));
            string where = $"{distance:0} m {Direction(Mathf.DeltaAngle(ArenaLocalInputSystem.LocalLook.x, bearing))}";
            switch (s.RaidPhase)
            {
                case RaidPhase.Collecting:
                    e.Append($"<color=#D9B3FF>Raid door</color> {where}: key <color=#FF6B5B>Red {s.KeyRed}/{RaidRules.RedCost}</color> " +
                             $"<color=#7BE07B>Green {s.KeyGreen}/{RaidRules.GreenCost}</color>\n");
                    break;
                case RaidPhase.Gathering:
                    e.Append($"<color=#D9B3FF><b>RAID DOOR OPEN</b></color> {where}: stand at it, going in in {t}s\n");
                    break;
                case RaidPhase.Running:
                    e.Append($"<color=#B8B0D8>A group is in the raid ({t / 60}:{t % 60:00} left)</color>\n");
                    break;
                default:
                    e.Append($"<color=#B8B0D8>Raid door resets in {t}s</color>\n");
                    break;
            }
        }

        private string RaidAnnouncement(ArenaAnnounce m)
        {
            var room = RoomAnnouncement(m);
            if (room != null) return room;
            var strange = StrangeAnnouncement(m);
            if (strange != null) return TryGetStatus(out var st) && st.InRaid ? strange : "";
            switch (m.Kind)
            {
                case AnnounceKind.RaidGathering:
                    return $"<color=#D9B3FF>RAID KEY FULL! Stand at the raid door: going in in {m.Value}s</color>\n";
                case AnnounceKind.RaidStarted:
                    return $"<color=#D9B3FF>{m.Value} player{(m.Value == 1 ? "" : "s")} went into the raid</color>\n";
                case AnnounceKind.RaidCleared:
                    return $"<color=#FFD84D>RAID CLEARED in {m.Value / 60}:{m.Value % 60:00}! +{RaidRules.ClearXp} XP, +{RaidRules.ClearYellow} Yellow</color>\n";
                case AnnounceKind.RaidFailed:
                    return (RaidOutcome)m.Value == RaidOutcome.TimedOut
                        ? "<color=#FF8080>The raid ran out of time</color>\n"
                        : "<color=#FF8080>The raid group was wiped</color>\n";
                case AnnounceKind.RaidAdds:
                    return "<color=#FF9A40>The boss calls for help: adds incoming!</color>\n";
                case AnnounceKind.RaidPledged:
                    return $"{PlayerColors.Tag(m.PlayerNetworkId)} pledged {m.Value} to the raid key\n";
                case AnnounceKind.GateUnlocked:
                    return $"<color=#FFE14D>{PlayerColors.Tag(m.PlayerNetworkId)} opened the gate ({m.Value} Yellow)! It shuts in {(int)GateLook.OpenSeconds}s</color>\n";
                case AnnounceKind.ExpeditionComplete:
                    return $"<color=#FFE14D>{PlayerColors.Tag(m.PlayerNetworkId)} went through the gate in {m.Value / 60}:{m.Value % 60:00}</color>\n";
                case AnnounceKind.GateClosed:
                    return $"<color=#FFB040>The gate shut. Opening it again costs {m.Value} Yellow</color>\n";
                case AnnounceKind.WorldBossLanded:
                {
                    var landmarks = ExpeditionWorld.Map.Landmarks;
                    string where = m.Value >= 0 && m.Value < landmarks.Count ? $" near the {landmarks[m.Value].Name}" : "";
                    return $"<color=#FF9A40><b>WORLD BOSS!</b> The Colossus landed{where}. Everyone's welcome: Yellow, Red and Green for all who hit it</color>\n";
                }
                case AnnounceKind.WorldBossDown:
                    return $"<color=#FFD84D>THE COLOSSUS FELL! +{WorldBossLook.EssenceEach} Yellow, Red and Green and an item for each of the {m.Value} who hit it</color>\n";
                default:
                    return null;
            }
        }
    }
}
