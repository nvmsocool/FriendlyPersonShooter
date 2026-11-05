using System;
using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for a raid run's stages (Docs/RaidDevPlan.md, phase 1).</summary>
    public sealed class RaidRunSettings
    {
        public int RoomCount { get; set; } = 3;
        /// <summary>Once someone steps into an open portal: seconds until everyone moves on.</summary>
        public double PortalSeconds { get; set; } = 5.0;
        /// <summary>Paid to everyone inside when a room is solved.</summary>
        public int RoomXp { get; set; } = 75;
        public int RoomYellow { get; set; } = 3;
        /// <summary>Brittle: damage players take, and room pay.</summary>
        public float BrittleDamage { get; set; } = 1.3f;
        public float BrittlePay { get; set; } = 1.5f;
        /// <summary>Swift Bolts: enemy bolt speed.</summary>
        public float SwiftBoltSpeed { get; set; } = 1.4f;
    }

    /// <summary>
    /// One raid run: <see cref="RaidRunSettings.RoomCount"/> different rooms picked at random from the
    /// pool, then the boss, plus one run modifier. The current room's rules live in the matching property
    /// (<see cref="Locks"/>, <see cref="Plates"/>, <see cref="Gauntlet"/>, <see cref="Storm"/>); the caller
    /// feeds them and calls <see cref="MarkSolved"/>. A solved room opens a portal; once someone steps in,
    /// <see cref="StepPortal"/> counts down and then says to move everyone to the next stage.
    /// </summary>
    public sealed class RaidRun
    {
        private readonly List<RaidRoomKind> _rooms = new List<RaidRoomKind>();
        private readonly uint _seed;

        public RaidRun(uint seed, int players, RaidRunSettings settings = null, RaidRoomKind first = RaidRoomKind.Boss,
            RaidModifier modifier = RaidModifier.None)
        {
            Settings = settings ?? new RaidRunSettings();
            Players = Math.Max(1, players);
            _seed = seed;
            var random = new CombatRandom(seed | 1u);
            var pool = new List<RaidRoomKind>(RaidRoomLayout.Pool);
            if (first != RaidRoomKind.Boss && pool.Remove(first)) _rooms.Add(first);
            while (_rooms.Count < Settings.RoomCount && pool.Count > 0)
            {
                int i = random.NextInt(pool.Count);
                _rooms.Add(pool[i]);
                pool.RemoveAt(i);
            }
            Modifier = modifier != RaidModifier.None ? modifier : (RaidModifier)(1 + random.NextInt(3));
        }

        public RaidRunSettings Settings { get; }
        public int Players { get; }
        public IReadOnlyList<RaidRoomKind> Rooms => _rooms;
        public RaidModifier Modifier { get; }
        /// <summary>0..Rooms.Count-1 are rooms; Rooms.Count is the boss.</summary>
        public int Stage { get; private set; } = -1;
        public int StageCount => _rooms.Count + 1;
        public bool AtBoss => Stage >= _rooms.Count;
        public RaidRoomKind Room => Stage < 0 || AtBoss ? RaidRoomKind.Boss : _rooms[Stage];
        public RaidRoomLayout Layout => RaidRoomLayout.For(Room);
        public double StageStartedAt { get; private set; }
        /// <summary>The current room is solved and its portal is open.</summary>
        public bool Solved { get; private set; }
        /// <summary>When the portal moves everyone on (negative until someone steps in).</summary>
        public double PortalAt { get; private set; } = -1;

        public ColorLocks Locks { get; private set; }
        public HoldPlates Plates { get; private set; }
        public GauntletRun Gauntlet { get; private set; }
        public WispStorm Storm { get; private set; }

        public bool Swarming => Modifier == RaidModifier.Swarming;
        public int AddsMultiplier => Swarming ? 2 : 1;
        public float DamageTakenMultiplier => Modifier == RaidModifier.Brittle ? Settings.BrittleDamage : 1f;
        public float BoltSpeedMultiplier => Modifier == RaidModifier.SwiftBolts ? Settings.SwiftBoltSpeed : 1f;
        public int RoomYellow => Modifier == RaidModifier.Brittle ? (int)Math.Round(Settings.RoomYellow * Settings.BrittlePay) : Settings.RoomYellow;
        public int RoomXp => Modifier == RaidModifier.Brittle ? (int)Math.Round(Settings.RoomXp * Settings.BrittlePay) : Settings.RoomXp;

        /// <summary>How the current room stands, 0..1, for the HUD.</summary>
        public float Progress
        {
            get
            {
                if (Solved) return 1f;
                switch (Room)
                {
                    case RaidRoomKind.ColorLocks: return Locks.Progress;
                    case RaidRoomKind.HoldPlates: return Plates.Progress;
                    case RaidRoomKind.Gauntlet: return Gauntlet.Progress;
                    case RaidRoomKind.WispStorm: return Storm.Progress;
                    default: return 0f;
                }
            }
        }

        /// <summary>Enters the first stage.</summary>
        public void Begin(double now) => Enter(0, now);

        private void Enter(int stage, double now)
        {
            Stage = stage;
            StageStartedAt = now;
            Solved = false;
            PortalAt = -1;
            Locks = null;
            Plates = null;
            Gauntlet = null;
            Storm = null;
            uint roomSeed = _seed * 2654435761u + (uint)stage * 40503u;
            switch (Room)
            {
                case RaidRoomKind.ColorLocks: Locks = new ColorLocks(roomSeed, Players); break;
                case RaidRoomKind.HoldPlates: Plates = new HoldPlates(Players); break;
                case RaidRoomKind.Gauntlet: Gauntlet = new GauntletRun(Layout); break;
                case RaidRoomKind.WispStorm: Storm = new WispStorm(Players, Swarming, now); break;
            }
        }

        /// <summary>Seconds into the current stage (gauntlet sweepers turn on this clock).</summary>
        public double StageTime(double now) => Math.Max(0, now - StageStartedAt);

        /// <summary>The room's rules say it's done: open the portal. True the first time.</summary>
        public bool MarkSolved()
        {
            if (Solved || AtBoss || Stage < 0) return false;
            Solved = true;
            return true;
        }

        /// <summary>
        /// The portal: the countdown starts when someone up is in it. Returns true when it ends; the
        /// caller then moves everyone and calls <see cref="Advance"/>.
        /// </summary>
        public bool StepPortal(double now, bool someoneInPortal)
        {
            if (!Solved) return false;
            if (PortalAt < 0)
            {
                if (!someoneInPortal) return false;
                PortalAt = now + Settings.PortalSeconds;
            }
            return now >= PortalAt;
        }

        public double PortalLeft(double now) => PortalAt < 0 ? -1 : Math.Max(0, PortalAt - now);

        /// <summary>On to the next stage (the boss after the last room).</summary>
        public void Advance(double now)
        {
            if (AtBoss) return;
            Enter(Stage + 1, now);
        }
    }
}
