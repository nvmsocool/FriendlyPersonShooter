using Unity.Entities;
using Unity.NetCode;

namespace FriendlyPersonShooter.Net
{
    /// <summary>Singleton holding the player ghost prefab built in code (same in every world).</summary>
    public struct PlayerPrefab : IComponentData
    {
        public Entity Value;
    }

    /// <summary>Client-to-server request to start receiving snapshots and get a player.</summary>
    public struct GoInGameRequest : IRpcCommand
    {
    }

    /// <summary>Bot ally client worlds only: marks the world as a bot ally rather than the human player.</summary>
    public struct ArenaBotSeat : IComponentData
    {
        public int Index;
    }
}
