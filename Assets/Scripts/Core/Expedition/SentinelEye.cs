using System;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    public enum EyeEvent : byte
    {
        None,
        /// <summary>A spot on the shell starts to glow: the eye opens there in <see cref="SentinelEye.WarnSeconds"/>.</summary>
        Warn,
        Opened,
        /// <summary>Nobody hit it in time; it shut.</summary>
        Missed,
    }

    /// <summary>
    /// Sentinel Eye: a stone shell floats over the middle of the room, turning. Again and again a spot on
    /// it glows (<see cref="WarnSeconds"/>), then an eye opens there for <see cref="OpenSeconds"/>; a hit
    /// while it's open counts and shuts it. The eye opens on any side, so the group spreads around the
    /// shell, which fires bolts at them meanwhile. <see cref="Target"/> hits solve the room.
    /// Spots are on a fixed grid (<see cref="Azimuths"/> x <see cref="ElevationAngles"/>), so the server
    /// and clients agree on where the eye is from a few bits.
    /// </summary>
    public sealed class SentinelEye
    {
        /// <summary>The shell's centre above the floor, and its radius.</summary>
        public const float ShellHeight = 7f;
        public const float ShellRadius = 3f;
        /// <summary>The eye bulges from the shell: its hit sphere sits this far out from the centre.</summary>
        public const float EyeOut = ShellRadius + 0.15f;
        public const float EyeRadius = 0.65f;
        /// <summary>How fast the shell turns (radians per second of room time).</summary>
        public const float Spin = 0.3f;
        public const double WarnSeconds = 0.6;
        public const double OpenSeconds = 0.8;
        public const double MinGap = 1.0;
        public const double MaxGap = 2.2;
        /// <summary>The first warning comes this long after the room starts.</summary>
        public const double FirstOpen = 3.0;
        public const int Azimuths = 16;
        public static readonly float[] ElevationAngles = { -0.3f, 0.05f, 0.4f, 0.75f };
        /// <summary>The shell's bolts: one at a player every so often.</summary>
        public const double BoltEvery = 2.4;
        public const float BoltDamage = 8f;

        private enum Phase : byte
        {
            Waiting,
            Warning,
            Open,
        }

        private CombatRandom _random;
        private Phase _phase = Phase.Waiting;
        private double _phaseEnds;
        private double _nextBolt;

        public SentinelEye(uint seed, int players, double now)
        {
            _random = new CombatRandom(seed * 747796405u + 2891336453u | 1u);
            Target = 6 + Math.Max(1, players);
            _phaseEnds = now + FirstOpen;
            _nextBolt = now + FirstOpen + 1.0;
            Azimuth = -1;
        }

        public int Target { get; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public int Azimuth { get; private set; }
        public int Elevation { get; private set; }
        public bool IsOpen => _phase == Phase.Open;
        public bool IsWarning => _phase == Phase.Warning;
        public bool Solved => Hits >= Target;
        public float Progress => Math.Min(1f, Hits / (float)Target);

        public EyeEvent Step(double now)
        {
            if (Solved || now < _phaseEnds) return EyeEvent.None;
            switch (_phase)
            {
                case Phase.Waiting:
                {
                    // A new spot, never the last one's side.
                    int az;
                    do az = _random.NextInt(Azimuths); while (Azimuth >= 0 && Math.Abs(((az - Azimuth + Azimuths + Azimuths / 2) % Azimuths) - Azimuths / 2) < 3);
                    Azimuth = az;
                    Elevation = _random.NextInt(ElevationAngles.Length);
                    _phase = Phase.Warning;
                    _phaseEnds = now + WarnSeconds;
                    return EyeEvent.Warn;
                }
                case Phase.Warning:
                    _phase = Phase.Open;
                    _phaseEnds = now + OpenSeconds;
                    return EyeEvent.Opened;
                default:
                    Misses++;
                    Shut(now);
                    return EyeEvent.Missed;
            }
        }

        private void Shut(double now)
        {
            _phase = Phase.Waiting;
            _phaseEnds = now + MinGap + _random.NextFloat() * (MaxGap - MinGap);
        }

        /// <summary>A shot reached the eye. True if it was open (it counts and shuts).</summary>
        public bool Hit(double now)
        {
            if (!IsOpen || Solved) return false;
            Hits++;
            Shut(now);
            return true;
        }

        /// <summary>True when the shell should fire its next bolt.</summary>
        public bool TakeBoltDue(double now)
        {
            if (Solved || now < _nextBolt) return false;
            _nextBolt = now + BoltEvery;
            return true;
        }

        /// <summary>The shell's centre relative to the room's centre.</summary>
        public static Vector3 ShellCenter => new Vector3(0f, ShellHeight, 0f);

        /// <summary>
        /// The eye's centre relative to the room's centre, at room time <paramref name="stageTime"/>
        /// (the shell turns, carrying the spot with it).
        /// </summary>
        public static Vector3 EyeAt(int azimuth, int elevation, double stageTime)
        {
            float yaw = (float)(2 * Math.PI * azimuth / Azimuths + Spin * stageTime);
            float pitch = ElevationAngles[Math.Max(0, Math.Min(ElevationAngles.Length - 1, elevation))];
            var dir = new Vector3((float)(Math.Cos(pitch) * Math.Sin(yaw)), (float)Math.Sin(pitch), (float)(Math.Cos(pitch) * Math.Cos(yaw)));
            return ShellCenter + dir * EyeOut;
        }

        /// <summary>The shell's turn at room time <paramref name="stageTime"/> (radians, about the up axis).</summary>
        public static float ShellYaw(double stageTime) => (float)(Spin * stageTime);

        /// <summary>For the HUD and the shell's look: bit 0 open, bit 1 warning, 2-5 azimuth, 6-7 elevation, 8-15 hits, 16-23 target.</summary>
        public int Pack() =>
            (IsOpen ? 1 : 0) | (IsWarning ? 2 : 0) | (Math.Max(0, Azimuth) << 2) | (Elevation << 6) | (Math.Min(255, Hits) << 8) |
            (Math.Min(255, Target) << 16);

        public static bool OpenOf(int packed) => (packed & 1) != 0;
        public static bool WarningOf(int packed) => (packed & 2) != 0;
        public static int AzimuthOf(int packed) => (packed >> 2) & 15;
        public static int ElevationOf(int packed) => (packed >> 6) & 3;
        public static int HitsOf(int packed) => (packed >> 8) & 255;
        public static int TargetOf(int packed) => (packed >> 16) & 255;
    }
}
