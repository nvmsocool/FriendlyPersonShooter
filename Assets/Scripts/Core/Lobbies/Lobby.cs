using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.Lobbies
{
    /// <summary>A set of players who can see each other. Lobbies only merge within the same layer and seed.</summary>
    public sealed class Lobby
    {
        public int Id { get; }

        /// <summary>Difficulty layer (1 = easiest).</summary>
        public int Layer { get; }

        /// <summary>World seed the lobby's players are on.</summary>
        public int Seed { get; }

        public List<LobbyPlayer> Players { get; } = new List<LobbyPlayer>();

        public Lobby(int id, int layer, int seed)
        {
            Id = id;
            Layer = layer;
            Seed = seed;
        }

        public Lobby(int id, int layer, int seed, IEnumerable<LobbyPlayer> players)
            : this(id, layer, seed)
        {
            Players.AddRange(players);
        }
    }
}
