using FriendlyPersonShooter.Core.Combat;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>Hit shapes for players. Gun numbers live in Core.Arsenal (frames and mods).</summary>
    public static class ArenaWeapon
    {
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
        /// <summary>Raid Low Gravity: players' gravity multiplier (0 is read as 1). Read by predicted movement.</summary>
        [GhostField(Quantization = 100)] public float GravityMultiplier;
        /// <summary>Raid Lanterns: seconds this player still glows (an ally's payload lit them).</summary>
        [GhostField(Quantization = 10)] public float Glow;
    }

    /// <summary>
    /// A player's two guns (packed Core.Arsenal.Gun values), which one is in hand, and its magazine.
    /// <see cref="Payload"/> is the payload of the gun in hand (None when its payload slot is empty).
    /// </summary>
    public struct ArenaLoadout : IComponentData
    {
        [GhostField] public PayloadKind Payload;
        [GhostField] public ulong GunA;
        [GhostField] public ulong GunB;
        [GhostField] public byte Active;
        [GhostField] public int Ammo;
        /// <summary>Reload progress 0..1; 1 when not reloading.</summary>
        [GhostField(Quantization = 100)] public float Reload;
    }

    /// <summary>A gun or mod lying on the ground. Owner 0 = anyone can take it; otherwise only that player sees and takes it.</summary>
    public struct ArenaGroundItem : IComponentData
    {
        [GhostField] public ulong Item;
        [GhostField] public int Owner;
    }

    /// <summary>Singleton holding the ground item ghost prefab built in code.</summary>
    public struct ArenaGroundItemPrefab : IComponentData
    {
        public Entity Value;
    }

    /// <summary>Server-to-opener: you opened this loot box (it stays open for you this session).</summary>
    public struct LootBoxOpened : IRpcCommand
    {
        public int BoxId;
    }

    /// <summary>Bot-to-server: give me my role's kit (a gun with this payload).</summary>
    public struct BotKitRequest : IRpcCommand
    {
        public PayloadKind Payload;
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
        /// <summary>Raid boss: which way its weak plate faces (degrees), and whether the plate is open or about to open.</summary>
        [GhostField(Quantization = 10)] public float Yaw;
        [GhostField] public bool PlateOpen;
        [GhostField] public bool PlateWarn;
        /// <summary>The Colossus, the open-world boss: a Bulwark drawn and hit at a bigger size.</summary>
        [GhostField] public bool World;
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
        /// <summary>Raid: the key is full. Value = seconds until whoever stands at the door goes in.</summary>
        RaidGathering = 9,
        /// <summary>Raid: a group went in. Value = how many.</summary>
        RaidStarted = 10,
        /// <summary>Raid: the boss fell. Value = seconds the run took.</summary>
        RaidCleared = 11,
        /// <summary>Raid: the run failed. Value = (int)RaidOutcome.</summary>
        RaidFailed = 12,
        /// <summary>Raid: the boss called in adds. Value = the wave.</summary>
        RaidAdds = 13,
        /// <summary>Raid: Player pledged essence to the key. Value = how much.</summary>
        RaidPledged = 14,
        /// <summary>Expedition: the gate shut. Value = what the next opening costs.</summary>
        GateClosed = 15,
        /// <summary>The Colossus landed. Value = the landmark id it landed by.</summary>
        WorldBossLanded = 16,
        /// <summary>The Colossus fell. Value = how many players hit it.</summary>
        WorldBossDown = 17,
        /// <summary>Raid run: a stage began. Value = (int)RaidRoomKind, Player = stage index, Other = (int)RaidModifier.</summary>
        RaidStage = 18,
        /// <summary>Raid run: a room was solved. Value = (int)RaidRoomKind, Player = XP paid, Other = Yellow paid.</summary>
        RaidRoomSolved = 19,
        /// <summary>
        /// Raid run: a room set the group back. Value = (int)RaidRoomKind, Other = why (0 wrong crystal,
        /// 1 too slow, 2 storm timer, 3 wrong rune, 4 runes too slow, 5 the core broke), Player = who hit
        /// the wrong crystal or rune.
        /// </summary>
        RaidSetback = 20,
        /// <summary>The Twins: one fell. Value = seconds the group has to kill the other.</summary>
        TwinDown = 21,
        /// <summary>The Twins: too slow, the fallen twin is back.</summary>
        TwinRevived = 22,
        /// <summary>The Weaver tied players together. Value = how many threads.</summary>
        TetherTied = 23,
        /// <summary>A thread was left too long and snapped. Player and Other = its ends (Other 0 = the Weaver).</summary>
        TetherSnapped = 24,
        /// <summary>Player shot a thread apart.</summary>
        TetherCut = 25,
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
        /// <summary>The bolt that hit (so clients stop drawing it), or 0 for a melee hit or a shockwave.</summary>
        public int BoltId;
        public EnemyFamily Family;
        public float Damage;
        public float3 From;
        public float3 To;
    }

    /// <summary>Server-to-everyone: an enemy fired a bolt. Clients fly it along the same line for <see cref="Life"/> seconds.</summary>
    public struct EnemyBoltFx : IRpcCommand
    {
        public int Id;
        public EnemyFamily Family;
        public float3 From;
        public float3 Velocity;
        public float Life;
        public float Radius;
    }

    /// <summary>Server-to-everyone: the Colossus slammed; a ring runs out along the ground from Center.</summary>
    public struct ShockwaveFx : IRpcCommand
    {
        public float3 Center;
        public float Speed;
        public float Range;
        public float Width;
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
        /// <summary>0 for a ricochet (everyone draws it, the shooter too).</summary>
        public int ShooterNetworkId;
        public float3 From;
        public float3 To;
        public PayloadKind Payload;
        /// <summary>The gun's frame (Core.Arsenal.FrameKind): how to draw it (beam, launcher shell).</summary>
        public byte Frame;
    }
}
