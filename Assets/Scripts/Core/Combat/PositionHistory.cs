using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>One rewound state of a target.</summary>
    public struct HistorySample
    {
        public Vector3 Position;
        public bool Hittable;
    }

    /// <summary>
    /// Lag compensation: remembers where a target was on recent server ticks so the server can check
    /// a shot against what the shooter saw. A ring buffer of (tick, position, hittable) entries.
    /// Ticks must be recorded in increasing order; gaps are fine (it interpolates across them).
    /// </summary>
    public sealed class PositionHistory
    {
        private readonly int[] _ticks;
        private readonly Vector3[] _positions;
        private readonly bool[] _hittable;
        private readonly bool[] _teleported;
        private int _newest = -1;

        /// <param name="capacity">Ticks kept. At 60 ticks per second, 32 covers about half a second of ping.</param>
        public PositionHistory(int capacity = 32)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ticks = new int[capacity];
            _positions = new Vector3[capacity];
            _hittable = new bool[capacity];
            _teleported = new bool[capacity];
        }

        public int Capacity => _ticks.Length;
        public int Count { get; private set; }
        public int NewestTick => Count == 0 ? 0 : _ticks[_newest];
        public int OldestTick => Count == 0 ? 0 : _ticks[IndexFromOldest(0)];

        /// <summary>
        /// Stores the target's state at <paramref name="tick"/>. Recording the newest tick again
        /// overwrites it; older ticks are ignored. Set <paramref name="teleported"/> when the target
        /// jumped here instantly (a Wisp blink), so sampling never puts it partway along the jump.
        /// </summary>
        public void Record(int tick, Vector3 position, bool hittable = true, bool teleported = false)
        {
            if (Count > 0)
            {
                int newestTick = _ticks[_newest];
                if (tick < newestTick) return;
                if (tick == newestTick)
                {
                    _positions[_newest] = position;
                    _hittable[_newest] = hittable;
                    _teleported[_newest] = teleported;
                    return;
                }
            }
            _newest = (_newest + 1) % Capacity;
            _ticks[_newest] = tick;
            _positions[_newest] = position;
            _hittable[_newest] = hittable;
            _teleported[_newest] = teleported;
            if (Count < Capacity) Count++;
        }

        public void Clear()
        {
            Count = 0;
            _newest = -1;
        }

        /// <summary>Position at time <paramref name="tick"/> + <paramref name="fraction"/> (0-1 of a tick).</summary>
        public Vector3 Sample(int tick, float fraction) => SampleState(tick, fraction).Position;

        /// <summary>
        /// State at time <paramref name="tick"/> + <paramref name="fraction"/>, interpolated between the
        /// stored ticks either side. Times before the oldest or after the newest stored tick are clamped
        /// to those. Hittable comes from the earlier of the two ticks. Returns default when empty.
        /// </summary>
        public HistorySample SampleState(int tick, float fraction)
        {
            if (Count == 0) return default;

            // Work in offsets from the oldest tick to stay exact with large tick numbers.
            int oldestIdx = IndexFromOldest(0);
            int oldestTick = _ticks[oldestIdx];
            double t = (double)(tick - oldestTick) + fraction;
            if (t <= 0) return At(oldestIdx);
            if (t >= _ticks[_newest] - oldestTick) return At(_newest);

            // Walk back from the newest to find the entry at or before t (histories are short).
            for (int i = Count - 2; i >= 0; i--)
            {
                int a = IndexFromOldest(i);
                double ta = _ticks[a] - oldestTick;
                if (ta > t) continue;
                int b = IndexFromOldest(i + 1);
                double tb = _ticks[b] - oldestTick;
                float alpha = (float)((t - ta) / (tb - ta));
                if (_teleported[b] && alpha < 1f) return At(a);
                return new HistorySample
                {
                    Position = Vector3.Lerp(_positions[a], _positions[b], alpha),
                    Hittable = _hittable[a],
                };
            }
            return At(oldestIdx);
        }

        private HistorySample At(int index)
        {
            return new HistorySample { Position = _positions[index], Hittable = _hittable[index] };
        }

        private int IndexFromOldest(int i)
        {
            int oldest = (_newest - Count + 1 + Capacity) % Capacity;
            return (oldest + i) % Capacity;
        }
    }
}
