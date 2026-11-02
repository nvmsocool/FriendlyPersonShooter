using FriendlyPersonShooter.Core.Lobbies;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class LobbyMetricsTests
    {
        private static LobbyMetrics New() => new LobbyMetrics(480, 480);

        [Test]
        public void MergeOfFarLobbies_IsNotAPopIn()
        {
            var m = New();
            m.Observe(0, new[] { new PlayerSample(1, 0, 0, 1), new PlayerSample(2, 70, 0, 2) });
            m.RecordMerge(1, new[] { 1 }, new[] { 2 });
            m.Observe(1, new[] { new PlayerSample(1, 0, 0, 1), new PlayerSample(2, 70, 0, 1) });
            Assert.That(m.MergePopIns, Is.EqualTo(0));
        }

        [Test]
        public void MergeOfCloseLobbies_CountsAPopInForEachViewer()
        {
            var m = New();
            m.Observe(0, new[] { new PlayerSample(1, 0, 0, 1), new PlayerSample(2, 10, 0, 2) });
            m.Observe(1, new[] { new PlayerSample(1, 0, 0, 1), new PlayerSample(2, 10, 0, 1) });
            Assert.That(m.MergePopIns, Is.EqualTo(2));
        }

        [Test]
        public void PartyMove_AndLogin_AreCountedSeparately()
        {
            var m = New();
            m.Observe(0, new[] { new PlayerSample(1, 0, 0, 1), new PlayerSample(2, 5, 0, 2) });
            m.RecordPartyMove(new[] { 2 });
            m.Observe(1, new[]
            {
                new PlayerSample(1, 0, 0, 1), new PlayerSample(2, 5, 0, 1), new PlayerSample(3, 3, 0, 1),
            });
            Assert.That(m.PartyPopIns, Is.EqualTo(2));
            Assert.That(m.LoginPopIns, Is.EqualTo(2), "players 1 and 2 each see player 3 appear");
            Assert.That(m.MergePopIns, Is.EqualTo(0));
        }

        [Test]
        public void DistanceWrapsAroundTheWorld()
        {
            var m = New();
            m.Observe(0, new[] { new PlayerSample(1, 2, 0, 1), new PlayerSample(2, 475, 0, 2) });
            m.Observe(1, new[] { new PlayerSample(1, 2, 0, 1), new PlayerSample(2, 475, 0, 1) });
            Assert.That(m.MergePopIns, Is.EqualTo(2), "7 m apart across the edge");
        }

        [Test]
        public void Merge_CountsAsMet_WhenSidesComeClose()
        {
            var m = New();
            m.RecordMerge(0, new[] { 1 }, new[] { 2 });
            m.Observe(0, new[] { new PlayerSample(1, 0, 0, 1), new PlayerSample(2, 80, 0, 1) });
            m.Observe(10, new[] { new PlayerSample(1, 30, 0, 1), new PlayerSample(2, 50, 0, 1) });
            Assert.That(m.MergesMet, Is.EqualTo(1));
            Assert.That(m.TotalTimeToMeet, Is.EqualTo(10));
        }

        [Test]
        public void Merge_CountsAsMissed_AfterTheWindow_OrWhenTheySplit()
        {
            var m = New();
            m.RecordMerge(0, new[] { 1 }, new[] { 2 });
            m.RecordMerge(0, new[] { 3 }, new[] { 4 });
            m.Observe(5, new[]
            {
                new PlayerSample(1, 0, 0, 1), new PlayerSample(2, 80, 0, 1),
                new PlayerSample(3, 200, 0, 3), new PlayerSample(4, 300, 0, 4),
            });
            Assert.That(m.MergesMissed, Is.EqualTo(1), "3 and 4 split apart again");
            m.Observe(40, new[] { new PlayerSample(1, 0, 0, 1), new PlayerSample(2, 80, 0, 1) });
            Assert.That(m.MergesMissed, Is.EqualTo(2));
            Assert.That(m.MergesMet, Is.EqualTo(0));
        }

        [Test]
        public void PartyInTwoLobbies_IsReported()
        {
            var m = New();
            m.Observe(0, new[] { new PlayerSample(1, 0, 0, 1, groupId: 9), new PlayerSample(2, 5, 0, 2, groupId: 9) });
            Assert.That(m.PartySplits, Is.EqualTo(1));
            m.Observe(1, new[] { new PlayerSample(1, 0, 0, 1, groupId: 9), new PlayerSample(2, 5, 0, 1, groupId: 9) });
            Assert.That(m.PartySplits, Is.EqualTo(1));
        }
    }
}
