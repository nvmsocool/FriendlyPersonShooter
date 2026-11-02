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

        /// <summary>
        /// Lobbies farther apart than this aren't considered for a merge. Never more than
        /// <see cref="SplitDistance"/>, or the merged lobby would split straight away.
        /// </summary>
        public int MaxMergeDistance { get; set; } = 3;

        /// <summary>Lobbies this size or smaller get a bonus so they merge more eagerly.</summary>
        public int SmallLobbySize { get; set; } = 4;

        public double ProximityWeight { get; set; } = 1.0;
        public double DiscoveryWeight { get; set; } = 1.0;
        public double SmallLobbyBonus { get; set; } = 0.5;

        /// <summary>
        /// Weight for lobbies heading toward each other (positive) or apart (negative), judged from the
        /// closest pair of players. Players with no velocity add nothing.
        /// </summary>
        public double ApproachWeight { get; set; } = 3.0;

        /// <summary>Closing speed (metres per second) that earns the full approach score.</summary>
        public double ApproachFullSpeed { get; set; } = 6.0;

        /// <summary>
        /// Weight for complementary essence needs: the share (0 to 1) of both lobbies' wanted essence
        /// that the other lobby has to spare. Second in the doc's order, so it stays below one step of
        /// proximity (0.5 with the default distances): a full match can't beat being a zone closer,
        /// but it decides between otherwise equal pairs. Lobbies with no wants score 0 here.
        /// </summary>
        public double EssenceWeight { get; set; } = 0.4;

        /// <summary>
        /// Bonus per past kudos between any player in one lobby and any player in the other (third in
        /// the doc's order, "a small bonus"). Needs a kudos lookup; without one it adds nothing.
        /// </summary>
        public double KudosWeight { get; set; } = 0.05;

        /// <summary>Most the kudos bonus can add, so old friends nudge a pair rather than decide it.</summary>
        public double MaxKudosBonus { get; set; } = 0.2;

        /// <summary>Pairs scoring below this don't merge.</summary>
        public double MinMergeScore { get; set; } = 1.0;
    }
}
