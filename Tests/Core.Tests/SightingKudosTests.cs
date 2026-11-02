using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Social;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class SightingTests
    {
        private static readonly LayerMap Map = LayerGenerator.Generate(42, LayerSettings.MvpGrid(), LayerSettings.ForMvp());

        [Test]
        public void VisitingAZone_RevealsItsBias()
        {
            var book = new SightingBook(1);
            var zone = new ZoneCoord(1, 2);
            Assert.That(book.TryGetBias(zone, Map, out _), Is.False);
            book.Visit(zone);
            Assert.That(book.TryGetBias(zone, Map, out var bias), Is.True);
            Assert.That(bias.Primary, Is.EqualTo(Map.Zone(zone).Bias.Primary));
        }

        [Test]
        public void RecordingEnemies_PinsTheNearestLandmark_AndCountsAsAVisit()
        {
            var book = new SightingBook(1);
            var landmark = Map.Landmarks[3];
            var s = book.RecordEnemies(EssenceColor.Yellow, landmark.Point.World + new Vector2(1, 1), Map, 100);
            Assert.That(s.LandmarkId, Is.EqualTo(landmark.Id));
            Assert.That(s.Zone, Is.EqualTo(landmark.Point.Zone));
            Assert.That(book.KnowsBias(landmark.Point.Zone), Is.True);
            Assert.That(book.Latest(EssenceColor.Yellow, 101), Is.SameAs(s));
            Assert.That(book.Latest(EssenceColor.Red, 101), Is.Null);
        }

        [Test]
        public void Sightings_Expire()
        {
            var book = new SightingBook(1, new SightingSettings { EnemyLifetime = 60 });
            book.RecordEnemies(EssenceColor.Green, Map.Landmarks[0].Point.World, Map, 0);
            Assert.That(book.Recent(59).Count, Is.EqualTo(1));
            Assert.That(book.Recent(60).Count, Is.EqualTo(0));
            Assert.That(book.Latest(EssenceColor.Green, 60), Is.Null);
            Assert.That(book.ExpireOld(60), Is.EqualTo(1));
        }

        [Test]
        public void BloomPin_LastsUntilTheBloomEnds_AndBeatsEnemyPins()
        {
            var book = new SightingBook(1);
            var point = Map.Landmarks[0].Point;
            var bloom = new Bloom(point, EssenceColor.Yellow, EnemyFamily.Wisp, 0, 200, 0);
            book.RecordEnemies(EssenceColor.Yellow, point.World, Map, 50);
            var pin = book.RecordBloom(bloom, Map, 10);
            Assert.That(pin.ExpiresAt, Is.EqualTo(200));
            Assert.That(book.Latest(EssenceColor.Yellow, 60).Kind, Is.EqualTo(SightingKind.Bloom));
            Assert.That(pin.ToQuickChat().Id, Is.EqualTo(QuickChatId.BloomAt));
        }

        [Test]
        public void Sharing_TeachesVisitedZonesAndPins_OneHopOnly()
        {
            var a = new SightingBook(1);
            var b = new SightingBook(2);
            var c = new SightingBook(3);
            a.Visit(new ZoneCoord(0, 0));
            a.RecordEnemies(EssenceColor.Red, Map.Landmarks[0].Point.World, Map, 10);

            var result = b.MergeFrom(a, 20);
            Assert.That(result.Useful, Is.True);
            Assert.That(b.KnowsBias(new ZoneCoord(0, 0)), Is.True);
            Assert.That(b.KnowsBias(Map.Landmarks[0].Point.Zone), Is.True);
            Assert.That(b.Latest(EssenceColor.Red, 20).SeenBy, Is.EqualTo(1));

            // A second share teaches nothing new.
            Assert.That(b.MergeFrom(a, 21).Useful, Is.False);

            // C learns B's pins, but not the zones B only heard about.
            c.MergeFrom(b, 30);
            Assert.That(c.KnowsBias(new ZoneCoord(0, 0)), Is.False);
            Assert.That(c.Latest(EssenceColor.Red, 30), Is.Not.Null);

            var d = new SightingBook(4, new SightingSettings { ShareLearnedBiases = true });
            d.MergeFrom(b, 30);
            Assert.That(d.KnowsBias(new ZoneCoord(0, 0)), Is.True);
        }

        [Test]
        public void Sharing_SkipsExpiredPins_AndKeepsTheNewestCopy()
        {
            var a = new SightingBook(1, new SightingSettings { EnemyLifetime = 100 });
            var b = new SightingBook(2, new SightingSettings { EnemyLifetime = 100 });
            var spot = Map.Landmarks[1].Point.World;
            a.RecordEnemies(EssenceColor.Yellow, spot, Map, 0);
            Assert.That(b.MergeFrom(a, 150).NewSightings, Is.EqualTo(0));

            b.RecordEnemies(EssenceColor.Yellow, spot, Map, 200);
            a.RecordEnemies(EssenceColor.Yellow, spot, Map, 250);
            Assert.That(b.MergeFrom(a, 260).NewSightings, Is.EqualTo(1));
            Assert.That(b.Recent(260).Count, Is.EqualTo(1));
            Assert.That(b.Recent(260)[0].SeenAt, Is.EqualTo(250));
        }
    }

    public class KudosTests
    {
        [Test]
        public void Kudos_NeedARealInteraction()
        {
            var ledger = new KudosLedger();
            Assert.That(ledger.Give(1, 2, 10), Is.EqualTo(KudosOutcome.NoInteraction));
            Assert.That(ledger.Give(1, 1, 10), Is.EqualTo(KudosOutcome.Self));

            ledger.NoteHelp(helper: 2, helped: 1, KudosReason.Revive, 10);
            Assert.That(ledger.Give(2, 1, 11), Is.EqualTo(KudosOutcome.NoInteraction), "the helper can't thank themselves for helping");
            Assert.That(ledger.Eligible(1, 11), Is.EqualTo(new[] { 2 }));
            Assert.That(ledger.Give(1, 2, 11), Is.EqualTo(KudosOutcome.Given));
            Assert.That(ledger.ReceivedBy(2), Is.EqualTo(1));
            Assert.That(ledger.GivenBy(1), Is.EqualTo(1));
            Assert.That(ledger.ReceivedFor(2, KudosReason.Revive), Is.EqualTo(1));
        }

        [Test]
        public void Interaction_ExpiresAfterTheWindow()
        {
            var ledger = new KudosLedger(new KudosSettings { InteractionWindow = 600 });
            ledger.NoteHelp(2, 1, KudosReason.Buff, 0);
            Assert.That(ledger.CanGive(1, 2, 600), Is.EqualTo(KudosOutcome.Given));
            Assert.That(ledger.CanGive(1, 2, 601), Is.EqualTo(KudosOutcome.NoInteraction));
        }

        [Test]
        public void OneKudosPerPairPerDay_EvenWithRepeatedHelp()
        {
            var ledger = new KudosLedger();
            ledger.NoteHelp(2, 1, KudosReason.Buff, 0);
            Assert.That(ledger.Give(1, 2, 1), Is.EqualTo(KudosOutcome.Given));
            ledger.NoteHelp(2, 1, KudosReason.Revive, 100);
            Assert.That(ledger.Give(1, 2, 101), Is.EqualTo(KudosOutcome.PairCooldown));

            // The other way round is its own pair.
            ledger.NoteHelp(1, 2, KudosReason.Gift, 100);
            Assert.That(ledger.Give(2, 1, 101), Is.EqualTo(KudosOutcome.Given));

            // A day later, after fresh help, it's allowed again.
            ledger.NoteHelp(2, 1, KudosReason.Revive, 86401);
            Assert.That(ledger.Give(1, 2, 86402), Is.EqualTo(KudosOutcome.Given));
            Assert.That(ledger.KudosBetween(1, 2), Is.EqualTo(3));
            Assert.That(ledger.KudosBetween(2, 1), Is.EqualTo(3));
            Assert.That(ledger.History(1, 2).Select(r => r.Reason),
                Is.EqualTo(new[] { KudosReason.Buff, KudosReason.Gift, KudosReason.Revive }));
            Assert.That(ledger.KudosBetween(1, 3), Is.EqualTo(0));
        }

        [Test]
        public void Givers_AreCappedPerDay()
        {
            var ledger = new KudosLedger(new KudosSettings { MaxGivenPerDay = 2 });
            for (int helper = 10; helper < 13; helper++) ledger.NoteHelp(helper, 1, KudosReason.Buff, 0);
            Assert.That(ledger.Give(1, 10, 1), Is.EqualTo(KudosOutcome.Given));
            Assert.That(ledger.Give(1, 11, 2), Is.EqualTo(KudosOutcome.Given));
            Assert.That(ledger.Give(1, 12, 3), Is.EqualTo(KudosOutcome.DailyCap));
            Assert.That(ledger.Eligible(1, 3), Is.Empty);
        }

        [Test]
        public void CacheReturn_SendsTheOwnersKudosAutomatically_OncePerDay()
        {
            var ledger = new KudosLedger();
            Assert.That(ledger.RecordCacheReturn(finder: 5, owner: 1, 10), Is.EqualTo(KudosOutcome.Given));
            Assert.That(ledger.ReceivedFor(5, KudosReason.CacheReturn), Is.EqualTo(1));
            Assert.That(ledger.RecordCacheReturn(5, 1, 20), Is.EqualTo(KudosOutcome.PairCooldown));
            Assert.That(ledger.RecordCacheReturn(1, 1, 20), Is.EqualTo(KudosOutcome.Self));
        }
    }

    public class ExchangeTrackerTests
    {
        [Test]
        public void Report_GivesShareOfEncountersWithAnExchange_ByKind()
        {
            var t = new ExchangeTracker(new ExchangeSettings { Window = 120 });
            t.Met(1, 2, 0);
            t.Met(3, 4, 0);
            t.Met(5, 6, 0);
            t.Met(7, 8, 0);
            Assert.That(t.Exchange(2, 1, ExchangeKind.Buff, 30), Is.True, "pairs are unordered");
            t.Exchange(1, 2, ExchangeKind.Info, 40);
            t.Exchange(3, 4, ExchangeKind.Revive, 100);
            Assert.That(t.Exchange(5, 6, ExchangeKind.Group, 121), Is.False, "too late");
            Assert.That(t.Exchange(1, 9, ExchangeKind.Gift, 10), Is.False, "no encounter");

            var report = t.Report(200);
            Assert.That(report.Encounters, Is.EqualTo(4));
            Assert.That(report.WithExchange, Is.EqualTo(2));
            Assert.That(report.Share, Is.EqualTo(0.5));
            Assert.That(report.CountOf(ExchangeKind.Buff), Is.EqualTo(1));
            Assert.That(report.CountOf(ExchangeKind.Info), Is.EqualTo(1));
            Assert.That(report.ShareOf(ExchangeKind.Revive), Is.EqualTo(0.25));
            Assert.That(report.CountOf(ExchangeKind.Group), Is.EqualTo(0));
        }

        [Test]
        public void OpenEncounters_WithoutExchanges_AreNotCountedYet()
        {
            var t = new ExchangeTracker(new ExchangeSettings { Window = 120 });
            t.Met(1, 2, 0);
            t.Met(3, 4, 0);
            t.Exchange(1, 2, ExchangeKind.Gift, 5);
            var early = t.Report(60);
            Assert.That(early.Encounters, Is.EqualTo(1));
            Assert.That(early.Share, Is.EqualTo(1.0));
            Assert.That(t.Report(121).Encounters, Is.EqualTo(2));
        }

        [Test]
        public void Remeeting_SoonAfter_DoesNotStartANewEncounter()
        {
            var t = new ExchangeTracker(new ExchangeSettings { Window = 120, RemeetCooldown = 600 });
            Assert.That(t.Met(1, 2, 0), Is.True);
            Assert.That(t.Met(2, 1, 300), Is.False);
            Assert.That(t.Met(1, 2, 600), Is.True);
            Assert.That(t.TotalEncounters, Is.EqualTo(2));
        }

        [Test]
        public void InfoQuickChat_CountsForEveryStrangerWhoHeardIt()
        {
            var map = LayerGenerator.Generate(42, LayerSettings.MvpGrid(), LayerSettings.ForMvp());
            var channel = new QuickChatChannel(map);
            var t = new ExchangeTracker();
            t.Met(1, 2, 0);
            t.Met(1, 3, 0);
            var listeners = new[] { new ChatListener(2, new Vector2(10, 0)), new ChatListener(3, new Vector2(0, 10)) };

            var question = channel.Send(1, Vector2.Zero, QuickChatMessage.SeenAny(EssenceColor.Yellow), listeners, 5);
            Assert.That(t.RecordQuickChat(question), Is.EqualTo(0), "a question isn't info");
            var tip = channel.Send(1, Vector2.Zero, QuickChatMessage.ColorPast(EssenceColor.Yellow, 0), listeners, 6);
            Assert.That(t.RecordQuickChat(tip), Is.EqualTo(2));
            Assert.That(t.Report(500).ShareOf(ExchangeKind.Info), Is.EqualTo(1.0));
        }
    }
}
