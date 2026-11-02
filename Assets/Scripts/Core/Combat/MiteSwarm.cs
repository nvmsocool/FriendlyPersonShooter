using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for the Mite swarm (target-switching drill). Metres, seconds, radians.</summary>
    public sealed class MiteSwarmSettings
    {
        public int MinCount { get; set; } = 8;
        public int MaxCount { get; set; } = 20;
        public float DriftSpeed { get; set; } = 4f;
        /// <summary>How fast the drift direction turns, in radians per second (keeps the drift smooth).</summary>
        public float TurnRate { get; set; } = 1.2f;
        public float MinTurnInterval { get; set; } = 1f;
        public float MaxTurnInterval { get; set; } = 2.5f;
        /// <summary>The swarm centre stays within this horizontal distance of home.</summary>
        public float LeashRadius { get; set; } = 10f;
        /// <summary>Height of the swarm centre above home.</summary>
        public float Height { get; set; } = 1.4f;
        /// <summary>Mites sit between these distances from the centre.</summary>
        public float MinSpread { get; set; } = 1.2f;
        public float MaxSpread { get; set; } = 3f;
        /// <summary>Squashes the swarm vertically so it reads as a cloud, not a ball.</summary>
        public float VerticalScale { get; set; } = 0.5f;
        /// <summary>How fast mites circle the centre, in radians per second.</summary>
        public float OrbitSpeed { get; set; } = 0.9f;
        public float BobHeight { get; set; } = 0.2f;
        public float BobSpeed { get; set; } = 3f;
        public float MiteRadius { get; set; } = 0.15f;
    }

    /// <summary>
    /// The slowly drifting point a Mite swarm gathers around. A plain struct for ECS.
    /// Position is at swarm height (home + <see cref="MiteSwarmSettings.Height"/>).
    /// </summary>
    public struct MiteSwarmCentre
    {
        public Vector3 Home;
        public Vector3 Position;
        /// <summary>Current drift direction, radians around Y (0 = +X, toward +Z).</summary>
        public float Heading;
        public float TargetHeading;
        public float TimeToTurn;
        public CombatRandom Random;

        public static MiteSwarmCentre Create(Vector3 home, uint seed, MiteSwarmSettings s)
        {
            var c = new MiteSwarmCentre
            {
                Home = home,
                Position = home + new Vector3(0f, s.Height, 0f),
                Random = new CombatRandom(seed),
            };
            c.Heading = c.Random.Angle();
            c.TargetHeading = c.Heading;
            c.TimeToTurn = c.Random.Range(s.MinTurnInterval, s.MaxTurnInterval);
            return c;
        }

        public void Step(float dt, MiteSwarmSettings s)
        {
            float dx = Home.X - Position.X, dz = Home.Z - Position.Z;
            bool farOut = dx * dx + dz * dz > (s.LeashRadius * 0.8f) * (s.LeashRadius * 0.8f);
            TimeToTurn -= dt;
            if (farOut)
            {
                TargetHeading = MathF.Atan2(dz, dx);
            }
            else if (TimeToTurn <= 0f)
            {
                TargetHeading = Heading + Random.Range(-2f, 2f);
                TimeToTurn = Random.Range(s.MinTurnInterval, s.MaxTurnInterval);
            }

            float diff = WrapAngle(TargetHeading - Heading);
            float maxTurn = s.TurnRate * dt * (farOut ? 2f : 1f);
            Heading += Math.Clamp(diff, -maxTurn, maxTurn);

            Vector3 next = Position + new Vector3(MathF.Cos(Heading), 0f, MathF.Sin(Heading)) * (s.DriftSpeed * dt);
            Vector3 offset = new Vector3(next.X - Home.X, 0f, next.Z - Home.Z);
            float dist = offset.Length();
            if (dist > s.LeashRadius) offset *= s.LeashRadius / dist;
            Position = new Vector3(Home.X + offset.X, Home.Y + s.Height, Home.Z + offset.Z);
        }

        internal static float WrapAngle(float a)
        {
            const float twoPi = 2f * MathF.PI;
            a %= twoPi;
            if (a > MathF.PI) a -= twoPi;
            if (a < -MathF.PI) a += twoPi;
            return a;
        }
    }

    /// <summary>
    /// One Mite: a tiny target that holds its own spot around the swarm centre and slowly circles it.
    /// Spots are spread over a squashed sphere (a golden-angle spiral) so mites stay separate targets.
    /// Mites die to any hit (see <see cref="CombatSettings.MiteHealth"/>). A plain struct for ECS.
    /// </summary>
    public struct MiteBehavior
    {
        /// <summary>This mite's spot relative to the centre, before orbiting.</summary>
        public Vector3 Offset;
        public float OrbitAngle;
        public float BobPhase;
        public Vector3 Position;

        /// <summary>Makes mite <paramref name="index"/> of <paramref name="count"/>.</summary>
        public static MiteBehavior Create(int index, int count, Vector3 centre, ref CombatRandom random, MiteSwarmSettings s)
        {
            const float goldenAngle = 2.39996323f;
            float y = 1f - 2f * (index + 0.5f) / Math.Max(1, count);
            float ring = MathF.Sqrt(Math.Max(0f, 1f - y * y));
            float theta = index * goldenAngle;
            float spread = random.Range(s.MinSpread, s.MaxSpread);
            var m = new MiteBehavior
            {
                Offset = new Vector3(MathF.Cos(theta) * ring, y * s.VerticalScale, MathF.Sin(theta) * ring) * spread,
                BobPhase = random.Angle(),
            };
            m.Position = m.PositionAround(centre, s);
            return m;
        }

        public void Step(float dt, Vector3 centre, MiteSwarmSettings s)
        {
            OrbitAngle += s.OrbitSpeed * dt;
            BobPhase += s.BobSpeed * dt;
            Position = PositionAround(centre, s);
        }

        public HitTarget ToHitTarget(int id, MiteSwarmSettings s) => HitTarget.Body(id, Position, s.MiteRadius);

        private Vector3 PositionAround(Vector3 centre, MiteSwarmSettings s)
        {
            float c = MathF.Cos(OrbitAngle), sn = MathF.Sin(OrbitAngle);
            var rotated = new Vector3(Offset.X * c - Offset.Z * sn, Offset.Y, Offset.X * sn + Offset.Z * c);
            return centre + rotated + new Vector3(0f, MathF.Sin(BobPhase) * s.BobHeight, 0f);
        }
    }

    /// <summary>A whole swarm in one object: the centre plus its mites. Handy outside ECS and in tests.</summary>
    public sealed class MiteSwarm
    {
        public MiteSwarmCentre Centre;
        public readonly MiteBehavior[] Mites;
        public readonly bool[] Alive;

        public MiteSwarm(Vector3 home, uint seed, MiteSwarmSettings settings, int count = 0)
        {
            Settings = settings;
            Centre = MiteSwarmCentre.Create(home, seed, settings);
            var random = new CombatRandom(seed ^ 0xA5A5A5A5u);
            if (count <= 0) count = settings.MinCount + random.NextInt(settings.MaxCount - settings.MinCount + 1);
            Mites = new MiteBehavior[count];
            Alive = new bool[count];
            for (int i = 0; i < count; i++)
            {
                Mites[i] = MiteBehavior.Create(i, count, Centre.Position, ref random, settings);
                Alive[i] = true;
            }
        }

        public MiteSwarmSettings Settings { get; }

        public int AliveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Alive.Length; i++) if (Alive[i]) n++;
                return n;
            }
        }

        public void Step(float dt)
        {
            Centre.Step(dt, Settings);
            for (int i = 0; i < Mites.Length; i++) Mites[i].Step(dt, Centre.Position, Settings);
        }
    }
}
