using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class RaidRunTests
    {
        [Test]
        public void RunsPickThreeDifferentRoomsThenTheBoss()
        {
            var seen = new HashSet<string>();
            var modifiers = new HashSet<RaidModifier>();
            for (uint seed = 1; seed < 60; seed++)
            {
                var run = new RaidRun(seed, 4);
                Assert.That(run.Rooms.Count, Is.EqualTo(3));
                Assert.That(run.Rooms.Distinct().Count(), Is.EqualTo(3));
                Assert.That(run.Rooms, Has.None.EqualTo(RaidRoomKind.Boss));
                Assert.That(run.Modifier, Is.Not.EqualTo(RaidModifier.None));
                seen.Add(string.Join(",", run.Rooms));
                modifiers.Add(run.Modifier);
            }
            Assert.That(seen.Count, Is.GreaterThan(10), "runs differ");
            Assert.That(modifiers.Count, Is.EqualTo(5));
        }

        [Test]
        public void PortalCountsDownOnceSomeoneStepsInThenTheBossComes()
        {
            var run = new RaidRun(7, 4, first: RaidRoomKind.HoldPlates);
            run.Begin(0);
            Assert.That(run.Room, Is.EqualTo(RaidRoomKind.HoldPlates));
            Assert.That(run.StepPortal(1, true), Is.False, "no portal before the room is solved");
            Assert.That(run.MarkSolved(), Is.True);
            Assert.That(run.MarkSolved(), Is.False);
            Assert.That(run.StepPortal(10, false), Is.False, "waits for someone");
            Assert.That(run.StepPortal(11, true), Is.False);
            Assert.That(run.StepPortal(15.9, false), Is.False, "the countdown keeps going");
            Assert.That(run.StepPortal(16, false), Is.True);
            run.Advance(16);
            Assert.That(run.Stage, Is.EqualTo(1));
            Assert.That(run.Solved, Is.False);
            run.Advance(20);
            run.Advance(30);
            Assert.That(run.AtBoss, Is.True);
            Assert.That(run.Room, Is.EqualTo(RaidRoomKind.Boss));
            Assert.That(run.MarkSolved(), Is.False, "the boss stage ends the run instead");
        }

        [Test]
        public void BrittlePaysMoreAndHurtsMore()
        {
            var run = new RaidRun(3, 4, modifier: RaidModifier.Brittle);
            Assert.That(run.DamageTakenMultiplier, Is.GreaterThan(1f));
            Assert.That(run.RoomYellow, Is.GreaterThan(new RaidRunSettings().RoomYellow));
            Assert.That(new RaidRun(3, 4, modifier: RaidModifier.Swarming).AddsMultiplier, Is.EqualTo(2));
        }

        [Test]
        public void ColorLocksNeedsTheOrderAndDifferentPlayers()
        {
            var locks = new ColorLocks(11, 4);
            Assert.That(locks.Length, Is.EqualTo(3));
            for (int i = 1; i < locks.Length; i++) Assert.That(locks.SequenceAt(i), Is.Not.EqualTo(locks.SequenceAt(i - 1)));

            int first = locks.Next;
            Assert.That(locks.Hit(first, 1, 4, 0), Is.EqualTo(LockHit.Progress));
            Assert.That(locks.Hit(first, 2, 4, 0.1), Is.EqualTo(LockHit.Ignored), "a second hit on the lit crystal is no mistake");
            int second = locks.Next;
            Assert.That(locks.Hit(second, 1, 4, 0.5), Is.EqualTo(LockHit.SamePlayer));
            Assert.That(locks.Index, Is.EqualTo(1));
            Assert.That(locks.Hit(second, 2, 4, 0.6), Is.EqualTo(LockHit.Progress));
            int wrong = Enumerable.Range(0, 3).First(c => c != locks.Next && c != second);
            Assert.That(locks.Hit(wrong, 3, 4, 1), Is.EqualTo(LockHit.Wrong));
            Assert.That(locks.Index, Is.EqualTo(0), "back to the start of the same order");
            Assert.That(locks.Next, Is.EqualTo(first));
        }

        [Test]
        public void ColorLocksSolvesAfterThreeLongerRoundsAndPacksForTheHud()
        {
            var locks = new ColorLocks(5, 4);
            int player = 0;
            double t = 0;
            var results = new List<LockHit>();
            while (!locks.Solved)
            {
                int packed = locks.Pack();
                Assert.That(ColorLocks.LengthOf(packed), Is.EqualTo(locks.Length));
                Assert.That(ColorLocks.CrystalOf(packed, ColorLocks.IndexOf(packed)), Is.EqualTo(locks.Next));
                results.Add(locks.Hit(locks.Next, player++ % 4, 4, t += 1));
            }
            Assert.That(results.Count(r => r == LockHit.RoundDone), Is.EqualTo(2));
            Assert.That(results.Count, Is.EqualTo(3 + 4 + 5));
            Assert.That(locks.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void ColorLocksStartsOverAfterAPauseButLetsASoloPlayerHitTwice()
        {
            var locks = new ColorLocks(9, 1);
            Assert.That(locks.Hit(locks.Next, 1, 1, 0), Is.EqualTo(LockHit.Progress));
            Assert.That(locks.Hit(locks.Next, 1, 1, 1), Is.EqualTo(LockHit.Progress), "alone, you may hit them all");
            Assert.That(locks.Step(5), Is.False);
            Assert.That(locks.Step(1 + ColorLocks.HitWindow + 0.1), Is.True);
            Assert.That(locks.Index, Is.EqualTo(0));
        }

        [Test]
        public void PlatesFillOnlyWhileAllAreHeld()
        {
            var layout = RaidRoomLayout.For(RaidRoomKind.HoldPlates);
            var plates = new HoldPlates(5);
            Assert.That(plates.Plates, Is.EqualTo(3));
            Assert.That(new HoldPlates(1).Plates, Is.EqualTo(1));
            Assert.That(new HoldPlates(8).Plates, Is.EqualTo(4));

            var onTwo = new[] { layout.Targets[0], layout.Targets[1] + new Vector3(1f, 0f, 1f) };
            int two = HoldPlates.OccupancyOf(layout, 3, onTwo);
            Assert.That(two, Is.EqualTo(0b011));
            plates.Step(10f, two);
            Assert.That(plates.Held, Is.EqualTo(0f));
            int all = HoldPlates.OccupancyOf(layout, 3, onTwo.Append(layout.Targets[2]));
            plates.Step(10f, all);
            Assert.That(plates.Held, Is.EqualTo(10f));
            plates.Step(5f, two);
            Assert.That(plates.Held, Is.EqualTo(10f - 5f * HoldPlates.DrainRate).Within(1e-4));
            Assert.That(plates.Step(HoldPlates.HoldSeconds, all), Is.True);
            Assert.That(plates.Solved, Is.True);
        }

        [Test]
        public void GauntletStonesAreJumpable()
        {
            var layout = RaidRoomLayout.For(RaidRoomKind.Gauntlet);
            Assert.That(layout.Path.Count, Is.EqualTo(RaidRoomLayout.GauntletStones + 2));
            // A standing jump (6 m/s up, 18 m/s² down, 6.5 m/s across) covers about 4.3 m.
            for (int i = 1; i < layout.Path.Count; i++)
            {
                var a = layout.Path[i - 1];
                var b = layout.Path[i];
                float gapZ = (b.Center.Z - b.Size.Z / 2f) - (a.Center.Z + a.Size.Z / 2f);
                float gapX = System.Math.Max(0f, System.Math.Abs(b.Center.X - a.Center.X) - (a.Size.X + b.Size.X) / 2f);
                float gap = (float)System.Math.Sqrt(gapZ * gapZ + gapX * gapX);
                Assert.That(gap, Is.InRange(0.8f, 2.6f), $"gap before stone {i}");
                Assert.That(b.Top - a.Top, Is.LessThan(0.8f), $"step up to stone {i}");
            }
            Assert.That(layout.Sweepers.Count, Is.EqualTo(4));
        }

        [Test]
        public void SweepersHitStandingPlayersButNotJumpingOnes()
        {
            var s = new RaidSweeper(Vector3.Zero, 2.6f, 2f, 0f);
            // The arm starts pointing +z; a player at +x is passed a quarter turn later.
            var feet = new Vector3(1.5f, 0f, 0f);
            Assert.That(GauntletRun.Swept(s, feet, 0.0, 0.3), Is.False);
            Assert.That(GauntletRun.Swept(s, feet, 0.7, 0.85), Is.True);
            Assert.That(GauntletRun.Swept(s, feet + new Vector3(0f, 0.8f, 0f), 0.7, 0.85), Is.False, "jumped over it");
            Assert.That(GauntletRun.Swept(s, new Vector3(4f, 0f, 0f), 0, 10), Is.False, "out of reach");
        }

        [Test]
        public void GauntletSolvesWhenEveryoneUpIsOnTheEndPad()
        {
            var layout = RaidRoomLayout.For(RaidRoomKind.Gauntlet);
            var run = new GauntletRun(layout);
            var end = layout.Path[layout.Path.Count - 1];
            var onEnd = new Vector3(end.Center.X, end.Top, end.Center.Z);
            var start = layout.Path[0];
            var onStart = new Vector3(start.Center.X, start.Top, start.Center.Z);
            var swept = new List<int>();
            var fell = new List<int>();
            Assert.That(run.Step(0, 0.1, new[] { (1, onEnd, true), (2, onStart, true) }, swept, fell), Is.False);
            Assert.That(run.Furthest, Is.EqualTo(layout.Path.Count - 1));
            Assert.That(run.Step(0.1, 0.2, new[] { (1, onEnd, true), (2, onStart + new Vector3(0f, -9f, 0f), true) }, swept, fell), Is.False);
            Assert.That(fell, Is.EqualTo(new[] { 2 }));
            Assert.That(run.Step(0.2, 0.3, new[] { (1, onEnd, true), (2, onStart, false) }, swept, fell), Is.True, "the downed don't hold it up");
        }

        [Test]
        public void WispStormStartsOverWhenTimeRunsOut()
        {
            var storm = new WispStorm(4, false, 0);
            Assert.That(storm.Target, Is.EqualTo(16));
            Assert.That(new WispStorm(4, true, 0).Target, Is.GreaterThan(storm.Target));
            for (int i = 0; i < 10; i++) storm.Kill();
            Assert.That(storm.Step(WispStorm.Duration + 0.1), Is.True);
            Assert.That(storm.Kills, Is.EqualTo(0));
            Assert.That(storm.Attempts, Is.EqualTo(2));
            bool solved = false;
            for (int i = 0; i < storm.Target; i++) solved = storm.Kill();
            Assert.That(solved, Is.True);
        }
    }
}
