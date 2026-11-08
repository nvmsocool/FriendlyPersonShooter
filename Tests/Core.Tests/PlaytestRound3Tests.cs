using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    /// <summary>Playtest round 3: the starting gun pick and the raid's victory hang-out.</summary>
    public class PlaytestRound3Tests
    {
        [Test]
        public void StartingChoicesNeverRepeatAFrameOrAPayload()
        {
            for (uint seed = 0; seed < 300; seed++)
            {
                var guns = LootRolls.StartingChoices(seed);
                Assert.That(guns.Length, Is.EqualTo(LootRolls.StartingChoiceCount));
                var frames = new HashSet<FrameKind>();
                var payloads = new HashSet<PayloadKind>();
                foreach (var g in guns)
                {
                    Assert.That(frames.Add(g.Frame), Is.True, $"seed {seed}: two {g.Frame}s");
                    Assert.That(g.Payload, Is.Not.EqualTo(PayloadKind.None));
                    Assert.That(payloads.Add(g.Payload), Is.True, $"seed {seed}: two {g.Payload} payloads");
                    Assert.That(Gun.Unpack(g.Pack()).Pack(), Is.EqualTo(g.Pack()), "survives the network");
                }
            }
            Assert.That(LootRolls.StartingChoices(7)[0].Pack(), Is.EqualTo(LootRolls.StartingChoices(7)[0].Pack()), "same seed, same pick");
            Assert.That(LootRolls.StartingChoices(1, 10).Length, Is.EqualTo(GunMods.PayloadKinds.Length), "never more than the payloads");
        }

        [Test]
        public void StartingChoicesCoverEveryFrameAndPayloadOverManyPlayers()
        {
            var frames = new HashSet<FrameKind>();
            var payloads = new HashSet<PayloadKind>();
            for (uint seed = 1; seed < 100; seed++)
            {
                foreach (var g in LootRolls.StartingChoices(seed))
                {
                    frames.Add(g.Frame);
                    payloads.Add(g.Payload);
                }
            }
            Assert.That(frames.Count, Is.EqualTo(GunFrames.All.Length));
            Assert.That(payloads.Count, Is.EqualTo(GunMods.PayloadKinds.Length));
        }

        private static RaidDoor RunningDoor(params int[] players)
        {
            var door = new RaidDoor();
            door.FillKey();
            Assert.That(door.Begin(0), Is.True);
            Assert.That(door.Update(door.Settings.GatherSeconds, players), Is.EqualTo(RaidDoorEvent.Started));
            return door;
        }

        [Test]
        public void AfterTheBossTheRaidersStayUntilTheyLeave()
        {
            var door = RunningDoor(1, 2);
            double won = 700; // near the run's 720 s limit
            Assert.That(door.Win(won), Is.True);
            Assert.That(door.Win(won + 1), Is.False, "only once");
            Assert.That(door.Phase, Is.EqualTo(RaidPhase.Running));
            Assert.That(door.IsInside(1), Is.True);
            Assert.That(door.TimeLeft(won + 10), Is.EqualTo(door.Settings.VictorySeconds - 10).Within(1e-6));

            // Nobody up no longer means a wipe, and the run's own time limit no longer applies.
            Assert.That(door.Check(won + 10, 0), Is.EqualTo(RaidOutcome.None));
            Assert.That(door.Check(door.Settings.TimeLimit + 30, 2), Is.EqualTo(RaidOutcome.None));

            door.Leave(1);
            Assert.That(door.Check(won + 20, 1), Is.EqualTo(RaidOutcome.None), "one is still celebrating");
            door.Leave(2);
            Assert.That(door.Check(won + 30, 0), Is.EqualTo(RaidOutcome.Cleared), "everyone went home");
        }

        [Test]
        public void TheHangOutEndsAfterItsTime()
        {
            var door = RunningDoor(1);
            door.Win(50);
            Assert.That(door.Check(50 + door.Settings.VictorySeconds - 1, 1), Is.EqualTo(RaidOutcome.None));
            Assert.That(door.Check(50 + door.Settings.VictorySeconds, 1), Is.EqualTo(RaidOutcome.Cleared));
            door.Finish(400, RaidOutcome.Cleared);
            Assert.That(door.Phase, Is.EqualTo(RaidPhase.Resetting));

            // The next run starts fresh.
            Assert.That(door.Update(400 + door.Settings.ResetSeconds, new int[0]), Is.EqualTo(RaidDoorEvent.Reopened));
            door.FillKey();
            door.Begin(500);
            door.Update(500 + door.Settings.GatherSeconds, new[] { 3 });
            Assert.That(door.Won, Is.False);
        }

        [Test]
        public void BeatingTheBossOpensTheExitPortalAndStopsTheFight()
        {
            var run = new RaidRun(5, 4, boss: RaidBossKind.Weaver);
            run.Begin(0);
            Assert.That(run.MarkWon(), Is.False, "not before the boss");
            for (int i = 0; i < run.Rooms.Count; i++) run.Advance(i + 1);
            Assert.That(run.AtBoss, Is.True);
            Assert.That(run.Weaver, Is.Not.Null);

            Assert.That(run.MarkWon(), Is.True);
            Assert.That(run.MarkWon(), Is.False);
            Assert.That(run.Won && run.Solved, Is.True);
            Assert.That(run.Weaver, Is.Null, "the floor stops falling and the threads go");
            Assert.That(run.StepPortal(100, true), Is.False, "the exit portal sends people home one by one, not all at once");
        }
    }
}
