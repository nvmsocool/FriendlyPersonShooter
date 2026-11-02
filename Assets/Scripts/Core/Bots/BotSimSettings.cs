namespace FriendlyPersonShooter.Core.Bots
{
    /// <summary>
    /// Tunable numbers for simulated players. Durations are in game minutes and rates per game hour.
    /// The test world is compressed (a zone takes seconds to cross, not minutes), so the clock is
    /// compressed by the same factor: <see cref="TimeScale"/> game seconds pass per real second.
    /// </summary>
    public sealed class BotSimSettings
    {
        /// <summary>
        /// Game seconds per real second. 48 makes a 5-second crossing of a 30 m test zone take
        /// 4 game minutes, as the design's 3-5 minutes per zone. A game day then lasts 30 real minutes.
        /// </summary>
        public double TimeScale { get; set; } = 48;

        /// <summary>Game hour of day (0-24) when the run starts.</summary>
        public double StartHour { get; set; } = 12;

        // Daily population curve: share of all accounts online.
        public double TroughOnline { get; set; } = 0.2;
        public double PeakOnline { get; set; } = 0.7;
        public double PeakHour { get; set; } = 21;

        /// <summary>Real seconds over which the starting population logs in, so they don't all connect at once.</summary>
        public double PrewarmSeconds { get; set; } = 20;

        // Session length: log-normal around the median, clamped.
        public double MedianSessionMinutes { get; set; } = 50;
        public double SessionSpread { get; set; } = 0.8;
        public double MinSessionMinutes { get; set; } = 5;
        public double MaxSessionMinutes { get; set; } = 300;

        // Parties.
        /// <summary>Chance that a login brings offline friends along (they log in a few minutes later).</summary>
        public double PartyLoginChance { get; set; } = 0.3;
        /// <summary>Chance that a player who logs in while a friend is online joins that friend.</summary>
        public double JoinFriendChance { get; set; } = 0.5;
        /// <summary>Relative chance of a party of 2, 3 and 4 when friends are brought along.</summary>
        public double[] PartySizeWeights { get; set; } = { 0.6, 0.25, 0.15 };
        public int MaxPartySize { get; set; } = 4;
        public int FriendsPerAccount { get; set; } = 5;
        /// <summary>Brought-along friends log in within this many minutes of the first one.</summary>
        public double FriendLoginDelayMinutes { get; set; } = 3;
        /// <summary>Chance each member logs off soon after the party leader does.</summary>
        public double LeaveWithLeaderChance { get; set; } = 0.7;
        /// <summary>Rate at which a member quits the party but keeps playing.</summary>
        public double LeavePartyPerHour { get; set; } = 0.15;
        /// <summary>Rate at which two solo players in the same lobby and zone team up.</summary>
        public double StrangerPartyPerHour { get; set; } = 0.5;

        // Connection drops: the client crashes or loses network, then comes back.
        public double DropPerHour { get; set; } = 0.05;
        public double ReconnectMinMinutes { get; set; } = 1;
        public double ReconnectMaxMinutes { get; set; } = 5;

        // Behavior profiles (see the design doc's Test bots table). Everyone else plays normally.
        /// <summary>Share of accounts that are Groupers: they invite strangers and nearly always party.</summary>
        public double GrouperShare { get; set; } = 0.1;
        /// <summary>Groupers invite strangers this many times more often than other players.</summary>
        public double GrouperInviteMultiplier { get; set; } = 20;
        /// <summary>Share of accounts that are Chaos bots: frequent drops, quick reconnects, teleports.</summary>
        public double ChaosShare { get; set; } = 0.05;
        public double ChaosDropPerHour { get; set; } = 3;
        public double ChaosReconnectMinMinutes { get; set; } = 0.2;
        public double ChaosReconnectMaxMinutes { get; set; } = 1;
        /// <summary>Rate at which a solo Chaos bot jumps to a random point in the world.</summary>
        public double ChaosTeleportPerHour { get; set; } = 4;

        // What a solo player or party leader does, by relative weight.
        public double ExploreWeight { get; set; } = 0.45;
        public double HuntWeight { get; set; } = 0.33;
        public double HotspotWeight { get; set; } = 0.14;
        public double AfkWeight { get; set; } = 0.08;
        public double HuntMinMinutes { get; set; } = 5;
        public double HuntMaxMinutes { get; set; } = 25;
        public double HotspotMinMinutes { get; set; } = 5;
        public double HotspotMaxMinutes { get; set; } = 20;
        public double AfkMinMinutes { get; set; } = 2;
        public double AfkMaxMinutes { get; set; } = 10;

        /// <summary>Gathering points (stand-ins for gates and raid doors) per zone in the world.</summary>
        public double HotspotsPerZone { get; set; } = 1.0 / 40.0;

        // Movement, in metres and real seconds (the same units as the server).
        public double PlayerSpeed { get; set; } = 6;
        /// <summary>How far party followers keep from their leader.</summary>
        public double FollowDistance { get; set; } = 4;
        /// <summary>A party leader stops and waits while a member is farther than this.</summary>
        public double LeaderWaitDistance { get; set; } = 25;
        /// <summary>Real seconds between steering decisions, like a player's reaction time.</summary>
        public double DecisionInterval { get; set; } = 0.2;
    }
}
