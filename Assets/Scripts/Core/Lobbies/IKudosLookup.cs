namespace FriendlyPersonShooter.Core.Lobbies
{
    /// <summary>
    /// Past kudos between two players, for the matchmaker's small "you've helped each other
    /// before" bonus. <see cref="Social.KudosLedger"/> implements it; the network layer can plug in
    /// whatever the backend stores.
    /// </summary>
    public interface IKudosLookup
    {
        /// <summary>Kudos sent either way between the two players (0 for strangers).</summary>
        int KudosBetween(int playerA, int playerB);
    }
}
