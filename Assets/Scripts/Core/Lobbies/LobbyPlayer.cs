using System.Collections.Generic;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Lobbies
{
    /// <summary>What the lobby system needs to know about one player.</summary>
    public sealed class LobbyPlayer
    {
        public int Id { get; }

        /// <summary>Zone the player is in right now.</summary>
        public ZoneCoord Zone { get; set; }

        /// <summary>Group the player chose to join, or 0 for none. Group members always share a lobby.</summary>
        public int GroupId { get; set; }

        /// <summary>Zones this player has been in during the current world seed.</summary>
        public HashSet<ZoneCoord> VisitedZones { get; } = new HashSet<ZoneCoord>();

        /// <summary>Players this player has blocked. Blocked pairs never share a lobby.</summary>
        public HashSet<int> BlockedPlayers { get; } = new HashSet<int>();

        public LobbyPlayer(int id, ZoneCoord zone, int groupId = 0)
        {
            Id = id;
            Zone = zone;
            GroupId = groupId;
            VisitedZones.Add(zone);
        }

        /// <summary>Moves the player and remembers the zone as visited.</summary>
        public void MoveTo(ZoneCoord zone)
        {
            Zone = zone;
            VisitedZones.Add(zone);
        }
    }
}
