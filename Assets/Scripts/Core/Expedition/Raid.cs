using System;
using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for the raid door and its run. Essence, metres, seconds.</summary>
    public sealed class RaidSettings
    {
        /// <summary>
        /// The key: two colors, so one player rarely has the mix and groups pool. Red and Green, so it
        /// never competes with the Yellow gate. About 10 Skitters and 4 Mite swarms for one player.
        /// </summary>
        public int RedCost { get; set; } = 20;
        public int GreenCost { get; set; } = 12;
        /// <summary>Stand this close to the door to pledge and to be counted in.</summary>
        public float UseRadius { get; set; } = 7f;
        /// <summary>Once the key is full: seconds everyone has to reach the door before it takes them in.</summary>
        public float GatherSeconds { get; set; } = 20f;
        /// <summary>A run (three rooms and the boss) ends as a loss after this long.</summary>
        public float TimeLimit { get; set; } = 720f;
        /// <summary>Seconds after a run before the door takes pledges again.</summary>
        public float ResetSeconds { get; set; } = 20f;
        public int MaxPlayers { get; set; } = 8;
        /// <summary>Clear reward for everyone still in the raid: XP (one level) and some Yellow toward the gate.</summary>
        public int ClearXp { get; set; } = 300;
        public int ClearYellow { get; set; } = 10;

        public int Cost(EssenceColor color) => color == EssenceColor.Red ? RedCost : color == EssenceColor.Green ? GreenCost : 0;
    }

    public enum RaidPhase : byte
    {
        /// <summary>Taking pledges toward the key.</summary>
        Collecting = 0,
        /// <summary>The key is full: a countdown, then whoever stands at the door goes in.</summary>
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
        /// <summary>The key just filled with someone at the door: the countdown started.</summary>
        GatherStarted,
        /// <summary>The countdown ended with nobody at the door; the key stays full for next time.</summary>
        GatherCancelled,
        /// <summary>The countdown ended and <see cref="RaidDoor.Participants"/> went in.</summary>
        Started,
        /// <summary>The door takes pledges again after a run.</summary>
        Reopened,
    }

    /// <summary>
    /// A raid door (MVP step 6). Players standing at it pledge Red and Green essence toward its key,
    /// carried first, then from their party bank; strangers pool into the same key. When the key is
    /// full a short countdown starts, and whoever is standing at the door when it ends goes in together
    /// (up to 8). The run ends when the boss dies, when everyone inside is down at once, or at the time
    /// limit; then the door resets and takes pledges for a new key. One run at a time per door.
    /// </summary>
    public sealed class RaidDoor
    {
        public static readonly EssenceColor[] KeyColors = { EssenceColor.Red, EssenceColor.Green };

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

        /// <summary>
        /// Moves what the key still needs from the wallet (carried first, then banked) into the key.
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
        /// Advances the door. <paramref name="atDoor"/> lists the players standing at it now (up and not
        /// already inside), in the order they should be let in.
        /// </summary>
        public RaidDoorEvent Update(double now, IReadOnlyList<int> atDoor)
        {
            switch (Phase)
            {
                case RaidPhase.Collecting:
                    if (KeyFull && atDoor.Count > 0)
                    {
                        Phase = RaidPhase.Gathering;
                        PhaseEnds = now + Settings.GatherSeconds;
                        return RaidDoorEvent.GatherStarted;
                    }
                    return RaidDoorEvent.None;
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
            if (_participants.Count == 0 || participantsUp <= 0) return RaidOutcome.Wiped;
            if (now - StartedAt >= Settings.TimeLimit) return RaidOutcome.TimedOut;
            return RaidOutcome.None;
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
