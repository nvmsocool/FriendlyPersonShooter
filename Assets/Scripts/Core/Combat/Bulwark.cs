using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>Tunables for the raid boss. Metres, seconds, degrees.</summary>
    public sealed class BulwarkSettings
    {
        /// <summary>The armored shell (a big, easy target that shrugs off most damage).</summary>
        public float BodyRadius { get; set; } = 2.6f;
        /// <summary>The weak plate on one side of the shell.</summary>
        public float PlateRadius { get; set; } = 1.0f;
        /// <summary>How far the plate's centre sits outside the shell.</summary>
        public float PlateOut { get; set; } = 0.1f;
        /// <summary>Damage share a hit does while it doesn't land on an open plate.</summary>
        public float ArmorMultiplier { get; set; } = 0.2f;
        /// <summary>Seconds the plate stays shut, then open.</summary>
        public float ClosedTime { get; set; } = 4.5f;
        public float OpenTime { get; set; } = 3f;
        /// <summary>The plate glows this long before it opens, so players can get in position.</summary>
        public float WarnTime { get; set; } = 1f;
        /// <summary>How fast the shell turns (degrees per second); faster below half health.</summary>
        public float TurnSpeed { get; set; } = 18f;
        public float EnragedTurnSpeed { get; set; } = 32f;
        /// <summary>Health fractions at which it calls in adds.</summary>
        public float[] AddWaveAt { get; set; } = { 0.75f, 0.5f, 0.25f };
        /// <summary>Hover height of the shell's centre above the floor, and its bob.</summary>
        public float HoverHeight { get; set; } = 3.4f;
        public float BobHeight { get; set; } = 0.3f;

        /// <summary>
        /// The Bulwark that roams the world: about half the raid boss's size, a quicker plate rhythm and turn, no add
        /// waves (a Guarded elite brings its own Mites).
        /// </summary>
        public static BulwarkSettings ForWorld() => new BulwarkSettings
        {
            BodyRadius = 1.3f, PlateRadius = 0.6f, PlateOut = 0.05f, ClosedTime = 3.5f, OpenTime = 2.2f, WarnTime = 0.8f,
            TurnSpeed = 35f, EnragedTurnSpeed = 55f, AddWaveAt = new float[0], HoverHeight = 1.9f, BobHeight = 0.15f,
        };

        /// <summary>Boss health for a group of <paramref name="players"/> (at least one).</summary>
        public static float HealthFor(int players, CombatSettings combat) => combat.BulwarkHealth * Math.Max(1, players);
    }

    /// <summary>
    /// The raid boss: an armored shell hovering over the middle of the raid platform. Its weak plate
    /// opens on a rhythm (shut 4.5 s, open 3 s) and the shell keeps turning, so players have to move
    /// around it to shoot the plate. Hits elsewhere, or on a shut plate, do a fifth of their damage.
    /// It calls in adds at 75%, 50% and 25% health, and turns faster below half. Slow slows its turn.
    /// A plain struct for ECS.
    /// </summary>
    public struct BulwarkBehavior
    {
        /// <summary>Centre of the floor under the boss.</summary>
        public Vector3 Home;
        /// <summary>Centre of the shell.</summary>
        public Vector3 Position;
        /// <summary>Which way the plate faces, in degrees (0 = +z, 90 = +x).</summary>
        public float Yaw;
        public bool Open;
        /// <summary>Seconds into the current shut or open stretch.</summary>
        public float PhaseTime;
        public float Age;
        /// <summary>Add waves called so far.</summary>
        public int WavesCalled;

        public static BulwarkBehavior Create(Vector3 home, float yaw, BulwarkSettings settings)
        {
            return new BulwarkBehavior
            {
                Home = home,
                Position = home + new Vector3(0f, settings.HoverHeight, 0f),
                Yaw = yaw,
            };
        }

        /// <summary>True in the last <see cref="BulwarkSettings.WarnTime"/> before the plate opens.</summary>
        public bool OpeningSoon(BulwarkSettings settings) => !Open && PhaseTime >= settings.ClosedTime - settings.WarnTime;

        /// <summary>
        /// One tick: turn (slower when Slowed: <paramref name="moveMultiplier"/>), bob, and open or shut the plate.
        /// </summary>
        public void Step(float dt, float healthFraction, float moveMultiplier, BulwarkSettings settings)
        {
            Age += dt;
            float speed = healthFraction < 0.5f ? settings.EnragedTurnSpeed : settings.TurnSpeed;
            Yaw = (Yaw + speed * moveMultiplier * dt) % 360f;
            Position = Home + new Vector3(0f, settings.HoverHeight + settings.BobHeight * (float)Math.Sin(Age * 1.3f), 0f);

            PhaseTime += dt;
            if (Open && PhaseTime >= settings.OpenTime)
            {
                Open = false;
                PhaseTime = 0f;
            }
            else if (!Open && PhaseTime >= settings.ClosedTime)
            {
                Open = true;
                PhaseTime = 0f;
            }
        }

        public static Vector3 FacingOf(float yaw)
        {
            float r = yaw * (float)Math.PI / 180f;
            return new Vector3((float)Math.Sin(r), 0f, (float)Math.Cos(r));
        }

        public static Vector3 PlateCenterOf(Vector3 position, float yaw, BulwarkSettings settings) =>
            position + FacingOf(yaw) * (settings.BodyRadius + settings.PlateOut);

        /// <summary>
        /// What a hit does: full damage and a weak-point hit only on an open plate; a fifth of the damage anywhere else.
        /// </summary>
        public void HitOutcome(bool hitPlate, BulwarkSettings settings, out float damageMultiplier, out bool weakPoint)
        {
            weakPoint = hitPlate && Open;
            damageMultiplier = weakPoint ? 1f : settings.ArmorMultiplier;
        }

        /// <summary>
        /// The add wave due now (1, 2 or 3) if health just fell past the next threshold, else 0.
        /// Each wave is returned once.
        /// </summary>
        public int TakeWaveDue(float healthFraction, BulwarkSettings settings)
        {
            var at = settings.AddWaveAt;
            if (WavesCalled >= at.Length || healthFraction > at[WavesCalled]) return 0;
            WavesCalled++;
            return WavesCalled;
        }
    }
}
