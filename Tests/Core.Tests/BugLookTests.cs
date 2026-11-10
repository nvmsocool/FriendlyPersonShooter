using System.Collections.Generic;
using FriendlyPersonShooter.Core.Avatars;
using FriendlyPersonShooter.Core.Combat;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class BugLookTests
    {
        private static readonly EnemyFamily[] Families =
        {
            EnemyFamily.Wisp, EnemyFamily.Skitter, EnemyFamily.Mite, EnemyFamily.Sentinel, EnemyFamily.Diver, EnemyFamily.Bulwark, EnemyFamily.Glimmer,
        };

        [Test]
        public void SameSeed_SameBug()
        {
            foreach (var f in Families)
            {
                var a = new BugLook(f, 99);
                var b = new BugLook(f, 99);
                Assert.That(a.LegPairs, Is.EqualTo(b.LegPairs));
                Assert.That(a.Segments, Is.EqualTo(b.Segments));
                Assert.That(a.Antennae, Is.EqualTo(b.Antennae));
                Assert.That(a.HueShift, Is.EqualTo(b.HueShift));
                Assert.That(a.WingLength, Is.EqualTo(b.WingLength));
            }
        }

        [Test]
        public void NoTwoOfAPackAlike()
        {
            // A pack of 20 from consecutive enemy ids: every one differs from the others somewhere.
            foreach (var f in Families)
            {
                var seen = new HashSet<string>();
                for (uint id = 1; id <= 20; id++)
                {
                    var b = new BugLook(f, BugLook.SeedFrom(id));
                    seen.Add($"{b.LegPairs}/{b.Segments}/{b.Antennae}/{b.Eyes}/{b.LegLength:0.000}/{b.HueShift:0.000}/{b.Length:0.000}");
                }
                Assert.That(seen.Count, Is.EqualTo(20), f.ToString());
            }
        }

        [Test]
        public void EachFamilyKeepsItsInsect()
        {
            for (uint i = 1; i <= 200; i++)
            {
                uint seed = BugLook.SeedFrom(i);
                var skitter = new BugLook(EnemyFamily.Skitter, seed);
                Assert.That(skitter.LegPairs, Is.InRange(3, 4), "beetles and ticks walk on six or eight legs");
                Assert.That(skitter.WingPairs, Is.EqualTo(0));
                Assert.That(new BugLook(EnemyFamily.Wisp, seed).WingPairs, Is.InRange(1, 2), "fireflies fly");
                var diver = new BugLook(EnemyFamily.Diver, seed);
                Assert.That(diver.WingPairs == 2 || diver.Horn > 0f, "a dragonfly, or a wasp with a stinger");
                Assert.That(new BugLook(EnemyFamily.Sentinel, seed).LegPairs, Is.EqualTo(2), "a mantis stands on four legs");
                var bulwark = new BugLook(EnemyFamily.Bulwark, seed);
                Assert.That(bulwark.Segments, Is.InRange(4, 7), "a pill bug's shell has many plates");
                Assert.That(bulwark.LegPairs, Is.InRange(5, 7));
                Assert.That(new BugLook(EnemyFamily.Mite, seed).LegPairs, Is.InRange(2, 3));
                var glimmer = new BugLook(EnemyFamily.Glimmer, seed);
                Assert.That(glimmer.WingPairs, Is.EqualTo(2), "a butterfly has fore- and hindwings");
                Assert.That(glimmer.Mandibles, Is.EqualTo(0f), "and nothing to bite with: Glimmers never attack");
            }
        }

        [Test]
        public void ColourStaysTheFamilys()
        {
            for (uint i = 1; i <= 200; i++)
            {
                var b = new BugLook(EnemyFamily.Skitter, BugLook.SeedFrom(i));
                Assert.That(b.HueShift, Is.InRange(-0.015f, 0.015f), "a small shift keeps the essence colour readable (red never turns Diver orange)");
                Assert.That(b.Lightness, Is.InRange(0.75f, 1.05f));
            }
        }
    }
}
