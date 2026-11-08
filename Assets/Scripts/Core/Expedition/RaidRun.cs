using System;
using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for a raid run's stages (Docs/RaidDevPlan.md).</summary>
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
        /// <summary>Low Gravity: players' gravity (jumps go about twice as high and hang longer).</summary>
        public float LowGravity { get; set; } = 0.5f;
    }

    /// <summary>
    /// One raid run: <see cref="RaidRunSettings.RoomCount"/> different rooms picked at random from the
    /// pool, then a boss (the Bulwark, the Twins or the Weaver), plus one run modifier. Each room gets its own layout
    /// seed (<see cref="RoomSeed"/>), so a room comes back in a new shape. The current room's rules live in
    /// the matching property (<see cref="Locks"/>, <see cref="Plates"/>, <see cref="Gauntlet"/>,
    /// <see cref="Storm"/>, <see cref="Runes"/>, <see cref="Core"/>, <see cref="Lanterns"/>, <see cref="Eye"/>); the caller feeds them and calls
    /// <see cref="MarkSolved"/>. A solved room opens a portal; once someone steps in,
    /// <see cref="StepPortal"/> counts down and then says to move everyone to the next stage.
    /// </summary>
    public sealed class RaidRun
    {
        private readonly List<RaidRoomKind> _rooms = new List<RaidRoomKind>();
        private readonly uint _seed;

        public RaidRun(uint seed, int players, RaidRunSettings settings = null, RaidRoomKind first = RaidRoomKind.Boss,
            RaidModifier modifier = RaidModifier.None, RaidBossKind? boss = null)
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
            Modifier = modifier != RaidModifier.None ? modifier : (RaidModifier)(1 + random.NextInt(5));
            int bossRoll = random.NextInt(3);
            Boss = boss ?? (RaidBossKind)bossRoll;
        }

        public RaidRunSettings Settings { get; }
        public int Players { get; }
        public IReadOnlyList<RaidRoomKind> Rooms => _rooms;
        public RaidModifier Modifier { get; }
        public RaidBossKind Boss { get; }
        /// <summary>0..Rooms.Count-1 are rooms; Rooms.Count is the boss.</summary>
        public int Stage { get; private set; } = -1;
        public int StageCount => _rooms.Count + 1;
        public bool AtBoss => Stage >= _rooms.Count;
        public RaidRoomKind Room => Stage < 0 || AtBoss ? RaidRoomKind.Boss : _rooms[Stage];
        /// <summary>The current room's layout seed (never 0, the plain layout).</summary>
        public uint RoomSeed { get; private set; }
        public RaidRoomLayout Layout => RaidRoomLayout.For(Room, RoomSeed);
        /// <summary>The current stage's name: the room's, or the boss's.</summary>
        public string StageName => AtBoss ? RaidRoomLayout.NameOf(Boss) : RaidRoomLayout.NameOf(Room);
        public double StageStartedAt { get; private set; }
        /// <summary>The current room is solved and its portal is open.</summary>
        public bool Solved { get; private set; }
        /// <summary>The boss is beaten: the boss stage's exit portal is open (each raider leaves on their own).</summary>
        public bool Won { get; private set; }
        /// <summary>When the portal moves everyone on (negative until someone steps in).</summary>
        public double PortalAt { get; private set; } = -1;

        public ColorLocks Locks { get; private set; }
        public HoldPlates Plates { get; private set; }
        public GauntletRun Gauntlet { get; private set; }
        public WispStorm Storm { get; private set; }
        public EchoRunes Runes { get; private set; }
        public HoldCore Core { get; private set; }
        public Lanterns Lanterns { get; private set; }
        public SentinelEye Eye { get; private set; }
        /// <summary>The boss stage with the Twins: their shared rule.</summary>
        public RaidTwins Twins { get; private set; }
        /// <summary>The boss stage with the Weaver: its floor and threads.</summary>
        public RaidWeaver Weaver { get; private set; }

        public bool Swarming => Modifier == RaidModifier.Swarming;
        public int AddsMultiplier => Swarming ? 2 : 1;
        public float DamageTakenMultiplier => Modifier == RaidModifier.Brittle ? Settings.BrittleDamage : 1f;
        public float BoltSpeedMultiplier => Modifier == RaidModifier.SwiftBolts ? Settings.SwiftBoltSpeed : 1f;
        /// <summary>Players' gravity inside the run (Low Gravity lowers it).</summary>
        public float GravityMultiplier => Modifier == RaidModifier.LowGravity ? Settings.LowGravity : 1f;
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
                    case RaidRoomKind.EchoRunes: return Runes.Progress;
                    case RaidRoomKind.HoldCore: return Core.Progress(_now);
                    case RaidRoomKind.Lanterns: return Lanterns.Progress;
                    case RaidRoomKind.SentinelEye: return Eye.Progress;
                    default: return 0f;
                }
            }
        }

        private double _now;

        /// <summary>Tells the run the time, for <see cref="Progress"/> (Hold the Core runs on the clock).</summary>
        public void Tick(double now) => _now = now;

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
            Runes = null;
            Core = null;
            Lanterns = null;
            Eye = null;
            Twins = null;
            Weaver = null;
            _now = now;
            uint roomSeed = _seed * 2654435761u + (uint)stage * 40503u;
            RoomSeed = (roomSeed ^ 0x9E3779B9u) | 1u;
            if (AtBoss && Boss == RaidBossKind.Twins) Twins = new RaidTwins();
            if (AtBoss && Boss == RaidBossKind.Weaver) Weaver = new RaidWeaver(RoomSeed, Players, now);
            switch (Room)
            {
                case RaidRoomKind.ColorLocks: Locks = new ColorLocks(roomSeed, Players); break;
                case RaidRoomKind.HoldPlates: Plates = new HoldPlates(Players); break;
                case RaidRoomKind.Gauntlet: Gauntlet = new GauntletRun(Layout); break;
                case RaidRoomKind.WispStorm: Storm = new WispStorm(Players, Swarming, now); break;
                case RaidRoomKind.EchoRunes: Runes = new EchoRunes(roomSeed, Players, now); break;
                case RaidRoomKind.HoldCore: Core = new HoldCore(Players, Swarming, now); break;
                case RaidRoomKind.Lanterns: Lanterns = new Lanterns(Players); break;
                case RaidRoomKind.SentinelEye: Eye = new SentinelEye(roomSeed, Players, now); break;
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

        /// <summary>The boss is beaten: its fight stops (no threads, no falling floor) and the exit portal opens. True the first time.</summary>
        public bool MarkWon()
        {
            if (Won || !AtBoss) return false;
            Won = true;
            Solved = true;
            PortalAt = -1;
            Twins = null;
            Weaver = null;
            return true;
        }

        /// <summary>
        /// The portal: the countdown starts when someone up is in it. Returns true when it ends; the
        /// caller then moves everyone and calls <see cref="Advance"/>.
        /// </summary>
        public bool StepPortal(double now, bool someoneInPortal)
        {
            if (!Solved || Won) return false;
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
