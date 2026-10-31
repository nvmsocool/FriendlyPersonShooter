namespace FriendlyPersonShooter.Core.Lobbies
{
    /// <summary>Tunable numbers for splitting and merging lobbies. Defaults follow the design doc.</summary>
    public sealed class LobbyRules
    {
        /// <summary>Most players a lobby can hold.</summary>
        public int MaxPlayers { get; set; } = 16;

        /// <summary>
        /// Players stay in one lobby while each is within this many zones of someone else in it.
        /// A player farther than this from everyone else splits off (design: "more than 3 zones apart").
        /// </summary>
        public int SplitDistance { get; set; } = 3;

        /// <summary>
        /// Closest two lobbies' players may be when they merge. 2 means at least one whole zone
        /// between them, so nobody pops into view in a zone they already have loaded.
        /// </summary>
        public int MinMergeGap { get; set; } = 2;

        /// <summary>Lobbies farther apart than this aren't considered for a merge.</summary>
        public int MaxMergeDistance { get; set; } = 5;

        /// <summary>Lobbies this size or smaller get a bonus so they merge more eagerly.</summary>
        public int SmallLobbySize { get; set; } = 4;

        public double ProximityWeight { get; set; } = 1.0;
        public double DiscoveryWeight { get; set; } = 1.0;
        public double SmallLobbyBonus { get; set; } = 0.5;

        /// <summary>Pairs scoring below this don't merge.</summary>
        public double MinMergeScore { get; set; } = 1.0;
    }
}
