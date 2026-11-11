using System.Collections.Generic;
using Unity.Mathematics;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Where the things you shoot to use them are, and how big (a sphere each): the gate, the up-portal and the raid
    /// door (their essence slots are in <see cref="EssenceSlots"/>). The host tests shots against these and the view
    /// draws them at the same spots. World positions (not wrapped near anyone).
    /// </summary>
    public static class ShotTargets
    {
        public const float GateRadius = 3.4f;
        public const float PortalRadius = 2.2f;
        public const float DoorRadius = 3f;

        private static readonly Dictionary<System.Numerics.Vector2, float> Grounds = new Dictionary<System.Numerics.Vector2, float>();

        /// <summary>The ground at a fixed site (remembered once its zone is built), or NaN while it isn't.</summary>
        public static float Ground(System.Numerics.Vector2 p)
        {
            if (!ExpeditionTerrain.Active) return 0f;
            if (Grounds.TryGetValue(p, out float y)) return y;
            y = ExpeditionTerrain.GroundY(p.X, p.Y, float.NaN);
            if (!float.IsNaN(y)) Grounds[p] = y;
            return y;
        }

        private static bool On(System.Numerics.Vector2 p, float up, out float3 at)
        {
            float y = Ground(p);
            at = new float3(p.X, float.IsNaN(y) ? 0f : y + up, p.Y);
            return !float.IsNaN(y);
        }

        /// <summary>The middle of the gate's opening.</summary>
        public static bool Gate(System.Numerics.Vector2 site, out float3 at) => On(site, ExpeditionWorld.GateHeight * 0.5f, out at);

        public static bool Portal(System.Numerics.Vector2 portal, out float3 at) => On(portal, 1.6f, out at);

        public static bool Door(System.Numerics.Vector2 door, out float3 at) => On(door, 4f, out at);
    }
}
