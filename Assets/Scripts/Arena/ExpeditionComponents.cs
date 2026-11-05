using FriendlyPersonShooter.Core.Expedition;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>A player's expedition progress, sent only to that player for their HUD and bots.</summary>
    [GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]
    public struct ExpeditionStatus : IComponentData
    {
        /// <summary>The gate's color (Yellow): carried, and in the party bank.</summary>
        [GhostField] public int Carried;
        [GhostField] public int Banked;
        /// <summary>The raid key's colors, carried and banked.</summary>
        [GhostField] public int CarriedRed;
        [GhostField] public int BankedRed;
        [GhostField] public int CarriedGreen;
        [GhostField] public int BankedGreen;
        /// <summary>Essence the gate costs.</summary>
        [GhostField] public int Goal;
        [GhostField] public ExpeditionPhase Phase;
        [GhostField] public bool HasTarget;
        /// <summary>Where the current objective is (world x, z).</summary>
        [GhostField(Quantization = 10)] public float2 Target;
        [GhostField(Quantization = 10)] public float Elapsed;
        [GhostField] public bool Complete;
        [GhostField] public int Kudos;
        /// <summary>A Yellow bloom is running within reach.</summary>
        [GhostField] public bool BloomNear;
        [GhostField] public FixedString128Bytes Objective;
        /// <summary>Where the player is, by landmark ("near the Broken Tower in zone (3,4)").</summary>
        [GhostField] public FixedString128Bytes Location;

        /// <summary>Raid door (MVP step 6): its phase, the key so far, and the countdown or time left.</summary>
        [GhostField] public Core.Expedition.RaidPhase RaidPhase;
        [GhostField] public bool InRaid;
        [GhostField] public int KeyRed;
        [GhostField] public int KeyGreen;
        [GhostField(Quantization = 10)] public float RaidTimer;

        /// <summary>
        /// Inside a raid run: the stage (0-based; StageCount - 1 is the boss), the room, the run's modifier,
        /// the room's state packed per kind (Color Locks: <see cref="Core.Expedition.ColorLocks.Pack"/>;
        /// plates: count | held mask &lt;&lt; 4; gauntlet: furthest stone; storm: kills | target &lt;&lt; 8),
        /// its progress, its clock (gauntlet sweepers turn on it), a room timer (storm), and the portal:
        /// -2 shut, -1 open, else seconds until everyone moves on.
        /// </summary>
        [GhostField] public int RaidStage;
        [GhostField] public int RaidStageCount;
        [GhostField] public Core.Expedition.RaidRoomKind RaidRoom;
        [GhostField] public Core.Expedition.RaidModifier RaidModifier;
        [GhostField] public int RaidRoomData;
        [GhostField(Quantization = 100)] public float RaidRoomProgress;
        [GhostField(Quantization = 100)] public float RaidRoomClock;
        [GhostField(Quantization = 10)] public float RaidRoomTimer;
        [GhostField(Quantization = 10)] public float RaidPortal;
        /// <summary>Color Locks: you lit the last crystal, so someone else must take the next.</summary>
        [GhostField] public bool RaidYouHitLast;

        /// <summary>The Colossus (world boss) is up: where it hovers (world x, z) and its health share.</summary>
        [GhostField] public bool WorldBoss;
        [GhostField(Quantization = 10)] public float2 WorldBossAt;
        [GhostField(Quantization = 100)] public float WorldBossHealth;
    }

    /// <summary>Essence dropped by a player who bled out. Walk over it to send it back to them.</summary>
    public struct ArenaCache : IComponentData
    {
        [GhostField] public int OwnerNetworkId;
        [GhostField] public int Amount;
    }

    /// <summary>Singleton holding the cache ghost prefab built in code.</summary>
    public struct ArenaCachePrefab : IComponentData
    {
        public Entity Value;
    }
}
