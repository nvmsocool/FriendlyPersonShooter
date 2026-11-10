using System.Collections.Generic;
using System.Numerics;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    /// <summary>Layers 2 and 3: the group goes down through gates and back up through the portal.</summary>
    public class LayerTravelTests
    {
        [Test]
        public void GoingDown_WaitsForTheCountdown_ThenMovesTheGroup()
        {
            var t = new LayerTravel();
            Assert.That(t.Layer, Is.EqualTo(1));
            Assert.That(t.BeginUp(0, 1), Is.False, "no way up from the top");
            Assert.That(t.BeginDown(5, 7), Is.True);
            Assert.That(t.Pending, Is.True);
            Assert.That(t.StartedBy, Is.EqualTo(7));
            Assert.That(t.Arrival, Is.EqualTo(LayerArrival.Start));
            Assert.That(t.BeginDown(6, 8), Is.False, "one move at a time");
            Assert.That(t.SecondsLeft(6), Is.EqualTo(LayerTravel.Countdown - 1f).Within(1e-4));

            Assert.That(t.Step(5 + LayerTravel.Countdown - 0.1), Is.False);
            Assert.That(t.Layer, Is.EqualTo(1));
            Assert.That(t.Step(5 + LayerTravel.Countdown), Is.True);
            Assert.That(t.Layer, Is.EqualTo(2));
            Assert.That(t.Pending, Is.False);
            Assert.That(t.Step(100), Is.False, "fires once");
        }

        [Test]
        public void TheDeepestLayer_HasNoWayDown()
        {
            var t = new LayerTravel(LayerSettings.LayerCount);
            Assert.That(t.CanGoDown, Is.False);
            Assert.That(t.BeginDown(0, 1), Is.False);
            Assert.That(t.BeginUp(0, 1), Is.True);
            Assert.That(t.Arrival, Is.EqualTo(LayerArrival.Gate));
            t.Step(LayerTravel.Countdown);
            Assert.That(t.Layer, Is.EqualTo(LayerSettings.LayerCount - 1));
        }

        [Test]
        public void Cancel_StopsThePendingMove()
        {
            var t = new LayerTravel();
            t.BeginDown(0, 1);
            t.Cancel();
            Assert.That(t.Step(100), Is.False);
            Assert.That(t.Layer, Is.EqualTo(1));
        }

        [Test]
        public void EachLayer_IsItsOwnMap()
        {
            const uint world = 12345u;
            Assert.That(LayerSettings.SeedFor(world, 1), Is.EqualTo(world), "layer 1 keeps today's map");
            var seeds = new HashSet<uint>();
            for (int layer = 1; layer <= LayerSettings.LayerCount; layer++) seeds.Add(LayerSettings.SeedFor(world, layer));
            Assert.That(seeds.Count, Is.EqualTo(LayerSettings.LayerCount));
            Assert.That(LayerSettings.SeedFor(world, 2), Is.EqualTo(LayerSettings.SeedFor(world, 2)), "same on every machine");
        }

        [Test]
        public void Portal_StandsOnlyBelowTheTop_AndArrivalIsClearOfTheGate()
        {
            for (int layer = 1; layer <= LayerSettings.LayerCount; layer++)
            {
                var settings = LayerSettings.ForLayer(layer);
                var map = LayerGenerator.Generate(LayerSettings.SeedFor(99u, layer), LayerSettings.MvpTiling(), settings);
                bool has = LayerTravel.TryPortal(map, layer, out var portal);
                Assert.That(has, Is.EqualTo(layer > 1));
                if (has) Assert.That(Vector2.Distance(portal, map.StartPoint.World), Is.EqualTo(LayerTravel.PortalOffset).Within(1e-3));

                var arrival = LayerTravel.GateArrival(map);
                float d = Vector2.Distance(arrival, map.Gates[0].Point.World);
                Assert.That(d, Is.GreaterThan(2.5f), "outside the gate's pass radius");
            }
        }

        [Test]
        public void LootBoxIds_DontClashBetweenLayers()
        {
            var ids = new HashSet<int>();
            int total = 0;
            for (int layer = 1; layer <= LayerSettings.LayerCount; layer++)
            {
                var map = LayerGenerator.Generate(LayerSettings.SeedFor(7u, layer), LayerSettings.MvpTiling(), LayerSettings.ForLayer(layer));
                foreach (var b in LootBoxSites.For(map, layer))
                {
                    ids.Add(b.Id);
                    total++;
                }
            }
            Assert.That(ids.Count, Is.EqualTo(total));
        }

        [Test]
        public void Map_RemembersItsLayer()
        {
            // The zone art builds each layer's own biome from this.
            for (int layer = 1; layer <= LayerSettings.LayerCount; layer++)
            {
                var map = LayerGenerator.Generate(LayerSettings.SeedFor(7u, layer), LayerSettings.MvpTiling(), LayerSettings.ForLayer(layer));
                Assert.That(map.Layer, Is.EqualTo(layer));
            }
            Assert.That(LayerGenerator.Generate(7u, LayerSettings.MvpTiling()).Layer, Is.EqualTo(1), "default settings are the top layer");
        }
    }
}
