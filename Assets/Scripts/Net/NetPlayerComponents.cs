using Unity.Entities;
using Unity.NetCode;

namespace FriendlyPersonShooter.Net
{
    /// <summary>Marks a networked player cube.</summary>
    public struct NetPlayer : IComponentData
    {
    }

    /// <summary>Movement input sent from a client to the server every tick. Values are -1, 0 or 1.</summary>
    public struct PlayerInput : IInputComponentData
    {
        public int Horizontal;
        public int Vertical;
    }

    /// <summary>Singleton holding the player ghost prefab built in code (same in every world).</summary>
    public struct PlayerPrefab : IComponentData
    {
        public Entity Value;
    }

    /// <summary>Client-to-server request to start receiving snapshots and get a player.</summary>
    public struct GoInGameRequest : IRpcCommand
    {
    }
}
