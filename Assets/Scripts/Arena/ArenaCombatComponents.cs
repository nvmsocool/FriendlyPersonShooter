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
        /// <summary>Seconds of bleed-out left while downed; 0 while up.</summary>
        [GhostField(Quantization = 10)] public float RespawnIn;
        /// <summary>Revive progress while downed, 0..1 (soul fragments shot so far).</summary>
        [GhostField(Quantization = 100)] public float ReviveProgress;
    }

    /// <summary>Buffs on a player, replicated so the HUD can show them.</summary>
    public struct ArenaBuffs : IComponentData
    {
        /// <summary>Might's buff: bonus damage as a fraction (0.25 = +25%).</summary>
        [GhostField(Quantization = 100)] public float Empower;
        [GhostField(Quantization = 10)] public float EmpowerRemaining;
        [GhostField(Quantization = 10)] public float ShieldRemaining;
        [GhostField(Quantization = 10)] public float HasteRemaining;
        /// <summary>Haste's movement multiplier (1 = normal). Read by predicted movement.</summary>
        [GhostField(Quantization = 100)] public float MoveMultiplier;
        [GhostField(Quantization = 100)] public float JumpMultiplier;
        /// <summary>Relayed hits left; the payload they carry is <see cref="RelayPayload"/>.</summary>
        [GhostField] public int RelayHits;
        [GhostField] public PayloadKind RelayPayload;
        /// <summary>Links on the chain this player holds (0 = none); their next enemy hit cashes it.</summary>
        [GhostField] public int ChainLinks;
        [GhostField(Quantization = 10)] public float ChainTimeLeft;
        /// <summary>Haste + Might combo: Empower doubled.</summary>
        [GhostField] public bool Blitz;
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
        [GhostField] public bool Slowed;
        [GhostField] public bool Tagged;
    }

    /// <summary>A downed player's soul fragment. Shoot it to help revive them.</summary>
    public struct ArenaFragment : IComponentData
    {
        [GhostField] public int OwnerNetworkId;
        [GhostField(Quantization = 100)] public float Opacity;
    }

    /// <summary>Singleton holding the soul fragment ghost prefab built in code.</summary>
    public struct ArenaFragmentPrefab : IComponentData
    {
        public Entity Value;
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
        /// <summary>Links on a chain this hit cashed (enemy hits) or passed on (ally hits); 0 = none.</summary>
        public byte ChainLinks;
        /// <summary>1 if this hit cashed a chain.</summary>
        public byte ChainCashed;
        public float ChainBonus;
        /// <summary>1 if this hit was a soul fragment; <see cref="ReviveProgress"/> is the owner's progress.</summary>
        public byte Fragment;
        public float ReviveProgress;
    }

    public enum AnnounceKind : byte
    {
        ComboDiscovered = 1,
        Revived = 2,
        BledOut = 3,
        ChainCashed = 4,
        /// <summary>Expedition: a player paid the gate. Value = essence paid.</summary>
        GateUnlocked = 5,
        /// <summary>Expedition: a player walked through the gate. Value = seconds taken.</summary>
        ExpeditionComplete = 6,
        /// <summary>Expedition: Other returned Player's cache. Value = essence returned.</summary>
        CacheReturned = 7,
        /// <summary>Player reached a new level. Value = the level.</summary>
        LevelUp = 8,
    }

    /// <summary>A player's level, XP and skills, replicated for the HUD and the skill tree.</summary>
    public struct ArenaProgress : IComponentData
    {
        [GhostField] public int Level;
        /// <summary>XP towards the next level, out of <see cref="XpNext"/>.</summary>
        [GhostField] public int Xp;
        [GhostField] public int XpNext;
        /// <summary>Unspent skill points.</summary>
        [GhostField] public int Points;
        /// <summary>Skill ranks, 2 bits per skill (see Core.Progression.PlayerProgress.RankIn).</summary>
        [GhostField] public int Ranks;
    }

    /// <summary>Client-to-server: spend a skill point on a skill.</summary>
    public struct SkillBuyRequest : IRpcCommand
    {
        public byte Skill;
    }

    /// <summary>Server-to-everyone: an enemy's attack hit a player (draw the bolt; the victim gets a warning).</summary>
    public struct EnemyAttackFx : IRpcCommand
    {
        public int VictimNetworkId;
        public EnemyFamily Family;
        public float Damage;
        public float3 From;
        public float3 To;
    }

    /// <summary>Server-to-everyone: something worth a message on screen (combo discovered, revive, big chain).</summary>
    public struct ArenaAnnounce : IRpcCommand
    {
        public AnnounceKind Kind;
        /// <summary>Combo kind, or chain links.</summary>
        public int Value;
        /// <summary>The player it's about (revived player, chain casher).</summary>
        public int PlayerNetworkId;
        /// <summary>Second player (the reviver who landed the last fragment).</summary>
        public int OtherNetworkId;
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
