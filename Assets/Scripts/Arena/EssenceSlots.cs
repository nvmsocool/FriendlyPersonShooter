using FriendlyPersonShooter.Core.Expedition;
using Unity.Mathematics;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Where the essence slots are on the gate and the raid door. Banking is shooting an empty slot: on the gate's
    /// tablet (one slot per essence of the gate's price) it banks one carried essence of the gate's color; on the raid
    /// door (one slot per essence of the key) it puts one essence of that color into the key. The host tests shots
    /// against these and the view draws them at the same spots. Positions are relative to the gate's or door's foot.
    /// </summary>
    public static class EssenceSlots
    {
        /// <summary>Slots per row on the gate's tablet, their spacing and size (metres).</summary>
        public const int GateColumns = 10;
        public const float GatePitch = 0.72f, GateSize = 0.46f;
        /// <summary>The tablet's faces are this far in front of and behind the gate.</summary>
        public const float GateFace = 0.32f;
        public const float DoorWidth = 7f, DoorHeight = 8f;
        /// <summary>A slot is hit within this much of its size from its centre (a diamond is a bit wider than its side).</summary>
        public const float HitScale = 0.75f;

        public static int GateRows(int slots) => (slots + GateColumns - 1) / GateColumns;

        /// <summary>The gate tablet's bottom (it sits on the lintel).</summary>
        public static float GateTabletBase => ExpeditionWorld.GateHeight + 0.5f;

        public static float GateTabletHeight(int slots) => GateRows(slots) * GatePitch + 0.5f;

        /// <summary>The middle of gate slot <paramref name="k"/> of <paramref name="slots"/> (bottom row first, a short last row centred).</summary>
        public static float3 GateSlot(int k, int slots)
        {
            int row = k / GateColumns, col = k % GateColumns;
            int inRow = math.min(GateColumns, slots - row * GateColumns);
            return new float3((col - (inRow - 1) * 0.5f) * GatePitch, GateTabletBase + 0.25f + (row + 0.5f) * GatePitch, 0f);
        }

        /// <summary>
        /// Raid door slot <paramref name="k"/> of <paramref name="color"/>, and its size: Blue up the left post and Green
        /// up the right (two columns, bottom first), Purple across the lintel. z is the face's distance from the middle.
        /// </summary>
        public static float3 DoorSlot(EssenceColor color, int k, out float size)
        {
            const float w = DoorWidth, h = DoorHeight;
            int n = RaidKey.Cost(color);
            if (color == EssenceColor.Purple)
            {
                // The lintel is 1.6 deep: its slots sit on its faces, not inside it.
                float pitch = math.min(0.95f, (w + 1.6f) / math.max(1, n));
                size = math.min(0.5f, pitch * 0.62f);
                return new float3((k - (n - 1) * 0.5f) * pitch, h + 0.5f, 0.84f);
            }
            float side = color == EssenceColor.Blue ? -1f : 1f;
            int rows = (n + 1) / 2;
            float step = math.min(1f, (h - 2.2f) / math.max(1, rows - 1));
            size = math.min(0.42f, step * 0.6f);
            return new float3(side * w * 0.5f + (k % 2 == 0 ? -0.33f : 0.33f), 1.4f + (k / 2) * step, 0.72f);
        }

        /// <summary>
        /// The first gate slot a shot from <paramref name="eye"/> along <paramref name="dir"/> meets, before
        /// <paramref name="maxRange"/>. <paramref name="foot"/> is the gate's foot, already made near the shooter.
        /// </summary>
        public static bool GateOnShot(float3 foot, int slots, float3 eye, float3 dir, float maxRange, out int slot, out float at)
        {
            slot = -1;
            at = maxRange;
            float r = GateSize * HitScale;
            for (int k = 0; k < slots; k++)
            {
                var c = foot + GateSlot(k, slots);
                Faces(c, GateFace, r, eye, dir, k, ref slot, ref at);
            }
            return slot >= 0;
        }

        /// <summary>As <see cref="GateOnShot"/>, for the raid door's key slots (any color).</summary>
        public static bool DoorOnShot(float3 foot, float3 eye, float3 dir, float maxRange, out EssenceColor color, out int slot, out float at)
        {
            slot = -1;
            at = maxRange;
            color = default;
            foreach (var c in RaidDoor.KeyColors)
            {
                int n = RaidKey.Cost(c);
                for (int k = 0; k < n; k++)
                {
                    var local = DoorSlot(c, k, out float size);
                    if (Faces(foot + new float3(local.x, local.y, 0f), local.z, size * HitScale, eye, dir, k, ref slot, ref at)) color = c;
                }
            }
            return slot >= 0;
        }

        /// <summary>Tests the slot on both faces (front and back) and keeps it if it's the nearest so far (returns true then).</summary>
        private static bool Faces(float3 middle, float face, float radius, float3 eye, float3 dir, int k, ref int slot, ref float at)
        {
            bool nearer = false;
            for (int f = -1; f <= 1; f += 2)
            {
                var c = middle + new float3(0f, 0f, face * f);
                if (RaySphere(eye, dir, c, radius, at, out float d))
                {
                    at = d;
                    slot = k;
                    nearer = true;
                }
            }
            return nearer;
        }

        private static bool RaySphere(float3 origin, float3 dir, float3 center, float radius, float maxRange, out float at)
        {
            at = 0f;
            var oc = center - origin;
            float along = math.dot(oc, dir);
            if (along < 0f || along - radius > maxRange) return false;
            float miss = math.lengthsq(oc) - along * along;
            if (miss > radius * radius) return false;
            at = along - math.sqrt(radius * radius - miss);
            return at < maxRange;
        }
    }
}
