using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Social;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class QuickChatTests
    {
        // 4x4 zones of 250 m: the world is 1000 m square.
        private static readonly LayerMap Map = LayerGenerator.Generate(42, LayerSettings.MvpTiling(), LayerSettings.ForMvp());

        [Test]
        public void Render_FillsColorAndLandmarkSlots()
        {
            var landmark = Map.Landmarks[0];
            string text = QuickChatCatalog.Render(QuickChatMessage.ColorPast(EssenceColor.Purple, landmark.Id), Map);
            Assert.That(text, Is.EqualTo($"Purple past {landmark.Name}"));
            Assert.That(QuickChatCatalog.Render(QuickChatMessage.SeenAny(EssenceColor.Yellow), Map), Is.EqualTo("Seen any Yellow?"));
            Assert.That(QuickChatCatalog.Render(QuickChatMessage.Simple(QuickChatId.NeedRevive), Map), Is.EqualTo("Need a res!"));
            Assert.That(QuickChatCatalog.Render(QuickChatMessage.HeadingTo(new ZoneCoord(2, 3)), Map), Is.EqualTo("Heading to zone 2.3"));
            Assert.That(QuickChatCatalog.Render(QuickChatMessage.PlayerNeedsRevive(7), Map, id => "Ana"), Is.EqualTo("Ana needs a res!"));
        }

        [Test]
        public void GateLine_StartingWithALandmark_IsCapitalised()
        {
            var landmark = Map.Landmarks.First(l => l.Name.StartsWith("the "));
            var text = QuickChatCatalog.Render(QuickChatMessage.GateAt(landmark.Id), Map);
            Assert.That(text, Is.EqualTo($"Gate at {landmark.Name}"));
            Assert.That(char.IsUpper(text[0]), Is.True);
        }

        [Test]
        public void Catalog_HasEveryIdInOrder_AndMarksInfoLines()
        {
            for (int i = 0; i < QuickChatCatalog.All.Count; i++) Assert.That((int)QuickChatCatalog.All[i].Id, Is.EqualTo(i));
            Assert.That(QuickChatCatalog.IsInfo(QuickChatId.ColorPast), Is.True);
            Assert.That(QuickChatCatalog.IsInfo(QuickChatId.BloomAt), Is.True);
            Assert.That(QuickChatCatalog.IsInfo(QuickChatId.SeenAny), Is.False);
            Assert.That(QuickChatCatalog.IsInfo(QuickChatId.Thanks), Is.False);
        }

        [Test]
        public void InvalidSlots_AreRefused()
        {
            Assert.That(QuickChatCatalog.IsValid(QuickChatMessage.ColorPast(EssenceColor.Yellow, 9999), Map), Is.False);
            Assert.That(QuickChatCatalog.IsValid(QuickChatMessage.ColorPast(EssenceColor.Yellow, -1), Map), Is.False);
            Assert.That(QuickChatCatalog.IsValid(new QuickChatMessage((QuickChatId)200), Map), Is.False);
            Assert.That(QuickChatCatalog.IsValid(QuickChatMessage.PlayerNeedsRevive(-1), Map), Is.False);
            var channel = new QuickChatChannel(Map);
            var result = channel.Send(1, Vector2.Zero, QuickChatMessage.GateAt(9999), new ChatListener[0], 0);
            Assert.That(result.Outcome, Is.EqualTo(QuickChatSendOutcome.Invalid));
        }

        [Test]
        public void Send_ReachesEveryoneInRadius_GroupedOrNot_AcrossTheWrap()
        {
            var channel = new QuickChatChannel(Map);
            var listeners = new[]
            {
                new ChatListener(1, new Vector2(5, 500)),    // sender
                new ChatListener(2, new Vector2(40, 500)),   // 35 m
                new ChatListener(3, new Vector2(975, 500)),  // 30 m across the x edge
                new ChatListener(4, new Vector2(5, 559)),    // 59 m
                new ChatListener(5, new Vector2(5, 570)),    // 70 m: too far
                new ChatListener(6, new Vector2(500, 500)),  // far away
            };
            var result = channel.Send(1, new Vector2(5, 500), QuickChatMessage.SeenAny(EssenceColor.Yellow), listeners, 10);
            Assert.That(result.Delivered, Is.True);
            Assert.That(result.Recipients, Is.EquivalentTo(new[] { 2, 3, 4 }));
            Assert.That(channel.HistoryOf(3).Count, Is.EqualTo(1));
            Assert.That(channel.HistoryOf(5).Count, Is.EqualTo(0));
            Assert.That(channel.HistoryOf(1).Count, Is.EqualTo(1), "the sender sees their own line");
            Assert.That(channel.Render(channel.HistoryOf(3)[0]), Is.EqualTo("Seen any Yellow?"));
        }

        [Test]
        public void RateLimit_AllowsABurst_ThenOneEveryRefill()
        {
            var channel = new QuickChatChannel(Map, new QuickChatSettings { Burst = 3, RefillSeconds = 2, RepeatCooldown = 0 });
            var none = new ChatListener[0];
            QuickChatSendOutcome Send(QuickChatId id, double t) => channel.Send(1, Vector2.Zero, QuickChatMessage.Simple(id), none, t).Outcome;

            Assert.That(Send(QuickChatId.Hello, 0), Is.EqualTo(QuickChatSendOutcome.Delivered));
            Assert.That(Send(QuickChatId.FollowMe, 0), Is.EqualTo(QuickChatSendOutcome.Delivered));
            Assert.That(Send(QuickChatId.GroupUp, 0), Is.EqualTo(QuickChatSendOutcome.Delivered));
            Assert.That(Send(QuickChatId.Thanks, 0.5), Is.EqualTo(QuickChatSendOutcome.RateLimited));
            Assert.That(channel.CanSend(1, 1.9), Is.False);
            Assert.That(Send(QuickChatId.Thanks, 2.0), Is.EqualTo(QuickChatSendOutcome.Delivered));
            Assert.That(Send(QuickChatId.OnMyWay, 2.1), Is.EqualTo(QuickChatSendOutcome.RateLimited));
            // Another player has their own bucket.
            Assert.That(channel.Send(2, Vector2.Zero, QuickChatMessage.Simple(QuickChatId.Hello), none, 2.1).Delivered, Is.True);
        }

        [Test]
        public void SameMessage_CantBeRepeatedWithinCooldown_ButOtherSlotsCan()
        {
            var channel = new QuickChatChannel(Map, new QuickChatSettings { Burst = 10, RepeatCooldown = 5 });
            var none = new ChatListener[0];
            Assert.That(channel.Send(1, Vector2.Zero, QuickChatMessage.SeenAny(EssenceColor.Yellow), none, 0).Delivered, Is.True);
            Assert.That(channel.Send(1, Vector2.Zero, QuickChatMessage.SeenAny(EssenceColor.Yellow), none, 3).Outcome,
                Is.EqualTo(QuickChatSendOutcome.Repeated));
            Assert.That(channel.Send(1, Vector2.Zero, QuickChatMessage.SeenAny(EssenceColor.Red), none, 3).Delivered, Is.True);
            Assert.That(channel.Send(1, Vector2.Zero, QuickChatMessage.SeenAny(EssenceColor.Yellow), none, 5).Delivered, Is.True);
        }

        [Test]
        public void History_KeepsOnlyTheNewestLines()
        {
            var channel = new QuickChatChannel(Map, new QuickChatSettings { Burst = 100, HistorySize = 3, RepeatCooldown = 0 });
            var listeners = new[] { new ChatListener(2, Vector2.Zero) };
            for (int i = 0; i < 5; i++)
                channel.Send(1, Vector2.Zero, QuickChatMessage.HeadingTo(new ZoneCoord(i % 3, 0)), listeners, i);
            var history = channel.HistoryOf(2);
            Assert.That(history.Count, Is.EqualTo(3));
            Assert.That(history[0].Time, Is.EqualTo(2));
            Assert.That(history[2].Time, Is.EqualTo(4));
        }
    }
}
