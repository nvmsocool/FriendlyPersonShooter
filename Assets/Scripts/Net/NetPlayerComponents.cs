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
        /// <summary>Test account, or 0 for an anonymous player who always spawns somewhere random.</summary>
        public int AccountId;
        /// <summary>Party the player belongs to, or 0. Party members spawn next to each other.</summary>
        public int GroupId;
        /// <summary>1 for a test bot. Bots get <see cref="BotStatus"/> messages, since they don't receive ghosts.</summary>
        public byte IsBot;
    }

    /// <summary>Client-to-server: the player joined or left a party mid-session (0 = none).</summary>
    public struct SetGroupRequest : IRpcCommand
    {
        public int GroupId;
    }

    /// <summary>
    /// Server-to-bot: where the bot's player is and which lobby it's in. Thin clients don't receive
    /// ghosts, so this test-only message is how a bot sees itself.
    /// </summary>
    public struct BotStatus : IRpcCommand
    {
        public float X;
        public float Z;
        public int LobbyId;
    }

    /// <summary>Bot-to-server, test only: move my player to this point (Chaos bots jumping between zones).</summary>
    public struct TeleportRequest : IRpcCommand
    {
        public float X;
        public float Z;
    }

    /// <summary>Server only: who owns a player entity.</summary>
    public struct PlayerAccount : IComponentData
    {
        public int AccountId;
        public int GroupId;
        public bool IsBot;
    }

    /// <summary>Bot worlds only: which simulated account this bot client plays.</summary>
    public struct BotSeat : IComponentData
    {
        public int Index;
    }
}
