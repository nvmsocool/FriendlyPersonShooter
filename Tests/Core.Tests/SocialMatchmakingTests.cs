using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Lobbies;
using FriendlyPersonShooter.Core.Social;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class SocialMatchmakingTests
    {
        private readonly WorldGrid _grid = new WorldGrid(16, 16, 100f);
        private readonly LobbyRules _rules = new LobbyRules();

        private static Lobby LobbyAt(int id, int x)
        {
            var lobby = new Lobby(id, 1, 7);
            lobby.Players.Add(new LobbyPlayer(id * 100, new ZoneCoord(x, 2)));
            return lobby;
        }

        private sealed class FixedKudos : IKudosLookup
        {
            private readonly Dictionary<(int, int), int> _pairs = new Dictionary<(int, int), int>();
            public void Set(int a, int b, int n) => _pairs[(a, b)] = n;
            public int KudosBetween(int a, int b) =>
                _pairs.TryGetValue((a, b), out int n) || _pairs.TryGetValue((b, a), out n) ? n : 0;
        }

        [Test]
        public void WithoutEssenceOrKudosData_ScoresAreUnchanged()
        {
            var a = LobbyAt(1, 2);
            var b = LobbyAt(2, 4);
            var plain = MergeScorer.Evaluate(a, b, _grid, _rules);
            var withEmptyLookup = MergeScorer.Evaluate(a, b, _grid, _rules, new FixedKudos());
            // Proximity 1 + discovery 1 + small-lobby bonus 0.5, as before step 5.
            Assert.That(plain.Score, Is.EqualTo(2.5).Within(1e-9));
            Assert.That(withEmptyLookup.Score, Is.EqualTo(plain.Score));
        }

        [Test]
        public void ComplementaryNeeds_BreakTies()
        {
            var a = LobbyAt(1, 2);
            a.Players[0].Wants[EssenceColor.Yellow] = 20;
            a.Players[0].Spare[EssenceColor.Red] = 15;

            var match = LobbyAt(2, 4);
            match.Players[0].Spare[EssenceColor.Yellow] = 30;
            match.Players[0].Wants[EssenceColor.Red] = 10;

            var noMatch = LobbyAt(3, 4);
            noMatch.Players[0].Spare[EssenceColor.Green] = 30;

            Assert.That(MergeScorer.Complement(a, match), Is.EqualTo(1.0));
            Assert.That(MergeScorer.Complement(a, noMatch), Is.EqualTo(0.0));
            double gain = MergeScorer.Evaluate(a, match, _grid, _rules).Score - MergeScorer.Evaluate(a, noMatch, _grid, _rules).Score;
            Assert.That(gain, Is.EqualTo(_rules.EssenceWeight).Within(1e-9));

            var merges = LobbyMatchmaker.ChooseMerges(new[] { a, noMatch, match }, _grid, _rules);
            Assert.That(merges[0].B.Id, Is.EqualTo(match.Id));
        }

        [Test]
        public void PartialMatch_ScoresTheShareOfWantsCovered()
        {
            var a = LobbyAt(1, 2);
            a.Players[0].Wants[EssenceColor.Yellow] = 40;
            var b = LobbyAt(2, 4);
            b.Players[0].Spare[EssenceColor.Yellow] = 10;
            Assert.That(MergeScorer.Complement(a, b), Is.EqualTo(0.25));
        }

        [Test]
        public void Proximity_DominatesEssence()
        {
            var a = LobbyAt(1, 2);
            a.Players[0].Wants[EssenceColor.Yellow] = 20;
            var nearNoMatch = LobbyAt(2, 4);   // 2 zones away
            var farMatch = LobbyAt(3, 5);      // 3 zones away, has all the Yellow A wants
            farMatch.Players[0].Spare[EssenceColor.Yellow] = 50;
            Assert.That(MergeScorer.Evaluate(a, nearNoMatch, _grid, _rules).Score,
                Is.GreaterThan(MergeScorer.Evaluate(a, farMatch, _grid, _rules).Score));
        }

        [Test]
        public void Kudos_IsASmallCappedNudge_BelowEssence()
        {
            var a = LobbyAt(1, 2);
            a.Players[0].Wants[EssenceColor.Yellow] = 20;
            var friends = LobbyAt(2, 4);
            var supplier = LobbyAt(3, 4);
            supplier.Players[0].Spare[EssenceColor.Yellow] = 20;
            var kudos = new FixedKudos();
            kudos.Set(a.Players[0].Id, friends.Players[0].Id, 100);

            double plain = MergeScorer.Evaluate(a, friends, _grid, _rules).Score;
            double nudged = MergeScorer.Evaluate(a, friends, _grid, _rules, kudos).Score;
            Assert.That(nudged - plain, Is.EqualTo(_rules.MaxKudosBonus).Within(1e-9), "capped");
            Assert.That(MergeScorer.Evaluate(a, supplier, _grid, _rules, kudos).Score, Is.GreaterThan(nudged));

            kudos.Set(a.Players[0].Id, friends.Players[0].Id, 1);
            Assert.That(MergeScorer.Evaluate(a, friends, _grid, _rules, kudos).Score - plain,
                Is.EqualTo(_rules.KudosWeight).Within(1e-9));

            // Between two otherwise equal pairs, kudos decides.
            var stranger = LobbyAt(4, 4);
            var merges = LobbyMatchmaker.ChooseMerges(new[] { a, stranger, friends }, _grid, _rules, kudos);
            Assert.That(merges[0].B.Id, Is.EqualTo(friends.Id));
        }

        [Test]
        public void KudosLedger_PlugsIntoTheMatchmaker()
        {
            var ledger = new KudosLedger();
            ledger.NoteHelp(200, 100, KudosReason.Revive, 0);
            ledger.Give(100, 200, 1);
            var a = LobbyAt(1, 2);
            var b = LobbyAt(2, 4);
            Assert.That(MergeScorer.KudosBonus(a, b, ledger, _rules), Is.EqualTo(_rules.KudosWeight).Within(1e-9));
        }

        [Test]
        public void Blocks_StillReject_EvenWithKudosAndNeeds()
        {
            var a = LobbyAt(1, 2);
            var b = LobbyAt(2, 4);
            a.Players[0].Wants[EssenceColor.Yellow] = 20;
            b.Players[0].Spare[EssenceColor.Yellow] = 20;
            b.Players[0].BlockedPlayers.Add(a.Players[0].Id);
            var kudos = new FixedKudos();
            kudos.Set(a.Players[0].Id, b.Players[0].Id, 5);
            Assert.That(MergeScorer.Evaluate(a, b, _grid, _rules, kudos).RejectReason, Is.EqualTo("blocked player"));
        }
    }

    public class SocialBrainTests
    {
        private static readonly LayerMap Map = LayerGenerator.Generate(42, LayerSettings.MvpGrid(), LayerSettings.ForMvp());

        private static QuickChatDelivery Heard(int sender, QuickChatMessage m, Vector2 at = default) =>
            new QuickChatDelivery(sender, at, m, 0);

        [Test]
        public void SeeingABloom_CallsItOutAtTheNearestLandmark_Once()
        {
            var brain = new SocialBrain(1, 7, new SocialBrainSettings { AnnounceBloomChance = 1 });
            var landmark = Map.Landmarks[2];
            var bloom = new Bloom(landmark.Point, EssenceColor.Yellow, EnemyFamily.Wisp, 0, 200, 0);
            var said = brain.OnSawBloom(bloom, Map, 10);
            Assert.That(said.HasValue, Is.True);
            Assert.That(said.Value.Id, Is.EqualTo(QuickChatId.BloomAt));
            Assert.That(said.Value.LandmarkId, Is.EqualTo(landmark.Id));
            Assert.That(brain.OnSawBloom(bloom, Map, 20).HasValue, Is.False);
        }

        [Test]
        public void GoingDown_AsksForARes_AndRevivedSaysThanks()
        {
            var brain = new SocialBrain(1, 7);
            Assert.That(brain.OnDowned(0).Value.Id, Is.EqualTo(QuickChatId.NeedRevive));
            Assert.That(brain.OnRevived(2, 10).Value.Id, Is.EqualTo(QuickChatId.Thanks));
            // Just thanked player 2, so a buff right after doesn't get another "Thanks!".
            Assert.That(brain.OnBuffed(2, 12).HasValue, Is.False);
        }

        [Test]
        public void BuffThanks_RespectTheCooldown()
        {
            var brain = new SocialBrain(1, 7, new SocialBrainSettings { ThankBuffChance = 1, ThanksCooldown = 30 });
            Assert.That(brain.OnBuffed(2, 0).HasValue, Is.True);
            Assert.That(brain.OnBuffed(2, 10).HasValue, Is.False);
            Assert.That(brain.OnBuffed(3, 10).HasValue, Is.True);
            Assert.That(brain.OnBuffed(2, 30).HasValue, Is.True);
        }

        [Test]
        public void AskedSeenAny_AnswersFromItsSightings()
        {
            var brain = new SocialBrain(1, 7, new SocialBrainSettings { AnswerChance = 1, AnswerNotSeenChance = 0 });
            var book = new SightingBook(1);
            var landmark = Map.Landmarks[4];
            book.RecordEnemies(EssenceColor.Yellow, landmark.Point.World, Map, 0);

            var answer = brain.OnHeard(Heard(2, QuickChatMessage.SeenAny(EssenceColor.Yellow)), book, 10);
            Assert.That(answer.Value, Is.EqualTo(QuickChatMessage.ColorPast(EssenceColor.Yellow, landmark.Id)));
            Assert.That(brain.OnHeard(Heard(2, QuickChatMessage.SeenAny(EssenceColor.Red)), book, 10).HasValue, Is.False);
            Assert.That(brain.OnHeard(Heard(1, QuickChatMessage.SeenAny(EssenceColor.Yellow)), book, 10).HasValue, Is.False, "own message");
            Assert.That(brain.OnHeard(Heard(2, QuickChatMessage.Simple(QuickChatId.Hello)), book, 10).HasValue, Is.False);
        }

        [Test]
        public void NeedingAColor_AsksOnlyWithoutAPin_AndNotTooOften()
        {
            var brain = new SocialBrain(1, 7, new SocialBrainSettings { AskChance = 1, AskCooldown = 45 });
            var book = new SightingBook(1);
            Assert.That(brain.OnNeedColor(EssenceColor.Yellow, book, 0).Value, Is.EqualTo(QuickChatMessage.SeenAny(EssenceColor.Yellow)));
            Assert.That(brain.OnNeedColor(EssenceColor.Yellow, book, 10).HasValue, Is.False);
            book.RecordEnemies(EssenceColor.Yellow, Map.Landmarks[0].Point.World, Map, 50);
            Assert.That(brain.OnNeedColor(EssenceColor.Yellow, book, 60).HasValue, Is.False);
        }

        [Test]
        public void Tips_ForANeededColor_GiveATarget()
        {
            var brain = new SocialBrain(1, 7, new SocialBrainSettings { FollowTipChance = 1 });
            var landmark = Map.Landmarks[1];
            Assert.That(brain.TryFollowTip(Heard(2, QuickChatMessage.ColorPast(EssenceColor.Yellow, landmark.Id)),
                EssenceColor.Yellow, Map, out var target), Is.True);
            Assert.That(target, Is.EqualTo(landmark.Point.World));
            Assert.That(brain.TryFollowTip(Heard(2, QuickChatMessage.BloomHere(EssenceColor.Yellow), new Vector2(5, 6)),
                EssenceColor.Yellow, Map, out target), Is.True);
            Assert.That(target, Is.EqualTo(new Vector2(5, 6)));
            Assert.That(brain.TryFollowTip(Heard(2, QuickChatMessage.ColorPast(EssenceColor.Red, landmark.Id)),
                EssenceColor.Yellow, Map, out _), Is.False);
        }

        [Test]
        public void Invites_AreDeterministicPerSeed_AndRespectLimits()
        {
            bool[] Run(uint seed)
            {
                var brain = new SocialBrain(1, seed);
                var r = new bool[20];
                for (int i = 0; i < r.Length; i++) r[i] = brain.ShouldAcceptInvite(2, 1);
                return r;
            }
            Assert.That(Run(5), Is.EqualTo(Run(5)));
            Assert.That(Run(5), Does.Contain(true).And.Contain(false));

            var grouper = new SocialBrain(1, 5, SocialBrainSettings.Grouper());
            Assert.That(grouper.ShouldAcceptInvite(2, 3), Is.True);
            Assert.That(grouper.ShouldAcceptInvite(2, 8), Is.False, "group would be too big");
            Assert.That(grouper.ShouldAcceptInvite(2, 1, inviterBlocked: true), Is.False);
            Assert.That(new SocialBrain(1, 5, SocialBrainSettings.Silent()).ShouldAcceptInvite(2, 1), Is.False);
        }
    }
}
