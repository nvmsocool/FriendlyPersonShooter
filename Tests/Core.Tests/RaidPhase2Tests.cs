using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FriendlyPersonShooter.Core.Expedition;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    /// <summary>Raid runs, phase 2: Echo Runes, Hold the Core, the Twins, and room layout variants.</summary>
    public class RaidPhase2Tests
    {
        private static double AfterShow(EchoRunes r, double now) =>
            now + EchoRunes.ShowLead + r.Length * (EchoRunes.ShowLit + EchoRunes.ShowGap) + 0.01;

        [Test]
        public void EchoRunesShowThenTakeTheSequenceBack()
        {
            var r = new EchoRunes(5, 4, 0);
            Assert.That(r.Length, Is.EqualTo(3));
            Assert.That(r.Showing(0, 0.1), Is.True);
            Assert.That(r.Lit(0, 0.1), Is.EqualTo(-1), "a short pause before the first rune");
            Assert.That(r.Lit(0, EchoRunes.ShowLead + 0.1), Is.EqualTo(r.SequenceAt(0, 0)));
            Assert.That(r.Lit(1, EchoRunes.ShowLead + EchoRunes.ShowLit + EchoRunes.ShowGap + 0.1), Is.EqualTo(r.SequenceAt(1, 1)));
            Assert.That(r.Hit(0, r.SequenceAt(0, 0), 0.5), Is.EqualTo(RuneHit.Ignored), "hits during the show don't count");

            double t = AfterShow(r, 0);
            Assert.That(r.Hit(0, r.SequenceAt(0, 0), t), Is.EqualTo(RuneHit.Progress));
            Assert.That(r.Hit(0, r.SequenceAt(0, 0), t + 0.1), Is.EqualTo(RuneHit.Ignored), "a second shot on the same rune");
            Assert.That(r.Hit(0, r.SequenceAt(0, 1), t + 0.2), Is.EqualTo(RuneHit.Progress));
            Assert.That(r.Hit(0, r.SequenceAt(0, 2), t + 0.3), Is.EqualTo(RuneHit.SetDone));
            Assert.That(r.Hit(0, 0, t + 0.4), Is.EqualTo(RuneHit.Ignored), "a finished set waits");
            Assert.That(r.Progress, Is.EqualTo(0.5f / 3f).Within(1e-4));

            int wrong = (r.SequenceAt(1, 0) + 1) % EchoRunes.RunesPerSet;
            Assert.That(r.Hit(1, wrong, t + 0.5), Is.EqualTo(RuneHit.Wrong));
            Assert.That(r.Showing(1, t + 0.6), Is.True, "the set shows its sequence again");
            Assert.That(r.Done(0), Is.True, "the other set keeps its progress");
        }

        [Test]
        public void EchoRunesSolveAfterThreeLongerRoundsOfBothSets()
        {
            var r = new EchoRunes(11, 8, 0);
            Assert.That(r.BaseLength, Is.EqualTo(4), "bigger groups get longer sequences");
            double now = 0;
            for (int round = 0; round < EchoRunes.Rounds; round++)
            {
                Assert.That(r.Length, Is.EqualTo(4 + round));
                now = AfterShow(r, now);
                RuneHit last = RuneHit.Ignored;
                int length = r.Length;
                for (int s = 0; s < EchoRunes.Sets; s++)
                {
                    for (int i = 0; i < length; i++)
                    {
                        if (i > 0) Assert.That(r.SequenceAt(s, i), Is.Not.EqualTo(r.SequenceAt(s, i - 1)));
                        last = r.Hit(s, r.SequenceAt(s, i), now += 0.3);
                    }
                }
                Assert.That(last, Is.EqualTo(round == EchoRunes.Rounds - 1 ? RuneHit.Solved : RuneHit.RoundDone));
            }
            Assert.That(r.Solved, Is.True);
            Assert.That(r.Mistakes, Is.Zero);
        }

        [Test]
        public void EchoRunesReplayAfterALongPauseAndPackForTheHud()
        {
            var r = new EchoRunes(3, 4, 0);
            double t = AfterShow(r, 0);
            r.Hit(1, r.SequenceAt(1, 0), t);
            Assert.That(r.Step(t + EchoRunes.HitWindow - 0.1), Is.False);
            Assert.That(r.Step(t + EchoRunes.HitWindow + 0.1), Is.True, "both sets waited too long");
            Assert.That(r.IndexOf(1), Is.Zero);

            double show = t + EchoRunes.HitWindow + 0.1 + EchoRunes.ShowLead + 0.1;
            int packed = r.Pack(show);
            Assert.That(EchoRunes.LengthOf(packed), Is.EqualTo(3));
            Assert.That(EchoRunes.ShowingOf(packed, 0), Is.True);
            Assert.That(EchoRunes.LitOf(packed, 1), Is.EqualTo(r.SequenceAt(1, 0)));
            Assert.That(EchoRunes.DoneOf(packed, 0), Is.False);
        }

        [Test]
        public void TheCoreSendsWavesAndStartsOverWhenItBreaks()
        {
            var c = new HoldCore(4, false, 0);
            Assert.That(c.WaveSize, Is.EqualTo(3));
            Assert.That(new HoldCore(4, true, 0).WaveSize, Is.EqualTo(5), "Swarming adds half again");
            Assert.That(c.Step(1), Is.EqualTo(CoreEvent.None));
            Assert.That(c.Step(HoldCore.FirstWave), Is.EqualTo(CoreEvent.Wave));
            Assert.That(c.Step(HoldCore.FirstWave + 1), Is.EqualTo(CoreEvent.None));
            Assert.That(c.Step(HoldCore.FirstWave + HoldCore.WaveEvery), Is.EqualTo(CoreEvent.Wave));

            int bursts = 0;
            while (!c.Burst()) bursts++;
            Assert.That(bursts + 1, Is.EqualTo((int)System.Math.Ceiling(HoldCore.CoreHealth / HoldCore.RunnerDamage)));
            Assert.That(c.Step(20), Is.EqualTo(CoreEvent.Broke));
            Assert.That(c.HealthFraction, Is.EqualTo(1f));
            Assert.That(c.Attempts, Is.EqualTo(2));
            Assert.That(c.TimeLeft(20), Is.EqualTo(HoldCore.Duration));

            Assert.That(c.Step(20 + HoldCore.Duration), Is.EqualTo(CoreEvent.Solved));
            Assert.That(c.Progress(100), Is.EqualTo(1f));
            Assert.That(c.Burst(), Is.False);
            int packed = new HoldCore(4, false, 0).Pack(10);
            Assert.That(HoldCore.HealthOf(packed), Is.EqualTo(100));
            Assert.That(HoldCore.SecondsOf(packed), Is.EqualTo(50));
        }

        [Test]
        public void TheTwinsMustDieTogether()
        {
            var t = new RaidTwins();
            Assert.That(t.Killed(0, 100), Is.EqualTo(TwinEvent.FirstDown));
            Assert.That(t.Waiting, Is.EqualTo(0));
            Assert.That(t.TimeLeft(104), Is.EqualTo(6).Within(1e-6));
            Assert.That(t.Step(109.9, out _), Is.EqualTo(TwinEvent.None));
            Assert.That(t.Step(110, out int revived), Is.EqualTo(TwinEvent.Revived));
            Assert.That(revived, Is.EqualTo(0));
            Assert.That(t.IsDown(0), Is.False);

            Assert.That(t.Killed(1, 120), Is.EqualTo(TwinEvent.FirstDown));
            Assert.That(t.Killed(1, 121), Is.EqualTo(TwinEvent.None), "already down");
            Assert.That(t.Killed(0, 129), Is.EqualTo(TwinEvent.Beaten));
            Assert.That(t.Beaten, Is.True);
            Assert.That(t.Step(200, out _), Is.EqualTo(TwinEvent.None));

            var waves = new[] { 0.75f, 0.5f, 0.25f };
            var w = new RaidTwins();
            Assert.That(w.TakeWaveDue(0.8f, waves), Is.Zero);
            Assert.That(w.TakeWaveDue(0.7f, waves), Is.EqualTo(1));
            Assert.That(w.TakeWaveDue(0.9f, waves), Is.Zero, "a revival doesn't call a wave again");
            Assert.That(w.TakeWaveDue(0.2f, waves), Is.EqualTo(2));
        }

        [Test]
        public void RunsPickTheBossAndTheNewRooms()
        {
            var bosses = new HashSet<RaidBossKind>();
            var rooms = new HashSet<RaidRoomKind>();
            for (uint seed = 1; seed < 80; seed++)
            {
                var run = new RaidRun(seed, 4);
                bosses.Add(run.Boss);
                foreach (var k in run.Rooms) rooms.Add(k);
            }
            Assert.That(bosses.Count, Is.EqualTo(3));
            Assert.That(rooms, Is.EquivalentTo(RaidRoomLayout.Pool));
            Assert.That(new RaidRun(1, 4, boss: RaidBossKind.Twins).Boss, Is.EqualTo(RaidBossKind.Twins));

            var r = new RaidRun(9, 4, first: RaidRoomKind.EchoRunes, boss: RaidBossKind.Twins);
            r.Begin(0);
            Assert.That(r.Runes, Is.Not.Null);
            Assert.That(r.RoomSeed, Is.Not.Zero);
            Assert.That(r.Layout.Seed, Is.EqualTo(r.RoomSeed));
            uint first = r.RoomSeed;
            r.MarkSolved();
            r.Advance(10);
            Assert.That(r.RoomSeed, Is.Not.EqualTo(first));
            r.Advance(20);
            r.Advance(30);
            Assert.That(r.AtBoss, Is.True);
            Assert.That(r.Twins, Is.Not.Null);
            Assert.That(r.StageName, Is.EqualTo("The Twins"));
        }

        [Test]
        public void SeedsMoveThingsButKeepTheRoomPlayable()
        {
            for (uint seed = 0; seed < 40; seed++)
            {
                foreach (var kind in RaidRoomLayout.Pool)
                {
                    var l = RaidRoomLayout.For(kind, seed);
                    Assert.That(l.Boxes, Is.Not.Empty);
                    Assert.That(l.Spawns.Count, Is.EqualTo(8));
                    foreach (var t in l.Targets) Assert.That(l.Contains(t), Is.True, $"{kind} seed {seed}: target inside");
                    foreach (var s in l.Spawns)
                    {
                        foreach (var b in l.Boxes)
                        {
                            if (b.Kind == RaidBoxKind.Floor || b.Kind == RaidBoxKind.Stone) continue;
                            Assert.That(b.Over(s.X, s.Z, 0.5f), Is.False, $"{kind} seed {seed}: a spawn inside a {b.Kind}");
                        }
                    }
                }

                var g = RaidRoomLayout.For(RaidRoomKind.Gauntlet, seed);
                Assert.That(g.Sweepers.Count, Is.EqualTo(4), $"seed {seed}");
                for (int i = 1; i < g.Path.Count; i++)
                {
                    var a = g.Path[i - 1];
                    var b = g.Path[i];
                    float gapZ = (b.Center.Z - b.Size.Z / 2f) - (a.Center.Z + a.Size.Z / 2f);
                    float gapX = System.Math.Max(0f, System.Math.Abs(b.Center.X - a.Center.X) - (a.Size.X + b.Size.X) / 2f);
                    Assert.That((float)System.Math.Sqrt(gapZ * gapZ + gapX * gapX), Is.InRange(0.8f, 2.6f), $"seed {seed} gap {i}");
                }

                var plates = RaidRoomLayout.For(RaidRoomKind.HoldPlates, seed).Targets;
                for (int i = 0; i < plates.Count; i++)
                {
                    for (int j = i + 1; j < plates.Count; j++)
                        Assert.That(Vector3.Distance(plates[i], plates[j]), Is.GreaterThan(12f), $"seed {seed}: plates apart");
                    Assert.That(Vector3.Distance(plates[i], RaidRoomLayout.For(RaidRoomKind.HoldPlates, seed).Portal), Is.GreaterThan(5f));
                }

                var runes = RaidRoomLayout.For(RaidRoomKind.EchoRunes, seed).Targets;
                Assert.That(runes.Count, Is.EqualTo(EchoRunes.Sets * EchoRunes.RunesPerSet));
                for (int i = 0; i < EchoRunes.RunesPerSet; i++)
                {
                    Assert.That(runes[i].X, Is.LessThan(-20f), "set 0 is on the west wall");
                    Assert.That(runes[EchoRunes.RunesPerSet + i].X, Is.GreaterThan(20f), "set 1 is on the east wall");
                }

                var core = RaidRoomLayout.For(RaidRoomKind.HoldCore, seed);
                Assert.That(core.Targets.Count, Is.EqualTo(1 + HoldCore.Gates));
                foreach (var b in core.Boxes)
                {
                    if (b.Kind != RaidBoxKind.Pillar || (b.Center.X == 0f && b.Center.Z == 0f)) continue;
                    for (int gate = 1; gate <= HoldCore.Gates; gate++)
                    {
                        var from = core.Targets[gate];
                        // Sample the runner's lane: no cover block on it.
                        for (float f = 0f; f <= 1f; f += 0.05f)
                        {
                            var p = from * (1f - f);
                            Assert.That(b.Over(p.X, p.Z, 0.8f), Is.False, $"seed {seed}: cover on the lane from gate {gate}");
                        }
                    }
                }
            }
            Assert.That(RaidRoomLayout.For(RaidRoomKind.ColorLocks, 3).Targets[0], Is.Not.EqualTo(RaidRoomLayout.For(RaidRoomKind.ColorLocks, 4).Targets[0]));
        }
    }
}
