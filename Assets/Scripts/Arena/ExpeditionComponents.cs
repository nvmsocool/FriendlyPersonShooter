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
        [GhostField] public int Carried;
        [GhostField] public int Banked;
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
