using FriendlyPersonShooter.Core.Combat;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>Weapon numbers for the arena. One automatic rifle for everyone in step 2.</summary>
    public static class ArenaWeapon
    {
        public const float FireInterval = 0.12f;
        public const float BaseDamage = 10f;
        public const float Range = 80f;
        /// <summary>Players are hit as a sphere around the chest: easy enough to buff a dodging ally.</summary>
        public const float PlayerHitRadius = 0.6f;
        public const float PlayerHitHeight = 1.0f;
    }

    /// <summary>A player's health and shield, replicated for HUDs and health bars.</summary>
    public struct ArenaVitals : IComponentData
    {
        [GhostField(Quantization = 10)] public float Health;
        [GhostField(Quantization = 10)] public float MaxHealth;
        [GhostField(Quantization = 10)] public float Shield;
        /// <summary>Seconds until a downed player respawns; 0 while alive.</summary>
        [GhostField(Quantization = 10)] public float RespawnIn;
    }

    /// <summary>Buffs on a player, replicated so the HUD can show them.</summary>
    public struct ArenaBuffs : IComponentData
    {
        /// <summary>Might's buff: bonus damage as a fraction (0.25 = +25%).</summary>
        [GhostField(Quantization = 100)] public float Empower;
        [GhostField(Quantization = 10)] public float EmpowerRemaining;
        [GhostField(Quantization = 10)] public float ShieldRemaining;
    }

    /// <summary>The payload a player's gun carries.</summary>
    public struct ArenaLoadout : IComponentData
    {
        [GhostField] public PayloadKind Payload;
    }

    /// <summary>An enemy, replicated to everyone.</summary>
    public struct ArenaEnemy : IComponentData
    {
        [GhostField] public EnemyFamily Family;
        [GhostField(Quantization = 10)] public float Health;
        [GhostField(Quantization = 10)] public float MaxHealth;
        /// <summary>False while a Wisp can't be hit (between blinks).</summary>
        [GhostField] public bool Hittable;
        /// <summary>True while the enemy is winding up an attack (shown as a glow).</summary>
        [GhostField] public bool Telegraph;
        [GhostField] public bool Withered;
        [GhostField] public bool Exposed;
    }

    /// <summary>Singleton holding the enemy ghost prefab built in code.</summary>
    public struct ArenaEnemyPrefab : IComponentData
    {
        public Entity Value;
    }

    /// <summary>Server-to-shooter: one of your shots landed (for hit markers and numbers).</summary>
    public struct HitConfirm : IRpcCommand
    {
        public byte TargetIsPlayer;
        public byte WeakPoint;
        public byte Killed;
        public byte Refused;
        public float Damage;
        public float Healed;
        public float3 Point;
    }

    /// <summary>Server-to-everyone: someone fired; draw a tracer from their gun to where it stopped.</summary>
    public struct ShotFx : IRpcCommand
    {
        public int ShooterNetworkId;
        public float3 From;
        public float3 To;
        public PayloadKind Payload;
    }
}
