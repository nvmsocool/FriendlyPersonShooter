using System;

namespace FriendlyPersonShooter.Core.Expedition
{
    public enum CoreEvent : byte
    {
        None,
        /// <summary>Time for a wave: <see cref="HoldCore.WaveSize"/> runners come in from the gates.</summary>
        Wave,
        /// <summary>The core broke: the room starts over (full core, full timer).</summary>
        Broke,
        Solved,
    }

    /// <summary>
    /// Hold the Core: a crystal in the middle of the room must survive <see cref="Duration"/> seconds.
    /// Every <see cref="WaveEvery"/> seconds Skitters run at it from three gates; one that reaches it
    /// bursts and cracks it. If the core breaks the attempt starts over. Kill them on the way in.
    /// </summary>
    public sealed class HoldCore
    {
        public const double Duration = 60.0;
        public const double WaveEvery = 7.0;
        /// <summary>The first wave comes this long after the start, so the group can spread out.</summary>
        public const double FirstWave = 4.0;
        public const float CoreHealth = 100f;
        public const float RunnerDamage = 10f;
        /// <summary>A runner this close to the core's centre (horizontally) bursts.</summary>
        public const float BurstRadius = 2.3f;
        public const int Gates = 3;

        public HoldCore(int players, bool swarming, double now)
        {
            // One runner per gate at 4 players, more for bigger groups; Swarming adds half again.
            int size = Math.Max(2, 1 + (players + 1) / 2);
            WaveSize = swarming ? (size * 3 + 1) / 2 : size;
            Start(now);
        }

        public int WaveSize { get; }
        public float Health { get; private set; }
        public double AttemptStarted { get; private set; }
        public int Attempts { get; private set; }
        public int Waves { get; private set; }
        public bool Solved { get; private set; }
        private double _nextWave;

        public float HealthFraction => Health / CoreHealth;
        public float Progress(double now) => Solved ? 1f : (float)Math.Min(1.0, (now - AttemptStarted) / Duration);
        public double TimeLeft(double now) => Math.Max(0, AttemptStarted + Duration - now);

        private void Start(double now)
        {
            Attempts++;
            Health = CoreHealth;
            AttemptStarted = now;
            _nextWave = now + FirstWave;
        }

        /// <summary>A runner reached the core. True if that broke it (the caller then calls <see cref="Step"/>).</summary>
        public bool Burst()
        {
            if (Solved || Health <= 0f) return false;
            Health = Math.Max(0f, Health - RunnerDamage);
            return Health <= 0f;
        }

        public CoreEvent Step(double now)
        {
            if (Solved) return CoreEvent.None;
            if (Health <= 0f)
            {
                Start(now);
                return CoreEvent.Broke;
            }
            if (now >= AttemptStarted + Duration)
            {
                Solved = true;
                return CoreEvent.Solved;
            }
            if (now < _nextWave || now > AttemptStarted + Duration - 3.0) return CoreEvent.None;
            _nextWave = now + WaveEvery;
            Waves++;
            return CoreEvent.Wave;
        }

        /// <summary>For the HUD: bits 0-6 core health %, 7-13 seconds left.</summary>
        public int Pack(double now) => (int)Math.Round(HealthFraction * 100f) | ((int)Math.Ceiling(TimeLeft(now)) << 7);

        public static int HealthOf(int packed) => packed & 127;
        public static int SecondsOf(int packed) => (packed >> 7) & 127;
    }
}
