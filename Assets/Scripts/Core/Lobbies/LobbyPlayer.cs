using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Lobbies
{
    /// <summary>What the lobby system needs to know about one player.</summary>
    public sealed class LobbyPlayer
    {
        public int Id { get; }

        /// <summary>Zone the player is in right now.</summary>
        public ZoneCoord Zone { get; set; }

        /// <summary>World position (metres), when the game knows it. Used to tell who is heading toward whom.</summary>
        public double X { get; set; }
        public double Z { get; set; }

        /// <summary>Recent average velocity (metres per second) on the ground plane; zero when unknown or standing.</summary>
        public double VelocityX { get; set; }
        public double VelocityZ { get; set; }

        /// <summary>Group the player chose to join, or 0 for none. Group members always share a lobby.</summary>
        public int GroupId { get; set; }

        /// <summary>Zones this player has been in during the current world seed.</summary>
        public HashSet<ZoneCoord> VisitedZones { get; } = new HashSet<ZoneCoord>();

        /// <summary>Players this player has blocked. Blocked pairs never share a lobby.</summary>
        public HashSet<int> BlockedPlayers { get; } = new HashSet<int>();

        /// <summary>
        /// Essence the player still needs, per color (for example a gate's cost minus what they have
        /// banked). Empty when unknown. Read by the matchmaker's complementary-needs weight.
        /// </summary>
        public EssenceAmounts Wants { get; } = new EssenceAmounts();

        /// <summary>
        /// Essence the player holds beyond what they need and could gift, per color. Empty when unknown.
        /// </summary>
        public EssenceAmounts Spare { get; } = new EssenceAmounts();

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
