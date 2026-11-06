using System;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>The boss at the end of a run.</summary>
    public enum RaidBossKind : byte
    {
        Bulwark = 0,
        /// <summary>Two smaller Bulwarks that must die close together (<see cref="RaidTwins"/>).</summary>
        Twins = 1,
        /// <summary>A Bulwark over a floor that falls away, which ties players together (<see cref="RaidWeaver"/>).</summary>
        Weaver = 2,
    }

    public enum TwinEvent : byte
    {
        None,
        /// <summary>One twin fell; the other must follow within <see cref="RaidTwins.Window"/> seconds.</summary>
        FirstDown,
        /// <summary>Too slow: the fallen twin is back (at <see cref="RaidTwins.ReviveHealth"/>).</summary>
        Revived,
        /// <summary>Both are down: the boss is beaten.</summary>
        Beaten,
    }

    /// <summary>
    /// The Twins: two Bulwarks, each with <see cref="HealthShare"/> of the single boss's health. When
    /// one dies the other must die within <see cref="Window"/> seconds, or the first comes back with
    /// <see cref="ReviveHealth"/> of its health. Add waves follow their combined health.
    /// </summary>
    public sealed class RaidTwins
    {
        public const double Window = 10.0;
        public const float HealthShare = 0.6f;
        public const float ReviveHealth = 0.5f;
        /// <summary>The twins hover this far either side of the arena's middle.</summary>
        public const float Apart = 8f;

        private readonly bool[] _down = new bool[2];
        private double _firstDownAt = -1;
        private int _wavesCalled;

        public bool IsDown(int twin) => _down[twin];
        public bool Beaten => _down[0] && _down[1];
        /// <summary>Which twin is down and waiting (-1 if none or both).</summary>
        public int Waiting => _down[0] == _down[1] ? -1 : _down[0] ? 0 : 1;
        public int Revivals { get; private set; }

        public double TimeLeft(double now) => Waiting < 0 ? -1 : Math.Max(0, _firstDownAt + Window - now);

        public TwinEvent Killed(int twin, double now)
        {
            if (twin < 0 || twin > 1 || _down[twin]) return TwinEvent.None;
            _down[twin] = true;
            if (Beaten) return TwinEvent.Beaten;
            _firstDownAt = now;
            return TwinEvent.FirstDown;
        }

        /// <summary>Returns <see cref="TwinEvent.Revived"/> when the window ran out; <see cref="Waiting"/> is then -1 again.</summary>
        public TwinEvent Step(double now, out int revived)
        {
            revived = -1;
            int w = Waiting;
            if (w < 0 || now < _firstDownAt + Window) return TwinEvent.None;
            _down[w] = false;
            revived = w;
            Revivals++;
            return TwinEvent.Revived;
        }

        /// <summary>The add wave due for the twins' combined health fraction (like the Bulwark's), else 0.</summary>
        public int TakeWaveDue(float combinedFraction, float[] waveAt)
        {
            if (_wavesCalled >= waveAt.Length || combinedFraction > waveAt[_wavesCalled]) return 0;
            _wavesCalled++;
            return _wavesCalled;
        }
    }
}
