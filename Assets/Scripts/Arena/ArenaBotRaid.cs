using FriendlyPersonShooter.Core.Expedition;
using Unity.Mathematics;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// How a bot plays the raid run's rooms (Docs/RaidDevPlan.md), from what its HUD status shows:
    /// shoot the next crystal unless it lit the last one, hold its plate, hop the gauntlet stones
    /// (jumping sweepers that are about to reach it), watch its wall's runes and shoot them back, guard
    /// the core, and walk into an open portal. Layouts come from the status's room seed.
    /// </summary>
    public partial class ArenaBotSystem
    {
        private int _gauntletStone;
        /// <summary>Mid-air from a sweeper hop (not a gap jump): stay over the stone instead of heading on.</summary>
        private bool _hopping;
        private float3 _hopFrom;
        private int _lockSeen = -1;
        private double _lockSeenAt;
        private float _clockSeen = -1f, _clockBase;
        private double _clockAt;
        /// <summary>Echo Runes: the sequence this bot watched its wall flash, and what it last saw.</summary>
        private readonly System.Collections.Generic.List<int> _runeSeq = new System.Collections.Generic.List<int>();
        private int _runeLitSeen = -1;
        private bool _runeShowSeen;
        private int _runeIndexSeen = -1;
        private double _runeIndexAt;

        private static bool InRoom(in ExpeditionStatus s, RaidRoomKind kind) => s.InRaid && s.RaidStageCount > 0 && s.RaidRoom == kind;

        /// <summary>
        /// Color Locks: aim and fire at the crystal that comes next. Waits a moment after each change (a
        /// little longer for later seats), so bots don't race each other or the humans.
        /// </summary>
        private bool CrystalAim(float dt, float3 eye, in ExpeditionStatus s, ref ArenaInput input)
        {
            if (InRoom(s, RaidRoomKind.EchoRunes)) return RuneAim(dt, eye, s, ref input);
            if (!InRoom(s, RaidRoomKind.ColorLocks) || s.RaidPortal > -1.5f || s.RaidYouHitLast) return false;
            int packed = s.RaidRoomData;
            if (ColorLocks.LengthOf(packed) == 0) return false;
            double now = World.Time.ElapsedTime;
            if (packed != _lockSeen)
            {
                _lockSeen = packed;
                _lockSeenAt = now;
            }
            if (now - _lockSeenAt < 0.5 + 0.35 * (_seat % 4)) return false;
            int next = ColorLocks.CrystalOf(packed, ColorLocks.IndexOf(packed));
            var layout = RaidRoomLayout.For(RaidRoomKind.ColorLocks, s.RaidRoomSeed);
            var p = RaidArena.FromRoom(RaidRoomKind.ColorLocks, layout.Targets[next]);
            var aim = _aim.Step(dt, ToVec(eye), -1000 - next, ToVec(p), RaidRoomLayout.CrystalRadius * 0.8f);
            input.Yaw = aim.Yaw;
            input.Pitch = aim.Pitch;
            bool clear = ArenaLayout.RaycastCover(eye, math.normalize(p - eye), math.distance(eye, p) - RaidRoomLayout.CrystalRadius) == float.MaxValue;
            input.Fire = aim.Fire && clear ? 1 : 0;
            return true;
        }

        /// <summary>The Echo Runes wall a bot looks after: even seats west, odd seats east.</summary>
        private int RuneSet => _seat % 2;

        /// <summary>
        /// Echo Runes: watch this bot's wall during its show and note each rune as it lights; once the show
        /// is over, shoot the noted sequence back, the next rune a moment after the last one counted.
        /// </summary>
        private bool RuneAim(float dt, float3 eye, in ExpeditionStatus s, ref ArenaInput input)
        {
            if (s.RaidPortal > -1.5f) return false;
            int packed = s.RaidRoomData;
            int set = RuneSet;
            int length = EchoRunes.LengthOf(packed);
            bool showing = EchoRunes.ShowingOf(packed, set);
            if (showing && !_runeShowSeen) _runeSeq.Clear();
            _runeShowSeen = showing;
            int lit = EchoRunes.LitOf(packed, set);
            if (showing && lit >= 0 && lit != _runeLitSeen) _runeSeq.Add(lit);
            _runeLitSeen = lit;
            if (showing || EchoRunes.DoneOf(packed, set) || length == 0 || _runeSeq.Count != length) return false;

            int index = EchoRunes.IndexOf(packed, set);
            double now = World.Time.ElapsedTime;
            if (index != _runeIndexSeen)
            {
                _runeIndexSeen = index;
                _runeIndexAt = now;
            }
            if (index >= length || now - _runeIndexAt < 0.45 + 0.3 * ((_seat / 2) % 3)) return false;
            int rune = _runeSeq[index];
            var layout = RaidRoomLayout.For(RaidRoomKind.EchoRunes, s.RaidRoomSeed);
            var p = RaidArena.FromRoom(RaidRoomKind.EchoRunes, layout.Targets[set * EchoRunes.RunesPerSet + rune]);
            var aim = _aim.Step(dt, ToVec(eye), -2000 - rune - 10 * set, ToVec(p), RaidRoomLayout.RuneRadius * 0.7f);
            input.Yaw = aim.Yaw;
            input.Pitch = aim.Pitch;
            bool clear = ArenaLayout.RaycastCover(eye, math.normalize(p - eye), math.distance(eye, p) - RaidRoomLayout.RuneRadius) == float.MaxValue;
            input.Fire = aim.Fire && clear ? 1 : 0;
            return true;
        }

        /// <summary>
        /// Where a room wants the bot. <paramref name="straight"/>: walk straight there (no dodging), for
        /// the gauntlet and the portal. <paramref name="jump"/>: jump now (a gap or a sweeper).
        /// </summary>
        private bool RoomAnchor(float3 pos, bool grounded, in ExpeditionStatus s, ref float3 anchor, out bool straight, out bool jump)
        {
            straight = false;
            jump = false;
            if (!s.InRaid || s.RaidStageCount == 0 || s.RaidRoom == RaidRoomKind.Boss) return false;
            var room = s.RaidRoom;
            var layout = RaidRoomLayout.For(room, s.RaidRoomSeed);
            if (s.RaidPortal > -1.5f)
            {
                anchor = RaidArena.FromRoom(room, layout.Portal);
                straight = true;
                return true;
            }
            switch (room)
            {
                case RaidRoomKind.HoldPlates:
                {
                    int plates = math.max(1, s.RaidRoomData & 15);
                    anchor = RaidArena.FromRoom(room, layout.Targets[_seat % plates]);
                    return true;
                }
                case RaidRoomKind.ColorLocks:
                {
                    float a = _seat * 1.3f;
                    anchor = RaidArena.FromRoom(room, new System.Numerics.Vector3(math.sin(a) * 5f, 0f, math.cos(a) * 5f));
                    return true;
                }
                case RaidRoomKind.Gauntlet:
                    anchor = GauntletAnchor(pos, grounded, s, layout, out jump);
                    straight = true;
                    return true;
                case RaidRoomKind.EchoRunes:
                {
                    // In front of this bot's wall, in its half of the room.
                    var rune = layout.Targets[RuneSet * EchoRunes.RunesPerSet];
                    float x = rune.X < 0f ? -11f - (_seat / 2) % 3 * 2f : 11f + (_seat / 2) % 3 * 2f;
                    anchor = RaidArena.FromRoom(room, new System.Numerics.Vector3(x, 0f, rune.Z + ((_seat / 2) % 2 == 0 ? -2f : 2f)));
                    return true;
                }
                case RaidRoomKind.HoldCore:
                {
                    // Around the core, spread out, to shoot the runners coming in.
                    float a = _seat * 2.4f;
                    anchor = RaidArena.FromRoom(room, new System.Numerics.Vector3(math.sin(a) * 5.5f, 0f, math.cos(a) * 5.5f));
                    return true;
                }
                default:
                    return false;
            }
        }

        private float3 GauntletAnchor(float3 pos, bool grounded, in ExpeditionStatus s, RaidRoomLayout layout, out bool jump)
        {
            jump = false;
            var local = RaidArena.ToRoom(RaidRoomKind.Gauntlet, pos);
            int on = layout.StoneUnder(local, 0.05f);
            if (on >= 0 && grounded) _gauntletStone = on;
            int last = layout.Path.Count - 1;
            if (_gauntletStone >= last)
            {
                var end = layout.Path[last];
                float a = _seat * 1.7f;
                return RaidArena.FromRoom(RaidRoomKind.Gauntlet, new System.Numerics.Vector3(end.Center.X + math.sin(a) * 2.5f, end.Top, end.Center.Z + math.cos(a) * 2.5f));
            }
            var next = layout.Path[_gauntletStone + 1];
            if (grounded) _hopping = false;
            if (_hopping) return _hopFrom;
            if (grounded && on == _gauntletStone)
            {
                // A sweeper about to reach us: hop it in place (heading on mid-hop would land in the gap).
                // Right by a pivot the arm always clips you, so there it's better to walk off than to hop.
                float clock = Clock(s);
                foreach (var sw in layout.Sweepers)
                {
                    float px = local.X - sw.Pivot.X, pz = local.Z - sw.Pivot.Z;
                    if (px * px + pz * pz < 1.1f * 1.1f) continue;
                    if (!GauntletRun.Swept(sw, local, clock, clock + 0.3)) continue;
                    jump = true;
                    _hopping = true;
                    _hopFrom = pos;
                    return pos;
                }
                // Close enough to the next stone to make the jump.
                float dx = math.max(math.abs(local.X - next.Center.X) - next.Size.X * 0.5f, 0f);
                float dz = math.max(math.abs(local.Z - next.Center.Z) - next.Size.Z * 0.5f, 0f);
                if (math.sqrt(dx * dx + dz * dz) < 2.9f) jump = true;
            }
            // A sweeper turns around its stone's centre and always clips whoever stands there: aim beside it.
            float side = 0f;
            foreach (var sw in layout.Sweepers)
                if (math.abs(sw.Pivot.X - next.Center.X) < 0.1f && math.abs(sw.Pivot.Z - next.Center.Z) < 0.1f) side = 1.3f;
            return RaidArena.FromRoom(RaidRoomKind.Gauntlet, new System.Numerics.Vector3(next.Center.X + side, next.Top, next.Center.Z));
        }

        /// <summary>The room clock from the status, run on between updates.</summary>
        private float Clock(in ExpeditionStatus s)
        {
            double now = World.Time.ElapsedTime;
            if (s.RaidRoomClock != _clockSeen)
            {
                _clockSeen = s.RaidRoomClock;
                _clockBase = s.RaidRoomClock;
                _clockAt = now;
            }
            return _clockBase + (float)(now - _clockAt);
        }
    }
}
