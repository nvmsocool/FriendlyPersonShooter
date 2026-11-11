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
        /// <summary>Each link's payload, packed (Core.Combat.ChainTracker.PayloadAt): the HUD's chain colours.</summary>
        [GhostField] public uint ChainPayloads;
        /// <summary>Haste + Might combo: Empower doubled.</summary>
        [GhostField] public bool Blitz;
        /// <summary>Frenzy: fire-rate (and reload-speed) multiplier, 0 read as 1. Read by the client to draw its own shots.</summary>
        [GhostField(Quantization = 100)] public float FireRate;
        [GhostField(Quantization = 10)] public float FrenzyRemaining;
        /// <summary>Ward: shield points left and seconds left.</summary>
        [GhostField(Quantization = 10)] public float Ward;
        [GhostField(Quantization = 10)] public float WardRemaining;
        /// <summary>Focus: weak-point radius bonus (0.35 = +35%) and seconds left.</summary>
        [GhostField(Quantization = 100)] public float Focus;
        [GhostField(Quantization = 10)] public float FocusRemaining;
        /// <summary>Combos active on this player (Blitz, Overdrive, Deadeye, Bastion).</summary>
        [GhostField] public ComboKind Combos;
        /// <summary>Raid Low Gravity: players' gravity multiplier (0 is read as 1). Read by predicted movement.</summary>
        [GhostField(Quantization = 100)] public float GravityMultiplier;
        /// <summary>Raid Lanterns: seconds this player still glows (an ally's payload lit them).</summary>
        [GhostField(Quantization = 10)] public float Glow;
    }

    /// <summary>
    /// A player's gun (packed Core.Arsenal.Gun; GunB stays empty, one gun only) and its payload capsule.
    /// <see cref="Payload"/> is the payload of the gun in hand (None when its payload slot is empty).
    /// </summary>
    public struct ArenaLoadout : IComponentData
    {
        [GhostField] public PayloadKind Payload;
        [GhostField] public ulong GunA;
        [GhostField] public ulong GunB;
        [GhostField] public byte Active;
        /// <summary>Payload charge left in the capsule.</summary>
        [GhostField] public int Charge;
        /// <summary>A Core.Arsenal.CapsuleState: on the gun, flying, or regrowing.</summary>
        [GhostField] public byte Capsule;
        /// <summary>How grown the capsule on the gun is, 0..1 (1 when seated).</summary>
        [GhostField(Quantization = 100)] public float Grown;
    }

    /// <summary>
    /// Server-to-everyone: a player's empty payload capsule was flung into the air (draw it flying), or it was shot
    /// (<see cref="Caught"/>: stop drawing it).
    /// </summary>
    public struct CapsuleFx : IRpcCommand
    {
        public int ShooterNetworkId;
        public float3 From;
        public float3 Velocity;
        public float Radius;
        public PayloadKind Payload;
        public byte Caught;
    }

    /// <summary>
    /// A player's shooting gallery (Core.Gallery), sent only to them. The targets' paths follow from the kind, layer,
    /// seed and start tick, so only progress is sent; the screen works out where the targets are. The last gallery
    /// stays here after it ends (with its outcome) so the screen can show the result. Also: the player's reward
    /// chest, and the loot seed their screen needs to preview what a box holds.
    /// </summary>
    [GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]
    public struct ArenaGallery : IComponentData
    {
        /// <summary>A Core.Gallery.GalleryKind; 0 = none yet.</summary>
        [GhostField] public byte Kind;
        /// <summary>Changes with every new gallery.</summary>
        [GhostField] public byte Serial;
        [GhostField] public byte Layer;
        /// <summary>A Core.Gallery.GalleryOutcome.</summary>
        [GhostField] public byte Outcome;
        [GhostField] public short Targets;
        [GhostField] public short Chosen;
        [GhostField] public uint Seed;
        /// <summary>Item cards: the seed for their target colours (<see cref="Core.Gallery.GalleryRound.ColorOrder"/>).</summary>
        [GhostField] public uint ColorSeed;
        [GhostField] public int StartTick;
        [GhostField] public int EndTick;
        /// <summary>Sequence: the tick the current target came up.</summary>
        [GhostField] public int NextTick;
        /// <summary>The aim when it opened (degrees); targets sit at offsets from it.</summary>
        [GhostField(Quantization = 100)] public float Yaw0;
        [GhostField(Quantization = 100)] public float Pitch0;
        [GhostField] public int Hits;
        [GhostField] public short HitCount;
        [GhostField] public short Next;
        /// <summary>Item cards (packed loot items, 0 = none).</summary>
        [GhostField] public ulong Choice0;
        [GhostField] public ulong Choice1;
        [GhostField] public ulong Choice2;
        /// <summary>Box galleries: the box; essence galleries: the essence colour.</summary>
        [GhostField] public int Subject;

        /// <summary>The player's boss reward chest, waiting to be shot (one at a time).</summary>
        [GhostField] public byte ChestOut;
        [GhostField(Quantization = 100)] public float3 ChestPos;
        [GhostField] public uint ChestSeed;
        [GhostField] public ulong Chest0;
        [GhostField] public ulong Chest1;
        [GhostField] public ulong Chest2;
        /// <summary>The host's loot seed, so the screen can preview a box's three items (the same roll the host makes).</summary>
        [GhostField] public uint LootSeed;
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

    /// <summary>Essence from a kill, waiting for its owner to shoot it (only they see it).</summary>
    public struct ArenaEssenceOrb : IComponentData
    {
        [GhostField] public int Owner;
        [GhostField] public byte Color;
        [GhostField] public int Amount;
    }

    /// <summary>Singleton holding the essence orb ghost prefab built in code.</summary>
    public struct ArenaEssenceOrbPrefab : IComponentData
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
        /// <summary>Frenzy, Ward and Focus debuffs, and combos (Pinned freezes it; Shatter broke its armor).</summary>
        [GhostField] public bool Jammed;
        [GhostField] public bool Brittle;
        [GhostField] public bool Marked;
        [GhostField] public bool Frozen;
        [GhostField] public bool Shattered;
        [GhostField] public bool Tagged;
        /// <summary>Raid boss: which way its weak plate faces (degrees), and whether the plate is open or about to open.</summary>
        [GhostField(Quantization = 10)] public float Yaw;
        [GhostField] public bool PlateOpen;
        [GhostField] public bool PlateWarn;
        /// <summary>The Colossus, the open-world boss: a Bulwark drawn and hit at a bigger size.</summary>
        [GhostField] public bool World;
        /// <summary>A raid boss (a Bulwark, the Twins or the Weaver), drawn big with its health on the HUD.</summary>
        [GhostField] public bool Boss;
        /// <summary>Difficulty tier (1 to 3, see Core.Combat.EnemyTiers): drawn smaller on higher tiers. 0 reads as 1.</summary>
        [GhostField] public byte Tier;
        /// <summary>An elite (two families mixed): drawn bigger, with an aura in its second family's color.</summary>
        [GhostField] public EliteKind Elite;
        /// <summary>A Diver's nose pitch (degrees, down is positive), so a dive reads from the ground.</summary>
        [GhostField(Quantization = 10)] public float Pitch;
        /// <summary>Seed for how this enemy looks (see Core.Avatars.BugLook), so every client draws the same bug.</summary>
        [GhostField] public uint LookSeed;
        /// <summary>The enemy's id, so a player's own hit test can name what it hit (see LocalShots).</summary>
        [GhostField] public int Id;
        /// <summary>Weak points grow by this (Mark), for the player's own hit test.</summary>
        [GhostField(Quantization = 100)] public float WeakScale;
    }

    /// <summary>A downed player's soul fragment. Shoot it to help revive them.</summary>
    public struct ArenaFragment : IComponentData
    {
        [GhostField] public int OwnerNetworkId;
        [GhostField(Quantization = 100)] public float Opacity;
        /// <summary>The fragment's hit target id, for a player's own hit test.</summary>
        [GhostField] public int TargetId;
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
        /// <summary>
        /// Layers: Player started the group's move to layer Other; Value = seconds until it goes. Value 0 =
        /// the move was called off (a raid started).
        /// </summary>
        LayerTravel = 26,
        /// <summary>Layers: the group arrived on layer Value, from layer Other.</summary>
        LayerArrived = 27,
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

    /// <summary>Server-to-everyone: a player pinged a spot (middle mouse). Clients mark it in the world and on the minimap.</summary>
    public struct PingFx : IRpcCommand
    {
        public const byte Spot = 0;
        public const byte Enemy = 1;
        /// <summary>Pointed at the sky: a spot far along the line.</summary>
        public const byte Far = 2;

        public int PlayerNetworkId;
        public float3 Position;
        public byte Kind;
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
