using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>How well a bot aims. Angles in degrees, time in seconds.</summary>
    public sealed class BotAimSettings
    {
        /// <summary>The bot aims at where the target was this long ago. After a switch it also waits this long before flicking.</summary>
        public float ReactionTime { get; set; } = 0.25f;
        /// <summary>How far ahead of the seen position the bot leads a moving target (0 = no lead, 1 = full lead over the reaction time).</summary>
        public float Prediction { get; set; } = 0.5f;
        /// <summary>Typical size of the wandering aim error (standard deviation), in degrees.</summary>
        public float TrackingErrorDegrees { get; set; } = 1.5f;
        /// <summary>How slowly the aim error wanders (time constant). Larger = smoother, slower drift.</summary>
        public float ErrorWanderTime { get; set; } = 0.35f;
        /// <summary>How quickly the crosshair catches up while tracking (per second; higher = tighter).</summary>
        public float TrackingSpeed { get; set; } = 12f;
        /// <summary>Seconds a flick to a new target takes, overshoot and settle included.</summary>
        public float FlickTime { get; set; } = 0.3f;
        /// <summary>A flick goes this share past the target before settling back (0.15 = 15% overshoot).</summary>
        public float FlickOvershoot { get; set; } = 0.12f;
        /// <summary>Fires when the aim error is within the target's angular radius times this.</summary>
        public float FireConeFactor { get; set; } = 0.9f;

        public static BotAimSettings Strong() => new BotAimSettings
        {
            ReactionTime = 0.18f,
            Prediction = 0.8f,
            TrackingErrorDegrees = 0.6f,
            ErrorWanderTime = 0.4f,
            TrackingSpeed = 18f,
            FlickTime = 0.22f,
            FlickOvershoot = 0.08f,
            FireConeFactor = 0.8f,
        };

        public static BotAimSettings Weak() => new BotAimSettings
        {
            ReactionTime = 0.35f,
            Prediction = 0.2f,
            TrackingErrorDegrees = 3f,
            ErrorWanderTime = 0.25f,
            TrackingSpeed = 6f,
            FlickTime = 0.45f,
            FlickOvershoot = 0.25f,
            FireConeFactor = 1.3f,
        };
    }

    /// <summary>One step of a bot's aim.</summary>
    public struct AimOutput
    {
        public float Yaw;
        public float Pitch;
        public Vector3 Direction;
        public bool Fire;
        /// <summary>Angle between the aim and where the bot believes the target is (drives <see cref="Fire"/>).</summary>
        public float BeliefErrorDegrees;
        /// <summary>Angle between the aim and where the target really is (for debugging and tuning).</summary>
        public float TrueErrorDegrees;
        /// <summary>True while the bot is still reacting to a new target, before its flick starts.</summary>
        public bool Reacting;
        public bool Flicking;
    }

    /// <summary>
    /// A human-like aimer for bot allies (and later bots hunting enemies). Each step it is told
    /// the current target and where that target really is; it then
    /// - sees the target with a delay (reaction time) and leads it a little by its seen velocity,
    /// - flicks to a new target after the reaction time, overshooting then settling,
    /// - tracks with a lag and a smoothly wandering error,
    /// - fires when its believed error is within the target's angular size.
    /// See <see cref="AimMath"/> for the yaw/pitch conventions.
    /// </summary>
    public sealed class BotAim
    {
        private const int HistorySize = 64;
        private const double VelocityWindow = 0.1;

        private readonly double[] _times = new double[HistorySize];
        private readonly Vector3[] _positions = new Vector3[HistorySize];
        private int _newest = -1;
        private int _count;

        private CombatRandom _random;
        private double _time;
        private int _targetId = int.MinValue;
        private double _switchTime;
        private bool _flicking;
        private bool _flickPending;
        private float _flickTimer;
        private float _flickStartYaw, _flickStartPitch;
        private float _noiseYaw, _noisePitch;

        public BotAim(BotAimSettings settings, uint seed, float startYaw = 0f, float startPitch = 0f)
        {
            Settings = settings;
            _random = new CombatRandom(seed);
            Yaw = startYaw;
            Pitch = startPitch;
        }

        public BotAimSettings Settings { get; }
        /// <summary>Aim without the wandering error.</summary>
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public int TargetId => _targetId;

        /// <summary>
        /// Advances by <paramref name="dt"/> seconds toward target <paramref name="targetId"/>, which is
        /// really at <paramref name="targetPosition"/> with hit radius <paramref name="targetRadius"/>.
        /// </summary>
        public AimOutput Step(float dt, Vector3 eye, int targetId, Vector3 targetPosition, float targetRadius)
        {
            var s = Settings;
            _time += dt;

            if (targetId != _targetId)
            {
                _targetId = targetId;
                _switchTime = _time;
                _count = 0;
                _newest = -1;
                _flicking = false;
                _flickPending = true;
            }
            Remember(_time, targetPosition);

            // What the bot believes: the seen position, led by the seen velocity.
            double seenAt = _time - s.ReactionTime;
            Vector3 seen = Recall(seenAt);
            Vector3 velocity = (seen - Recall(seenAt - VelocityWindow)) / (float)VelocityWindow;
            Vector3 believed = seen + velocity * (s.ReactionTime * s.Prediction);
            AimMath.ToYawPitch(believed - eye, out float wantYaw, out float wantPitch);

            bool reacting = _time - _switchTime < s.ReactionTime;
            if (!reacting)
            {
                if (_flickPending)
                {
                    _flickPending = false;
                    // Reaction time just ran out: start the flick from wherever the crosshair is.
                    _flicking = true;
                    _flickTimer = 0f;
                    _flickStartYaw = Yaw;
                    _flickStartPitch = Pitch;
                }

                if (_flicking)
                {
                    _flickTimer += dt;
                    float p = s.FlickTime > 0f ? Math.Min(1f, _flickTimer / s.FlickTime) : 1f;
                    float f = FlickCurve(p, s.FlickOvershoot);
                    Yaw = _flickStartYaw + AimMath.DeltaAngle(_flickStartYaw, wantYaw) * f;
                    Pitch = _flickStartPitch + (wantPitch - _flickStartPitch) * f;
                    if (p >= 1f) _flicking = false;
                }
                else
                {
                    float k = 1f - MathF.Exp(-s.TrackingSpeed * dt);
                    Yaw += AimMath.DeltaAngle(Yaw, wantYaw) * k;
                    Pitch += (wantPitch - Pitch) * k;
                }
            }
            Yaw = AimMath.DeltaAngle(0f, Yaw);

            // Wandering error (Ornstein-Uhlenbeck): smooth, with a steady spread of TrackingErrorDegrees.
            float tau = Math.Max(1e-3f, s.ErrorWanderTime);
            float pull = Math.Min(1f, dt / tau);
            float kick = s.TrackingErrorDegrees * MathF.Sqrt(2f * pull);
            _noiseYaw += -_noiseYaw * pull + kick * _random.Gaussian();
            _noisePitch += -_noisePitch * pull + kick * _random.Gaussian();

            var output = new AimOutput
            {
                Yaw = AimMath.DeltaAngle(0f, Yaw + _noiseYaw),
                Pitch = Math.Clamp(Pitch + _noisePitch, -89f, 89f),
                Reacting = reacting,
                Flicking = _flicking,
            };
            output.Direction = AimMath.Direction(output.Yaw, output.Pitch);
            output.BeliefErrorDegrees = HitGeometry.AngleBetween(output.Direction, believed - eye);
            output.TrueErrorDegrees = HitGeometry.AngleBetween(output.Direction, targetPosition - eye);
            float cone = AimMath.AngularRadius(targetRadius, Vector3.Distance(eye, believed)) * s.FireConeFactor;
            output.Fire = !reacting && output.BeliefErrorDegrees <= cone;
            return output;
        }

        /// <summary>0 to 1 + overshoot over the first 60%, then eases back to 1.</summary>
        internal static float FlickCurve(float p, float overshoot)
        {
            const float peakAt = 0.6f;
            float peak = 1f + overshoot;
            if (p < peakAt)
            {
                float u = p / peakAt;
                return peak * (1f - (1f - u) * (1f - u));
            }
            float v = (p - peakAt) / (1f - peakAt);
            float smooth = v * v * (3f - 2f * v);
            return peak + (1f - peak) * smooth;
        }

        private void Remember(double time, Vector3 position)
        {
            _newest = (_newest + 1) % HistorySize;
            _times[_newest] = time;
            _positions[_newest] = position;
            if (_count < HistorySize) _count++;
        }

        /// <summary>Target position at <paramref name="time"/>, clamped to what's remembered.</summary>
        private Vector3 Recall(double time)
        {
            int idx = _newest;
            for (int i = 0; i < _count; i++)
            {
                int cur = (_newest - i + HistorySize) % HistorySize;
                if (_times[cur] <= time)
                {
                    if (i == 0) return _positions[cur];
                    int next = (cur + 1) % HistorySize;
                    float a = (float)((time - _times[cur]) / (_times[next] - _times[cur]));
                    return Vector3.Lerp(_positions[cur], _positions[next], a);
                }
                idx = cur;
            }
            return _positions[idx];
        }
    }
}
