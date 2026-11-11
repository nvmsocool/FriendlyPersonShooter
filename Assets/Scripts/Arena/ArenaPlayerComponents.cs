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
        /// <summary>Holding Shift while moving forward (not zoomed): no stamina, just speed.</summary>
        public const float SprintMultiplier = 1.5f;
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
        /// <summary>1 while aiming down the sights (right mouse).</summary>
        public int Zoom;
        /// <summary>1 while Shift is held.</summary>
        public int Sprint;
        /// <summary>"Reset position" from the Esc menu: the server moves the player to the nearest shrine.</summary>
        public InputEvent Unstuck;
        /// <summary>Middle mouse: ping what you're pointing at.</summary>
        public InputEvent Ping;
        /// <summary>
        /// How far between ticks the enemies this player saw were drawn (NetworkTime.InterpolationTickFraction).
        /// The server rewinds targets to exactly that moment, not just the whole tick, so small fast enemies
        /// (Mites) are where the player saw them.
        /// </summary>
        public float SeenFraction;

        /// <summary>
        /// The player's own hit test (see LocalShots): the tick these shot reports are for (0 = none), and for each of
        /// that tick's first shots what it hit (LocalShots.Miss, Enemy, EnemyWeak, Player, Fragment), its id, and how
        /// far along the shot it was (or where the shot stopped, for a miss).
        /// </summary>
        public int ShotTick;
        public int ShotKind0, ShotKind1, ShotKind2;
        public int ShotId0, ShotId1, ShotId2;
        public float ShotDist0, ShotDist1, ShotDist2;

        public void SetShot(int n, int kind, int id, float distance)
        {
            switch (n)
            {
                case 0: ShotKind0 = kind; ShotId0 = id; ShotDist0 = distance; break;
                case 1: ShotKind1 = kind; ShotId1 = id; ShotDist1 = distance; break;
                default: ShotKind2 = kind; ShotId2 = id; ShotDist2 = distance; break;
            }
        }

        public void GetShot(int n, out int kind, out int id, out float distance)
        {
            switch (n)
            {
                case 0: kind = ShotKind0; id = ShotId0; distance = ShotDist0; break;
                case 1: kind = ShotKind1; id = ShotId1; distance = ShotDist1; break;
                default: kind = ShotKind2; id = ShotId2; distance = ShotDist2; break;
            }
        }
    }

    /// <summary>
    /// The seed of a player's robot avatar (Core.Avatars.RobotLook). The server picks a random one when
    /// the player joins, so each load-in gets a different robot; 0 = not picked yet.
    /// </summary>
    public struct ArenaAvatar : IComponentData
    {
        [GhostField] public uint Seed;
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
