using System.Collections.Generic;
using System.Text;
using FriendlyPersonShooter.Core.Expedition;
using Unity.Mathematics;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The raid run's rooms in the view (Docs/RaidDevPlan.md): each room's platform in this run's shape
    /// (its layout seed), its puzzle pieces in their live state (crystals and the altar's order, plates,
    /// sweepers, runes, the core and its gates), the portal, and the room lines of the HUD. Room state
    /// comes from the local player's ExpeditionStatus, so only the group inside sees it live; anyone else
    /// sees the rooms at rest.
    /// </summary>
    public partial class ArenaView
    {
        private static readonly Color CrystalYellow = new Color(1f, 0.85f, 0.25f);
        private static readonly Color[] CrystalColors = { CrystalYellow, KeyRedColor, KeyGreenColor };
        private static readonly string[] CrystalNames = { "YELLOW", "RED", "GREEN" };
        private static readonly string[] CrystalHex = { "#FFD84D", "#FF6B5B", "#7BE07B" };

        private Material[] _crystalMats, _crystalDimMats;
        private Material _plateHeldMat, _plateEmptyMat, _plateOffMat, _sweeperMat, _portalMat, _stoneTopMat;
        /// <summary>Echo Runes: each set's color bright (lit in the show) and dim, and gold once a set is done.</summary>
        private Material[] _echoBrightMats, _echoDimMats;
        private Material _echoDoneMat, _coreCrystalMat, _coreHurtMat, _coreGateMat;
        private static readonly Color[] EchoColors = { new Color(0.35f, 0.85f, 1f), new Color(1f, 0.45f, 0.9f) };
        private static readonly string[] EchoHex = { "#5AD8FF", "#FF73E6" };
        private static readonly string[] EchoSide = { "West wall", "East wall" };
        private readonly List<Matrix4x4>[] _qEchoBright = { new List<Matrix4x4>(), new List<Matrix4x4>() };
        private readonly List<Matrix4x4>[] _qEchoDim = { new List<Matrix4x4>(), new List<Matrix4x4>() };
        private readonly List<Matrix4x4> _qEchoDone = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qCore = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qCoreHurt = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qGate = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qFloor = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qTileA = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qTileB = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qWall = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qRock = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qGlow = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qStoneTop = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qPlateHeld = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qPlateEmpty = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qPlateOff = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qSweeper = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _qPortal = new List<Matrix4x4>();
        private readonly List<Matrix4x4>[] _qCrystal = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        private readonly List<Matrix4x4>[] _qCrystalDim = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };

        /// <summary>The room clock (gauntlet sweepers), eased between status updates.</summary>
        private float _roomClockSeen = -1f, _roomClockBase, _roomClockAt;

        private void CreateRoomMaterials()
        {
            _crystalMats = new Material[3];
            _crystalDimMats = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                _crystalMats[i] = MakeGlow(CrystalColors[i], 2.4f);
                _crystalDimMats[i] = MakeGlow(CrystalColors[i] * 0.55f, 0.35f);
            }
            _plateHeldMat = MakeGlow(PlateOpenColor, 2f);
            _plateEmptyMat = MakeGlow(KeyRedColor, 1.2f);
            _plateOffMat = MakeMat(new Color(0.22f, 0.2f, 0.27f));
            _sweeperMat = MakeGlow(PlateWarnColor, 2.2f);
            _portalMat = MakeGlow(new Color(0.85f, 0.6f, 1f), 2.4f);
            _stoneTopMat = MakeMat(new Color(0.36f, 0.32f, 0.44f));
            _echoBrightMats = new Material[2];
            _echoDimMats = new Material[2];
            for (int i = 0; i < 2; i++)
            {
                _echoBrightMats[i] = MakeGlow(EchoColors[i], 3f);
                _echoDimMats[i] = MakeGlow(EchoColors[i] * 0.45f, 0.3f);
            }
            _echoDoneMat = MakeGlow(PlateOpenColor, 1.6f);
            _coreCrystalMat = MakeGlow(new Color(0.45f, 1f, 0.75f), 2.6f);
            _coreHurtMat = MakeGlow(KeyRedColor, 2.6f);
            _coreGateMat = MakeGlow(new Color(1f, 0.55f, 0.25f), 1.8f);
        }

        /// <summary>Room platforms near the camera (they hang far above the ground, hidden in the fog).</summary>
        private void DrawRooms(ExpeditionStatus s)
        {
            if (_crystalMats == null) CreateRoomMaterials();
            foreach (var kind in RaidRoomLayout.Pool)
            {
                // The room the group is in takes this run's shape, colliders included (the server built
                // its own already; in a joined game this keeps the client's movement on the same ground).
                bool live = LiveIn(kind, s);
                if (live) RaidArena.UseRoom(kind, s.RaidRoomSeed);
                var d = RaidArena.ToRoom(kind, _camRef);
                if (math.abs(d.Y) < 220f && math.abs(d.X) < 150f && math.abs(d.Z) < 150f) DrawRoom(kind, s, live ? s.RaidRoomSeed : RaidArena.SeedOf(kind));
            }
        }

        private bool LiveIn(RaidRoomKind kind, ExpeditionStatus s) => s.InRaid && s.RaidStageCount > 0 && s.RaidRoom == kind;

        private float RoomClock(ExpeditionStatus s)
        {
            if (s.RaidRoomClock != _roomClockSeen)
            {
                _roomClockSeen = s.RaidRoomClock;
                _roomClockBase = s.RaidRoomClock;
                _roomClockAt = Time.time;
            }
            return _roomClockBase + (Time.time - _roomClockAt);
        }

        private void DrawRoom(RaidRoomKind kind, ExpeditionStatus s, uint seed)
        {
            foreach (var l in new[] { _qFloor, _qTileA, _qTileB, _qWall, _qRock, _qGlow, _qStoneTop, _qPlateHeld, _qPlateEmpty, _qPlateOff, _qSweeper, _qPortal,
                         _qEchoDone, _qCore, _qCoreHurt, _qGate }) l.Clear();
            for (int i = 0; i < 3; i++)
            {
                _qCrystal[i].Clear();
                _qCrystalDim[i].Clear();
            }
            for (int i = 0; i < 2; i++)
            {
                _qEchoBright[i].Clear();
                _qEchoDim[i].Clear();
            }
            var layout = RaidRoomLayout.For(kind, seed);
            var c = Near(RaidArena.RoomCenter(kind));
            var id = Quaternion.identity;
            float t = Time.time;
            bool live = LiveIn(kind, s);

            foreach (var b in layout.Boxes)
            {
                var at = c + new Vector3(b.Center.X, b.Center.Y, b.Center.Z);
                var size = new Vector3(b.Size.X, b.Size.Y, b.Size.Z);
                switch (b.Kind)
                {
                    case RaidBoxKind.Floor: Box(_qFloor, at, id, size); break;
                    case RaidBoxKind.Stone:
                        Box(_qRock, at, id, size);
                        Box(_qStoneTop, at + new Vector3(0f, size.y * 0.5f + 0.02f, 0f), id, new Vector3(size.x - 0.2f, 0.06f, size.z - 0.2f));
                        // A rock root under each stone, and a glowing rim so the edges read mid-jump.
                        Box(_qRock, at - new Vector3(0f, size.y * 0.5f + 1.2f, 0f), Quaternion.Euler(0f, 30f, 0f), new Vector3(size.x * 0.7f, 2.4f, size.z * 0.7f));
                        Box(_qGlow, at + new Vector3(0f, size.y * 0.5f - 0.05f, 0f), id, new Vector3(size.x + 0.06f, 0.08f, size.z + 0.06f));
                        break;
                    default:
                        Box(_qWall, at, id, size);
                        if (b.Kind == RaidBoxKind.Pillar) Box(_qGlow, at + new Vector3(0f, size.y * 0.5f - 0.3f, 0f), id, new Vector3(size.x + 0.08f, 0.12f, size.z + 0.08f));
                        break;
                }
            }

            if (kind != RaidRoomKind.Gauntlet)
            {
                // Checkered floor, a wall trim, and the rock root, like the boss arena.
                float hs = RaidRoomLayout.SquareHalf;
                const int n = 8;
                float ts = 2f * hs / n;
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        var pos = c + new Vector3((i + 0.5f) * ts - hs, 0.02f, (j + 0.5f) * ts - hs);
                        Box((i + j) % 2 == 0 ? _qTileA : _qTileB, pos, id, new Vector3(ts - 0.3f, 0.04f, ts - 0.3f));
                    }
                }
                float trimY = RaidRoomLayout.WallHeight - 0.3f;
                Box(_qGlow, c + new Vector3(0f, trimY, hs - 0.02f), id, new Vector3(2f * hs, 0.1f, 0.06f));
                Box(_qGlow, c + new Vector3(0f, trimY, -hs + 0.02f), id, new Vector3(2f * hs, 0.1f, 0.06f));
                Box(_qGlow, c + new Vector3(hs - 0.02f, trimY, 0f), id, new Vector3(0.06f, 0.1f, 2f * hs));
                Box(_qGlow, c + new Vector3(-hs + 0.02f, trimY, 0f), id, new Vector3(0.06f, 0.1f, 2f * hs));
                float[] widths = { 50f, 38f, 24f, 10f };
                float y0 = -2f;
                for (int k = 0; k < widths.Length; k++)
                {
                    Box(_qRock, c + new Vector3(0f, y0 - 2f, 0f), Quaternion.Euler(0f, k * 13f + (int)kind * 7f, 0f), new Vector3(widths[k], 4f, widths[k]));
                    y0 -= 3.4f;
                }
            }

            switch (kind)
            {
                case RaidRoomKind.ColorLocks: DrawLocks(layout, c, s, live, t); break;
                case RaidRoomKind.HoldPlates: DrawPlates(layout, c, s, live, t); break;
                case RaidRoomKind.Gauntlet: DrawSweepers(layout, c, live ? RoomClock(s) : t); break;
                case RaidRoomKind.EchoRunes: DrawRunes(layout, c, s, live, t); break;
                case RaidRoomKind.HoldCore: DrawCore(layout, c, s, live, t); break;
                case RaidRoomKind.SentinelEye: DrawEye(c, s, live, t); break;
            }

            // The portal: a ring standing at the far side, lit once the room is solved.
            if (live && s.RaidPortal > -1.5f)
            {
                var p = c + new Vector3(layout.Portal.X, layout.Portal.Y + 2.2f, layout.Portal.Z);
                float spin = t * (s.RaidPortal >= 0f ? 260f : 60f);
                const int segs = 16;
                for (int k = 0; k < segs; k++)
                {
                    float a = 2f * Mathf.PI * k / segs;
                    var o = new Vector3(Mathf.Sin(a) * 2f, Mathf.Cos(a) * 2f, 0f);
                    Box(_qPortal, p + o, Quaternion.Euler(0f, 0f, -a * Mathf.Rad2Deg + spin), new Vector3(0.75f, 0.22f, 0.22f));
                }
                Box(_qPortal, p, Quaternion.Euler(0f, 0f, spin), new Vector3(0.5f, 0.5f, 0.5f) * (1f + 0.3f * Mathf.Sin(t * 6f)));
                Box(_qPortal, c + new Vector3(layout.Portal.X, 40f, layout.Portal.Z), id, new Vector3(0.3f, 80f, 0.3f));
                Box(_qPortal, c + new Vector3(layout.Portal.X, layout.Portal.Y + 0.05f, layout.Portal.Z), Quaternion.Euler(0f, t * 40f, 0f),
                    new Vector3(RaidRoomLayout.PortalRadius * 1.6f, 0.05f, RaidRoomLayout.PortalRadius * 1.6f));
            }

            DrawList(_qFloor, _raidFloorMat);
            DrawList(_qTileA, _raidTileAMat);
            DrawList(_qTileB, _raidTileBMat);
            DrawList(_qWall, _raidWallMat);
            DrawList(_qRock, _raidRockMat);
            DrawList(_qGlow, _raidGlowMat);
            DrawList(_qStoneTop, _stoneTopMat);
            DrawList(_qPlateHeld, _plateHeldMat);
            DrawList(_qPlateEmpty, _plateEmptyMat);
            DrawList(_qPlateOff, _plateOffMat);
            DrawList(_qSweeper, _sweeperMat);
            DrawList(_qPortal, _portalMat);
            for (int i = 0; i < 3; i++)
            {
                DrawList(_qCrystal[i], _crystalMats[i]);
                DrawList(_qCrystalDim[i], _crystalDimMats[i]);
            }
            for (int i = 0; i < 2; i++)
            {
                DrawList(_qEchoBright[i], _echoBrightMats[i]);
                DrawList(_qEchoDim[i], _echoDimMats[i]);
            }
            DrawList(_qEchoDone, _echoDoneMat);
            DrawList(_qCore, _coreCrystalMat);
            DrawList(_qCoreHurt, _coreHurtMat);
            DrawList(_qGate, _coreGateMat);
        }

        /// <summary>
        /// Runes as glowing diamonds on their slabs, west set blue and east set pink: dim at rest, big and
        /// bright while the show lights them, gold once the set is done for the round. The sequence itself
        /// is only ever shown by the show, so watching is the puzzle.
        /// </summary>
        private void DrawRunes(RaidRoomLayout layout, Vector3 c, ExpeditionStatus s, bool live, float t)
        {
            int packed = s.RaidRoomData;
            for (int i = 0; i < layout.Targets.Count; i++)
            {
                int set = i / EchoRunes.RunesPerSet, rune = i % EchoRunes.RunesPerSet;
                var p = c + new Vector3(layout.Targets[i].X, layout.Targets[i].Y, layout.Targets[i].Z);
                bool done = live && s.RaidPortal < -1.5f && EchoRunes.DoneOf(packed, set);
                bool lit = live && EchoRunes.LitOf(packed, set) == rune;
                float size = RaidRoomLayout.RuneRadius * (lit ? 1.6f : 1.15f);
                var rot = Quaternion.Euler(0f, 90f, 45f + (lit ? t * 200f : 0f));
                var list = done ? _qEchoDone : lit ? _qEchoBright[set] : _qEchoDim[set];
                Box(list, p, rot, new Vector3(0.25f, size, size));
                if (lit) Box(_qEchoBright[set], p, Quaternion.Euler(0f, 90f, 0f), new Vector3(0.1f, size * 1.9f, 0.12f));
            }
            // A light over each slab while its set is showing, so players know to look now.
            for (int set = 0; set < EchoRunes.Sets && live; set++)
            {
                if (!EchoRunes.ShowingOf(packed, set)) continue;
                var first = layout.Targets[set * EchoRunes.RunesPerSet];
                float x = first.X < 0f ? -RaidRoomLayout.SquareHalf + 1.5f : RaidRoomLayout.SquareHalf - 1.5f;
                Box(_qEchoBright[set], c + new Vector3(x, 7.6f + 0.2f * Mathf.Sin(t * 6f), first.Z), Quaternion.identity, new Vector3(0.3f, 0.3f, 12f));
            }
        }

        /// <summary>
        /// The core: a big crystal spinning over its pillar, green when healthy and red as it cracks, with a
        /// light column that shrinks with its health. Each gate is a glowing arch with a faint lane to the core.
        /// </summary>
        private void DrawCore(RaidRoomLayout layout, Vector3 c, ExpeditionStatus s, bool live, float t)
        {
            float health = live ? HoldCore.HealthOf(s.RaidRoomData) / 100f : 1f;
            var core = c + new Vector3(layout.Targets[0].X, layout.Targets[0].Y, layout.Targets[0].Z);
            var list = health < 0.4f ? _qCoreHurt : _qCore;
            float size = 1.5f * (health < 0.4f ? 1f + 0.12f * Mathf.Sin(t * 12f) : 1f);
            Box(list, core, Quaternion.Euler(45f, t * 50f, 45f), Vector3.one * size);
            Box(list, core, Quaternion.Euler(0f, -t * 80f, 0f), new Vector3(0.5f, size * 1.8f, 0.5f));
            float h = 0.3f + 14f * health;
            Box(list, core + new Vector3(0f, 1.5f + h * 0.5f, 0f), Quaternion.identity, new Vector3(0.18f, h, 0.18f));
            for (int g = 1; g < layout.Targets.Count; g++)
            {
                var gate = layout.Targets[g];
                var at = c + new Vector3(gate.X, 0f, gate.Z);
                var inward = new Vector3(-gate.X, 0f, -gate.Z).normalized;
                var rot = Quaternion.LookRotation(inward);
                Box(_qGate, at + Vector3.up * 1.6f + rot * new Vector3(-1.6f, 0f, 0f), rot, new Vector3(0.35f, 3.2f, 0.35f));
                Box(_qGate, at + Vector3.up * 1.6f + rot * new Vector3(1.6f, 0f, 0f), rot, new Vector3(0.35f, 3.2f, 0.35f));
                Box(_qGate, at + Vector3.up * 3.3f, rot, new Vector3(3.6f, 0.35f, 0.35f));
                // The lane, faint on the floor.
                var mid = (at + core) * 0.5f;
                mid.y = c.y + 0.05f;
                float len = new Vector2(gate.X, gate.Z).magnitude;
                Box(_qEchoDone, mid, rot, new Vector3(0.12f, 0.03f, len - 2f));
            }
        }

        /// <summary>
        /// Crystals on their pillars (the next one in the order pulses) and the altar's order as a column
        /// of cubes, first at the bottom: lit ones bright, the next one spinning, the rest dim.
        /// </summary>
        private void DrawLocks(RaidRoomLayout layout, Vector3 c, ExpeditionStatus s, bool live, float t)
        {
            int packed = s.RaidRoomData;
            int length = live ? ColorLocks.LengthOf(packed) : 0;
            int index = live ? ColorLocks.IndexOf(packed) : 0;
            int next = live && s.RaidPortal < -1.5f && length > 0 ? ColorLocks.CrystalOf(packed, index) : -1;
            for (int i = 0; i < layout.Targets.Count && i < 3; i++)
            {
                var p = c + new Vector3(layout.Targets[i].X, layout.Targets[i].Y, layout.Targets[i].Z);
                float size = RaidRoomLayout.CrystalRadius * 1.25f;
                if (i == next) size *= 1f + 0.18f * Mathf.Sin(t * 8f);
                Box(next < 0 || i == next ? _qCrystal[i] : _qCrystalDim[i], p, Quaternion.Euler(45f, t * (i == next ? 120f : 30f) + i * 40f, 45f), Vector3.one * size);
                // A beam of the crystal's color, so each is easy to tell apart across the room.
                Box(_qCrystalDim[i], p + new Vector3(0f, 6f, 0f), Quaternion.identity, new Vector3(0.12f, 10f, 0.12f));
            }
            for (int k = 0; k < length; k++)
            {
                int color = ColorLocks.CrystalOf(packed, k);
                var p = c + new Vector3(0f, 2.2f + k * 1.15f, 0f);
                bool lit = k < index;
                bool isNext = k == index;
                var rot = isNext ? Quaternion.Euler(0f, t * 180f, 0f) : Quaternion.Euler(0f, 45f, 0f);
                Box(lit || isNext ? _qCrystal[color] : _qCrystalDim[color], p, rot, Vector3.one * (isNext ? 0.95f : 0.8f));
            }
        }

        /// <summary>Plates as glowing octagons: gold while held, pulsing red while empty, dark when not in play; a light column fills with the bar.</summary>
        private void DrawPlates(RaidRoomLayout layout, Vector3 c, ExpeditionStatus s, bool live, float t)
        {
            int plates = live ? s.RaidRoomData & 15 : 0;
            int held = live ? s.RaidRoomData >> 4 : 0;
            float d = RaidRoomLayout.PlateRadius * 1.8f;
            for (int i = 0; i < layout.Targets.Count; i++)
            {
                var p = c + new Vector3(layout.Targets[i].X, 0.05f, layout.Targets[i].Z);
                var list = i >= plates ? _qPlateOff : (held & (1 << i)) != 0 ? _qPlateHeld : _qPlateEmpty;
                float pulse = list == _qPlateEmpty ? 1f + 0.06f * Mathf.Sin(t * 7f) : 1f;
                Box(list, p, Quaternion.identity, new Vector3(d * pulse, 0.08f, d * pulse));
                Box(list, p, Quaternion.Euler(0f, 45f, 0f), new Vector3(d * pulse, 0.08f, d * pulse));
                if (i < plates) Box(list, p + new Vector3(0f, 3f, 0f), Quaternion.identity, new Vector3(0.15f, 6f, 0.15f));
            }
            if (live)
            {
                float h = 0.2f + 9f * math.saturate(s.RaidRoomProgress);
                Box(_qPlateHeld, c + new Vector3(0f, h * 0.5f, 0f), Quaternion.Euler(0f, t * 30f, 0f), new Vector3(1.2f, h, 1.2f));
            }
            Box(_qWall, c + new Vector3(0f, 0.1f, 0f), Quaternion.identity, new Vector3(3f, 0.2f, 3f));
        }

        /// <summary>Sweeper arms turning low over their stones, with a hub at the pivot.</summary>
        private void DrawSweepers(RaidRoomLayout layout, Vector3 c, float clock)
        {
            foreach (var sw in layout.Sweepers)
            {
                float a = sw.AngleAt(clock);
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var pivot = c + new Vector3(sw.Pivot.X, sw.Pivot.Y, sw.Pivot.Z);
                var rot = Quaternion.LookRotation(dir);
                Box(_qSweeper, pivot + dir * (sw.Length * 0.5f) + new Vector3(0f, RaidRoomLayout.SweeperHeight * 0.5f, 0f), rot,
                    new Vector3(RaidRoomLayout.SweeperWidth, RaidRoomLayout.SweeperHeight * 0.8f, sw.Length));
                Box(_qWall, pivot + new Vector3(0f, 0.35f, 0f), rot, new Vector3(0.7f, 0.7f, 0.7f));
            }
        }

        private static string Bar(float f, int width)
        {
            int bars = Mathf.RoundToInt(math.saturate(f) * width);
            return $"[{new string('|', bars)}{new string('.', width - bars)}]";
        }

        /// <summary>The HUD lines for the room you're in: stage, modifier, time, and what the room needs now.</summary>
        private void WriteRoomHud(ExpeditionStatus s, StringBuilder e)
        {
            int t = (int)s.RaidTimer;
            string mod = s.RaidModifier != RaidModifier.None ? $"  <color=#FF9A40>{RaidRoomLayout.NameOf(s.RaidModifier)}</color>" : "";
            string name = s.RaidRoom == RaidRoomKind.Boss ? RaidRoomLayout.NameOf(s.RaidBoss) : RaidRoomLayout.NameOf(s.RaidRoom);
            e.Append($"<color=#D9B3FF><b>RAID</b> {s.RaidStage + 1}/{s.RaidStageCount}: <b>{name}</b></color>{mod}" +
                     $"  <color=#D9B3FF>{t / 60}:{t % 60:00} left</color>\n");
            if (s.RaidPortal > -1.5f)
            {
                e.Append(s.RaidPortal < 0f
                    ? "<color=#FFD84D><b>PORTAL OPEN</b>: step into the ring to move on</color>\n"
                    : $"<color=#FFD84D><b>Moving on in {Mathf.CeilToInt(s.RaidPortal)}s</b></color>\n");
                return;
            }
            switch (s.RaidRoom)
            {
                case RaidRoomKind.ColorLocks:
                {
                    int packed = s.RaidRoomData;
                    int length = ColorLocks.LengthOf(packed), index = ColorLocks.IndexOf(packed);
                    e.Append($"Order (round {ColorLocks.RoundOf(packed) + 1}/{ColorLocks.Rounds}): ");
                    for (int k = 0; k < length; k++)
                    {
                        int col = ColorLocks.CrystalOf(packed, k);
                        if (k < index) e.Append($"<color=#6E6680>{CrystalNames[col]}</color> ");
                        else if (k == index) e.Append($"<color={CrystalHex[col]}><b>[{CrystalNames[col]}]</b></color> ");
                        else e.Append($"<color={CrystalHex[col]}>{CrystalNames[col]}</color> ");
                    }
                    if (s.RaidYouHitLast) e.Append(" <color=#FF9A40>you lit the last one: someone else takes the next</color>");
                    e.Append('\n');
                    break;
                }
                case RaidRoomKind.HoldPlates:
                {
                    int plates = s.RaidRoomData & 15, held = s.RaidRoomData >> 4, n = 0;
                    for (int i = 0; i < plates; i++) if ((held & (1 << i)) != 0) n++;
                    string warn = n < plates ? "  <color=#FF6B5B>a plate is empty: the bar drains</color>" : "  <color=#FFD84D>all held!</color>";
                    e.Append($"Plates held {n}/{plates}  {Bar(s.RaidRoomProgress, 20)} {s.RaidRoomProgress * 100f:0}%{warn}\n");
                    break;
                }
                case RaidRoomKind.Gauntlet:
                    e.Append($"Furthest stone {s.RaidRoomData}/{RaidRoomLayout.GauntletStones + 1}.  <color=#FF9A40>Jump the orange sweepers</color>; a fall sends you back to the start\n");
                    break;
                case RaidRoomKind.WispStorm:
                {
                    int kills = s.RaidRoomData & 255, target = s.RaidRoomData >> 8;
                    int left = (int)s.RaidRoomTimer;
                    e.Append($"Wisps {kills}/{target}  {Bar(target > 0 ? (float)kills / target : 0f, 20)}  <color={(left < 15 ? "#FF6B5B" : "#D9B3FF")}>{left}s left</color>\n");
                    break;
                }
                case RaidRoomKind.EchoRunes:
                {
                    int packed = s.RaidRoomData;
                    int length = EchoRunes.LengthOf(packed);
                    e.Append($"Runes, round {EchoRunes.RoundOf(packed) + 1}/{EchoRunes.Rounds} ({length} long): ");
                    for (int set = 0; set < EchoRunes.Sets; set++)
                    {
                        string state = EchoRunes.DoneOf(packed, set) ? "<color=#FFD84D>done</color>"
                            : EchoRunes.ShowingOf(packed, set) ? "<b>WATCH!</b>"
                            : $"{EchoRunes.IndexOf(packed, set)}/{length}";
                        e.Append($"<color={EchoHex[set]}>{EchoSide[set]}</color> {state}   ");
                    }
                    e.Append('\n');
                    break;
                }
                case RaidRoomKind.HoldCore:
                {
                    int health = HoldCore.HealthOf(s.RaidRoomData);
                    int left = (int)s.RaidRoomTimer;
                    e.Append($"Core {Bar(health / 100f, 20)} <color={(health < 40 ? "#FF6B5B" : "#7BE07B")}>{health}%</color>  hold for <b>{left}s</b>" +
                             "  <color=#FF9A40>Skitters from the orange gates</color>\n");
                    break;
                }
                default:
                    WriteStrangeHud(s, e);
                    break;
            }
        }

        private static string ModifierHint(RaidModifier m)
        {
            switch (m)
            {
                case RaidModifier.Swarming: return "twice the adds";
                case RaidModifier.SwiftBolts: return "enemy bolts fly faster";
                case RaidModifier.Brittle: return "you take more damage, rooms pay more";
                case RaidModifier.LowGravity: return "higher, floatier jumps";
                case RaidModifier.Fog: return "short sight lines";
                default: return "";
            }
        }

        /// <summary>Announcements about the run's rooms; shown only to the group inside. Null if it isn't one.</summary>
        private string RoomAnnouncement(ArenaAnnounce m)
        {
            if (m.Kind != AnnounceKind.RaidStage && m.Kind != AnnounceKind.RaidRoomSolved && m.Kind != AnnounceKind.RaidSetback &&
                m.Kind != AnnounceKind.TwinDown && m.Kind != AnnounceKind.TwinRevived) return null;
            if (!TryGetStatus(out var s) || !s.InRaid) return "";
            var room = (RaidRoomKind)m.Value;
            switch (m.Kind)
            {
                case AnnounceKind.TwinDown:
                    return $"<color=#FFD84D><b>A TWIN FELL!</b> Kill the other within {m.Value}s or it comes back</color>\n";
                case AnnounceKind.TwinRevived:
                    return "<color=#FF8080>Too slow: the fallen twin is back</color>\n";
                case AnnounceKind.RaidStage:
                {
                    string stageName = room == RaidRoomKind.Boss ? RaidRoomLayout.NameOf(s.RaidBoss) : RaidRoomLayout.NameOf(room);
                    string text = $"<color=#D9B3FF><b>{stageName.ToUpperInvariant()}</b></color>";
                    var mod = (RaidModifier)m.OtherNetworkId;
                    if (m.PlayerNetworkId == 0 && mod != RaidModifier.None)
                        text += $"  <color=#FF9A40>This run: {RaidRoomLayout.NameOf(mod)} ({ModifierHint(mod)})</color>";
                    return text + "\n";
                }
                case AnnounceKind.RaidRoomSolved:
                    return $"<color=#FFD84D>{RaidRoomLayout.NameOf(room)} solved! +{m.PlayerNetworkId} XP, +{m.OtherNetworkId} Yellow. Step into the portal</color>\n";
                default:
                    switch (m.OtherNetworkId)
                    {
                        case 0: return $"<color=#FF8080>{PlayerColors.Tag(m.PlayerNetworkId)} hit the wrong crystal: the order starts over</color>\n";
                        case 1: return "<color=#FF8080>Too slow between crystals: the order starts over</color>\n";
                        case 2: return "<color=#FF8080>The storm's timer ran out: the count starts over</color>\n";
                        case 3: return $"<color=#FF8080>{PlayerColors.Tag(m.PlayerNetworkId)} hit the wrong rune: that wall shows its runes again</color>\n";
                        case 4: return "<color=#FF8080>Too slow: the runes show again</color>\n";
                        default: return "<color=#FF8080>The core broke! It's whole again, and the clock starts over</color>\n";
                    }
            }
        }
    }
}
