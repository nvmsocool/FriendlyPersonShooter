using System;
using System.Collections.Generic;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Bots
{
    /// <summary>What a simulated player is doing right now.</summary>
    public enum BotActivity
    {
        Offline,
        /// <summary>Walking toward a zone they haven't visited.</summary>
        Explore,
        /// <summary>Milling around one zone, as if fighting enemies there.</summary>
        Hunt,
        /// <summary>Travelling to and waiting at a gathering point (a gate or raid door).</summary>
        Hotspot,
        /// <summary>Standing still: away from keyboard, in a menu, chatting.</summary>
        Afk,
        /// <summary>A party member staying near their leader.</summary>
        Follow,
    }

    /// <summary>A party of friends or teamed-up strangers. The first member leads.</summary>
    public sealed class BotParty
    {
        public int Id { get; }
        public List<BotAccount> Members { get; } = new List<BotAccount>();
        public BotAccount Leader => Members.Count > 0 ? Members[0] : null;

        public BotParty(int id)
        {
            Id = id;
        }
    }

    /// <summary>One simulated player account, tied to one bot client (a seat).</summary>
    public sealed class BotAccount
    {
        public int Seat { get; }
        public int AccountId { get; }

        /// <summary>Seats of this account's friends. Friendships go both ways.</summary>
        public List<int> Friends { get; } = new List<int>();

        /// <summary>True while the player wants to be connected and in game.</summary>
        public bool WantsOnline { get; internal set; }

        /// <summary>Real time this player's session ends.</summary>
        public double SessionEnd { get; internal set; }

        /// <summary>Real time a scheduled login happens, or -1.</summary>
        public double LoginAt { get; internal set; } = -1;

        /// <summary>When a scheduled login was brought on by a friend, that friend.</summary>
        public BotAccount LoginHost { get; internal set; }

        /// <summary>Real time a dropped connection comes back, or -1.</summary>
        public double ReconnectAt { get; internal set; } = -1;

        public BotParty Party { get; internal set; }
        public int PartyId => Party != null ? Party.Id : 0;
        public bool IsPartyLeader => Party != null && Party.Leader == this;

        /// <summary>Whether the server has told this bot where it is since it last logged in.</summary>
        public bool HasPosition { get; internal set; }
        public double X { get; internal set; }
        public double Z { get; internal set; }
        public int LobbyId { get; internal set; }

        public BotActivity Activity { get; internal set; } = BotActivity.Offline;
        internal double ActivityEnd;
        internal bool HasTarget;
        internal double TargetX, TargetZ;
        /// <summary>Centre of the area a hunting or waiting player stays in.</summary>
        internal double CenterX, CenterZ;
        internal double PauseUntil;
        internal double NextDecision;
        internal double LastSteer = -1;
        internal int MoveH, MoveV;
        internal readonly Random Random;

        /// <summary>Zones visited during this session's world seed.</summary>
        public HashSet<ZoneCoord> Visited { get; } = new HashSet<ZoneCoord>();

        public BotAccount(int seat, int accountId, int seed)
        {
            Seat = seat;
            AccountId = accountId;
            Random = new Random(seed);
        }

        /// <summary>Online, or about to be: logging in, or reconnecting after a drop.</summary>
        public bool IsCommitted => WantsOnline || LoginAt >= 0 || ReconnectAt >= 0;
    }

    /// <summary>The daily player-count curve.</summary>
    public static class PopulationCurve
    {
        /// <summary>Target share of accounts online at a game hour: lowest 12 hours before the peak.</summary>
        public static double TargetOnline(double hour, BotSimSettings s)
        {
            double phase = 2.0 * Math.PI * (hour - s.PeakHour) / 24.0;
            return s.TroughOnline + (s.PeakOnline - s.TroughOnline) * (1.0 + Math.Cos(phase)) / 2.0;
        }

        /// <summary>
        /// Logins per game hour, per offline account, that hold the online share steady at
        /// <paramref name="target"/>: logins in balance logouts when rate * offline = online / session.
        /// </summary>
        public static double LoginRatePerHour(double target, double meanSessionHours)
        {
            if (target <= 0) return 0;
            if (target >= 1) return double.PositiveInfinity;
            return target / ((1.0 - target) * meanSessionHours);
        }
    }
}
