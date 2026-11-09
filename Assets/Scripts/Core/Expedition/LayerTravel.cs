using System;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Where the group lands after a layer change.</summary>
    public enum LayerArrival : byte
    {
        /// <summary>Going down: the new layer's start shrine.</summary>
        Start = 0,
        /// <summary>Going up: just past the upper layer's gate, where the group went down.</summary>
        Gate = 1,
    }

    /// <summary>
    /// The group moves between layers together. Walking through an open gate (layers 1 and 2) or into the
    /// up-portal by the start (layers 2 and 3) starts a short countdown for everyone, so friends can catch up;
    /// then the whole group moves. The deepest layer's gate ends the expedition instead.
    /// </summary>
    public sealed class LayerTravel
    {
        /// <summary>Seconds between triggering a move and the move.</summary>
        public const float Countdown = 10f;
        /// <summary>How far from the start shrine the up-portal stands.</summary>
        public const float PortalOffset = 12f;
        public const float PortalRadius = 2.5f;
        /// <summary>How far past the gate the group lands when coming back up (outside its pass radius).</summary>
        public const float GateArrivalOffset = 8f;

        public LayerTravel(int layer = 1)
        {
            Layer = Clamp(layer);
        }

        public int Layer { get; private set; }
        public bool Pending { get; private set; }
        public int Target { get; private set; }
        public LayerArrival Arrival { get; private set; }
        public double DepartsAt { get; private set; }
        /// <summary>The player who started the move.</summary>
        public int StartedBy { get; private set; }

        public bool CanGoDown => Layer < LayerSettings.LayerCount;
        public bool CanGoUp => Layer > 1;

        /// <summary>Seconds until the move (0 when none is pending).</summary>
        public float SecondsLeft(double now) => Pending ? (float)Math.Max(0.0, DepartsAt - now) : 0f;

        /// <summary>Someone went through the gate: the group heads down. False on the deepest layer or while a move is pending.</summary>
        public bool BeginDown(double now, int playerId) => Begin(now, playerId, Layer + 1, LayerArrival.Start, CanGoDown);

        /// <summary>Someone stepped into the up-portal: the group heads back up. False on layer 1 or while a move is pending.</summary>
        public bool BeginUp(double now, int playerId) => Begin(now, playerId, Layer - 1, LayerArrival.Gate, CanGoUp);

        public void Cancel() => Pending = false;

        /// <summary>True once, when the countdown ends and <see cref="Layer"/> changes.</summary>
        public bool Step(double now)
        {
            if (!Pending || now < DepartsAt) return false;
            Pending = false;
            Layer = Target;
            return true;
        }

        private bool Begin(double now, int playerId, int target, LayerArrival arrival, bool allowed)
        {
            if (Pending || !allowed) return false;
            Pending = true;
            Target = target;
            Arrival = arrival;
            DepartsAt = now + Countdown;
            StartedBy = playerId;
            return true;
        }

        /// <summary>Where the up-portal stands on a layer below the first (none on layer 1).</summary>
        public static bool TryPortal(LayerMap map, int layer, out Vector2 portal)
        {
            portal = default;
            if (map == null || layer <= 1) return false;
            portal = map.StartPoint.World + new Vector2(PortalOffset, 0f);
            return true;
        }

        /// <summary>Where a group coming up lands: a few metres before the gate, on the side they first walked up from.</summary>
        public static Vector2 GateArrival(LayerMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var gate = map.Gates[0].Point.World;
            var away = gate - map.StartPoint.World;
            away = away.LengthSquared() > 1e-4f ? Vector2.Normalize(away) : Vector2.UnitX;
            return gate - away * GateArrivalOffset;
        }

        private static int Clamp(int layer) => Math.Max(1, Math.Min(LayerSettings.LayerCount, layer));
    }
}
