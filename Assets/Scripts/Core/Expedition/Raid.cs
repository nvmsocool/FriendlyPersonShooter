using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for the raid door and its run. Essence, metres, seconds.</summary>
    public sealed class RaidSettings
    {
        /// <summary>
        /// The key: three colors, so one player rarely has the mix and groups pool. Blue, Purple and Green, so it never
        /// competes with the Yellow gate. About 6 Sentinels, 2 Bulwarks and 4 Mite swarms for one player.
        /// </summary>
        public int BlueCost { get; set; } = 12;
        public int PurpleCost { get; set; } = 8;
        public int GreenCost { get; set; } = 12;
        /// <summary>Bots stand this close to the door to pledge.</summary>
        public float UseRadius { get; set; } = 7f;
        /// <summary>Seconds from the key filling until everyone goes in.</summary>
        public float GatherSeconds { get; set; } = 20f;
        /// <summary>A run (three rooms and the boss) ends as a loss after this long.</summary>
        public float TimeLimit { get; set; } = 720f;
        /// <summary>Seconds after a run before the door takes pledges again.</summary>
        public float ResetSeconds { get; set; } = 20f;
        public int MaxPlayers { get; set; } = 8;
        /// <summary>Clear reward for everyone still in the raid: XP (one level), plus an item. Raids never pay essence.</summary>
        public int ClearXp { get; set; } = 300;
        /// <summary>
        /// After the boss dies, the raiders can stay up there this long (seconds) before the door sends whoever
        /// is left home. Each one leaves sooner through the exit portal.
        /// </summary>
        public float VictorySeconds { get; set; } = 300f;

        public int Cost(EssenceColor color) =>
            color == EssenceColor.Blue ? BlueCost : color == EssenceColor.Purple ? PurpleCost : color == EssenceColor.Green ? GreenCost : 0;

        /// <summary>All three costs together.</summary>
        public int TotalCost => BlueCost + PurpleCost + GreenCost;
    }

    public enum RaidPhase : byte
    {
        /// <summary>Taking pledges toward the key (a full key starts the gather at once).</summary>
        Collecting = 0,
        /// <summary>The key is full and the countdown is running; when it ends, the group goes in.</summary>
        Gathering = 1,
        /// <summary>A group is inside fighting the boss.</summary>
        Running = 2,
        /// <summary>A run just ended; the door reopens for pledges soon.</summary>
        Resetting = 3,
    }

    public enum RaidOutcome : byte
    {
        None = 0,
        /// <summary>The boss died.</summary>
        Cleared = 1,
        /// <summary>Everyone inside was down at once, or left.</summary>
        Wiped = 2,
        TimedOut = 3,
    }

    public enum RaidDoorEvent : byte
    {
        None = 0,
        /// <summary>The countdown ended with nobody at the door; the key stays full until someone starts it again.</summary>
        GatherCancelled,
        /// <summary>The countdown ended and <see cref="RaidDoor.Participants"/> went in.</summary>
        Started,
        /// <summary>The door takes pledges again after a run.</summary>
        Reopened,
    }

    /// <summary>
    /// A raid door. Players fill its key with Blue, Purple and Green essence by shooting its empty slots, one
    /// essence a shot (<see cref="PledgeOne"/>), carried first, then from their party bank; bots pledge by
    /// standing at it (<see cref="Pledge"/>). Anyone can fill it, party or not. A full key starts a short
    /// countdown (<see cref="Begin"/>); when it ends, the players the server lists go in together (up to 8). The run ends when the boss dies, when everyone inside is down at once, or at the time
    /// limit; then the door resets and takes pledges for a new key. One run at a time per door.
    /// </summary>
    public sealed class RaidDoor
    {
        public static readonly EssenceColor[] KeyColors = { EssenceColor.Blue, EssenceColor.Purple, EssenceColor.Green };

        private readonly EssenceAmounts _pledged = new EssenceAmounts();
        private readonly List<int> _participants = new List<int>();

        public RaidDoor(RaidSettings settings = null)
        {
            Settings = settings ?? new RaidSettings();
        }

        public RaidSettings Settings { get; }
        public RaidPhase Phase { get; private set; } = RaidPhase.Collecting;
        /// <summary>When the countdown or the reset ends.</summary>
        public double PhaseEnds { get; private set; }
        public double StartedAt { get; private set; }
        /// <summary>The players inside (while running).</summary>
        public IReadOnlyList<int> Participants => _participants;
        public RaidOutcome LastOutcome { get; private set; }
        /// <summary>The boss is dead and the raiders are celebrating up there (still <see cref="RaidPhase.Running"/>).</summary>
        public bool Won { get; private set; }
        public double WonAt { get; private set; }

        public int Pledged(EssenceColor color) => _pledged[color];
        public int Cost(EssenceColor color) => Settings.Cost(color);

        public bool KeyFull
        {
            get
            {
                foreach (var c in KeyColors) if (_pledged[c] < Settings.Cost(c)) return false;
                return true;
            }
        }

        /// <summary>The key is full and the countdown hasn't started (only while a layer move is pending).</summary>
        public bool Ready => Phase == RaidPhase.Collecting && KeyFull;

        /// <summary>With a full key, the countdown starts. Returns true if it did.</summary>
        public bool Begin(double now)
        {
            if (!Ready) return false;
            Phase = RaidPhase.Gathering;
            PhaseEnds = now + Settings.GatherSeconds;
            return true;
        }

        /// <summary>
        /// Moves what the key still needs from the wallet (carried first, then banked) into the key: a bot at the door.
        /// Only while collecting. Returns how much essence moved.
        /// </summary>
        public int Pledge(EssenceWallet wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (Phase != RaidPhase.Collecting) return 0;
            int moved = 0;
            foreach (var c in KeyColors)
            {
                int need = Settings.Cost(c) - _pledged[c];
                if (need <= 0) continue;
                int took = wallet.Take(c, need);
                _pledged.Add(c, took);
                moved += took;
            }
            return moved;
        }

        /// <summary>
        /// A player shot an empty key slot: one <paramref name="color"/> essence (carried first, then banked) goes
        /// into the key. Only while collecting, and only while that color isn't full. Returns whether it did.
        /// </summary>
        public bool PledgeOne(EssenceWallet wallet, EssenceColor color)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (Phase != RaidPhase.Collecting || Array.IndexOf(KeyColors, color) < 0) return false;
            if (_pledged[color] >= Settings.Cost(color)) return false;
            if (wallet.Take(color, 1) <= 0) return false;
            _pledged.Add(color, 1);
            return true;
        }

        /// <summary>
        /// Advances the door. <paramref name="atDoor"/> lists the players to take in when the countdown ends (after a
        /// full key, everyone on the layer who isn't already inside), in the order they should be let in.
        /// </summary>
        public RaidDoorEvent Update(double now, IReadOnlyList<int> atDoor)
        {
            switch (Phase)
            {
                case RaidPhase.Gathering:
                    if (now < PhaseEnds) return RaidDoorEvent.None;
                    if (atDoor.Count == 0)
                    {
                        Phase = RaidPhase.Collecting;
                        return RaidDoorEvent.GatherCancelled;
                    }
                    _participants.Clear();
                    for (int i = 0; i < atDoor.Count && _participants.Count < Settings.MaxPlayers; i++)
                    {
                        if (!_participants.Contains(atDoor[i])) _participants.Add(atDoor[i]);
                    }
                    _pledged.Clear();
                    Phase = RaidPhase.Running;
                    StartedAt = now;
                    LastOutcome = RaidOutcome.None;
                    Won = false;
                    return RaidDoorEvent.Started;
                case RaidPhase.Resetting:
                    if (now < PhaseEnds) return RaidDoorEvent.None;
                    Phase = RaidPhase.Collecting;
                    return RaidDoorEvent.Reopened;
                default:
                    return RaidDoorEvent.None;
            }
        }

        public bool IsInside(int playerId) => Phase == RaidPhase.Running && _participants.Contains(playerId);

        /// <summary>A player left the run (bled out, reset their position, disconnected).</summary>
        public void Leave(int playerId) => _participants.Remove(playerId);

        /// <summary>Seconds left in the run (or in the countdown while gathering).</summary>
        public double TimeLeft(double now)
        {
            if (Phase == RaidPhase.Running && Won) return Math.Max(0, WonAt + Settings.VictorySeconds - now);
            if (Phase == RaidPhase.Running) return Math.Max(0, StartedAt + Settings.TimeLimit - now);
            if (Phase == RaidPhase.Gathering || Phase == RaidPhase.Resetting) return Math.Max(0, PhaseEnds - now);
            return 0;
        }

        /// <summary>
        /// While running: how the run stands. <paramref name="participantsUp"/> is how many people inside
        /// are not down. The boss dying is reported with <see cref="Finish"/> by the caller.
        /// </summary>
        public RaidOutcome Check(double now, int participantsUp)
        {
            if (Phase != RaidPhase.Running) return RaidOutcome.None;
            // After the boss: the run is over once everyone has gone home through the portal, or the hang-out ends.
            if (Won) return _participants.Count == 0 || now - WonAt >= Settings.VictorySeconds ? RaidOutcome.Cleared : RaidOutcome.None;
            if (_participants.Count == 0 || participantsUp <= 0) return RaidOutcome.Wiped;
            if (now - StartedAt >= Settings.TimeLimit) return RaidOutcome.TimedOut;
            return RaidOutcome.None;
        }

        /// <summary>
        /// The boss died: the raiders are paid by the caller and stay up to celebrate. The run's clock stops;
        /// <see cref="Check"/> reports <see cref="RaidOutcome.Cleared"/> once they have all left. True the first time.
        /// </summary>
        public bool Win(double now)
        {
            if (Phase != RaidPhase.Running || Won) return false;
            Won = true;
            WonAt = now;
            return true;
        }

        /// <summary>Ends the run; the door reopens for pledges after <see cref="RaidSettings.ResetSeconds"/>.</summary>
        public void Finish(double now, RaidOutcome outcome)
        {
            if (Phase != RaidPhase.Running) return;
            LastOutcome = outcome;
            Phase = RaidPhase.Resetting;
            PhaseEnds = now + Settings.ResetSeconds;
        }

        /// <summary>Test and debug hook: fills the key without anyone paying.</summary>
        public void FillKey()
        {
            if (Phase != RaidPhase.Collecting) return;
            foreach (var c in KeyColors) _pledged[c] = Settings.Cost(c);
        }
    }

    /// <summary>
    /// Which of a layer's raid doors is open. One at a time: clearing its raid spends it (it goes dark) and opens
    /// the next one that hasn't been cleared. Once every door is spent they all come back, except the one just cleared.
    /// </summary>
    public sealed class RaidDoorRotation
    {
        private ulong _spent;

        public RaidDoorRotation(int count)
        {
            if (count < 1 || count > 64) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
        }

        public int Count { get; }
        /// <summary>The open door.</summary>
        public int Active { get; private set; }
        /// <summary>Spent doors as bits (bit i = door i).</summary>
        public ulong SpentMask => _spent;

        public bool IsSpent(int door) => (_spent & (1UL << door)) != 0;

        /// <summary>The open door's raid was cleared: it goes dark and the next door opens. Returns the new open door.</summary>
        public int Cleared()
        {
            int cleared = Active;
            _spent |= 1UL << cleared;
            if (Count == 1)
            {
                _spent = 0;
                return Active;
            }
            ulong all = Count == 64 ? ulong.MaxValue : (1UL << Count) - 1;
            if ((_spent & all) == all) _spent = 1UL << cleared;
            for (int step = 1; step <= Count; step++)
            {
                int next = (cleared + step) % Count;
                if (IsSpent(next)) continue;
                Active = next;
                break;
            }
            return Active;
        }
    }

    /// <summary>Where a layer's raid door stands: by a shrine near the start, on open ground away from the world's wrap seam.</summary>
    public static class RaidSite
    {
        /// <summary>The door stands this far east of its shrine (shrines sit on flattened, open ground).</summary>
        public const float ShrineOffset = 12f;
        /// <summary>Keeps the door (and the raid platform above it) clear of the world's wrap seam.</summary>
        public const float EdgeMargin = 60f;
        /// <summary>Not at the start shrine itself: a short walk out.</summary>
        public const float MinFromStart = 60f;

        public static Vector2 DoorFor(LayerMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var start = map.StartPoint.World;
            Vector2 best = default;
            float bestDistance = float.MaxValue;
            foreach (var s in map.Shrines)
            {
                var door = s.Point.World + new Vector2(ShrineOffset, 0f);
                if (!ClearOfSeam(map, door)) continue;
                float d = map.Distance(start, door);
                if (d < MinFromStart || d >= bestDistance) continue;
                bestDistance = d;
                best = door;
            }
            if (bestDistance < float.MaxValue) return best;
            // No shrine fits: a spot a little way from the start (wrapped into the world).
            return map.Locate(start + new Vector2(MinFromStart, 0f)).World;
        }

        /// <summary>How many raid doors a layer has (fewer if it lacks shrines to put them by).</summary>
        public const int DoorCount = 4;

        /// <summary>
        /// The layer's raid doors: the first is <see cref="DoorFor"/> (near the start, and the raid platforms float
        /// above it); each next one is by the shrine farthest from the doors already placed, so they spread out.
        /// </summary>
        public static IReadOnlyList<Vector2> DoorsFor(LayerMap map, int count = DoorCount)
        {
            var doors = new List<Vector2> { DoorFor(map) };
            var spots = new List<Vector2>();
            foreach (var s in map.Shrines)
            {
                var door = s.Point.World + new Vector2(ShrineOffset, 0f);
                if (ClearOfSeam(map, door)) spots.Add(door);
            }
            while (doors.Count < count)
            {
                int best = -1;
                float bestDistance = MinApart;
                for (int i = 0; i < spots.Count; i++)
                {
                    float nearest = float.MaxValue;
                    foreach (var d in doors) nearest = Math.Min(nearest, map.Distance(spots[i], d));
                    if (nearest <= bestDistance) continue;
                    bestDistance = nearest;
                    best = i;
                }
                if (best < 0) break;
                doors.Add(spots[best]);
                spots.RemoveAt(best);
            }
            return doors;
        }

        /// <summary>Two doors stand at least this far apart.</summary>
        public const float MinApart = 150f;

        /// <summary>True when everything within <see cref="EdgeMargin"/> of the spot is on the same side of the wrap seam.</summary>
        public static bool ClearOfSeam(LayerMap map, Vector2 spot)
        {
            if (!map.Tiling.IsCanonical(spot)) return false;
            for (int i = 0; i < 16; i++)
            {
                float a = i * MathF.PI / 8f;
                if (!map.Tiling.IsCanonical(spot + new Vector2(MathF.Cos(a), MathF.Sin(a)) * EdgeMargin)) return false;
            }
            return true;
        }
    }
}
