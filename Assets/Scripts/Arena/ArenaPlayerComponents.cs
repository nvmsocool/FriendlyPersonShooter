using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>Tunable numbers for the arena (MVP step 2).</summary>
    public static class ArenaSettings
    {
        /// <summary>Side length of the square arena floor, in metres. The centre is at the origin.</summary>
        public const float Size = 50f;
        public const float HalfSize = Size * 0.5f;
        public const float MoveSpeed = 6.5f;
        public const float JumpSpeed = 6f;
        public const float Gravity = 18f;
        /// <summary>Eye height above the feet.</summary>
        public const float EyeHeight = 1.6f;
        public const float BodyRadius = 0.4f;
        public const float BodyHeight = 1.8f;

        public static float3 Forward(float yawDegrees, float pitchDegrees)
        {
            float yaw = math.radians(yawDegrees);
            float pitch = math.radians(pitchDegrees);
            return new float3(math.sin(yaw) * math.cos(pitch), math.sin(pitch), math.cos(yaw) * math.cos(pitch));
        }
    }

    /// <summary>Marks an arena player (human or bot ally).</summary>
    public struct ArenaPlayer : IComponentData
    {
    }

    /// <summary>
    /// What a player sends every tick: movement relative to where they look, where they look
    /// (degrees, yaw 0 = +Z), and their buttons.
    /// </summary>
    public struct ArenaInput : IInputComponentData
    {
        public int Horizontal;
        public int Vertical;
        public float Yaw;
        public float Pitch;
        public InputEvent Jump;
        /// <summary>1 while the fire button is held.</summary>
        public int Fire;
        public InputEvent SwapPayload;
    }

    /// <summary>Where a player looks, replicated so others can see it.</summary>
    public struct ArenaLook : IComponentData
    {
        [GhostField(Quantization = 100)] public float Yaw;
        [GhostField(Quantization = 100)] public float Pitch;
    }

    /// <summary>Jumping and falling.</summary>
    public struct ArenaMotion : IComponentData
    {
        [GhostField(Quantization = 1000)] public float VerticalSpeed;
        [GhostField] public bool Grounded;
    }
}
