using System;
using System.Collections.Generic;
using System.Linq;
using FriendlyPersonShooter.Core.Bots;
using FriendlyPersonShooter.Core.World;
using NUnit.Framework;

namespace FriendlyPersonShooter.Core.Tests
{
    public class BotPopulationTests
    {
        private readonly WorldGrid _grid = new WorldGrid(16, 16, 30f);

        /// <summary>Runs the population without a server: every online bot is told it sits where it last was.</summary>
        private static void Run(BotPopulation pop, double from, double to, double step = 0.5)
        {
            for (double t = from; t <= to; t += step)
            {
                pop.Tick(t);
                foreach (var a in pop.Accounts)
                {
                    if (a.WantsOnline && !a.HasPosition) pop.ReportPosition(a.Seat, 100 + a.Seat, 100, 1);
                }
            }
        }

        [Test]
        public void Curve_PeaksAtPeakHour_AndBottomsOutTwelveHoursLater()
        {
            var s = new BotSimSettings();
            Assert.That(PopulationCurve.TargetOnline(s.PeakHour, s), Is.EqualTo(s.PeakOnline).Within(1e-9));
            Assert.That(PopulationCurve.TargetOnline(s.PeakHour - 12, s), Is.EqualTo(s.TroughOnline).Within(1e-9));
        }

        [Test]
        public void LoginRate_BalancesLogouts()
        {
            // 25% online with 1-hour sessions: 25 logouts an hour per 100 accounts = rate * 75 offline.
            double rate = PopulationCurve.LoginRatePerHour(0.25, 1.0);
            Assert.That(rate * 75, Is.EqualTo(25).Within(1e-9));
        }

        [Test]
        public void OnlineShare_FollowsTheDailyCurve()
        {
            var settings = new BotSimSettings();
            var pop = new BotPopulation(400, _grid, settings, seed: 3);
            double day = 24 * 3600 / settings.TimeScale;
            double t = 0;
            var errors = new List<double>();
            for (int hour = 0; hour < 48; hour++)
            {
                double next = t + day / 24;
                Run(pop, t, next);
                t = next + 0.5;
                if (hour < 4) continue; // let the starting population settle
                errors.Add(Math.Abs((double)pop.OnlineCount / 400 - pop.TargetShare(t)));
            }
            Assert.That(errors.Average(), Is.LessThan(0.03), "average gap from the target share");
            Assert.That(errors.Max(), Is.LessThan(0.08), "worst gap from the target share");
            Assert.That(pop.Stats.Logouts, Is.GreaterThan(400));
            Assert.That(pop.Stats.PartyLogins + pop.Stats.FriendJoins, Is.GreaterThan(50));
            Assert.That(pop.Stats.Drops, Is.GreaterThan(0));
            Assert.That(pop.Stats.Reconnects, Is.GreaterThan(0));
        }

        [Test]
        public void Parties_StayWithinSize_AndOnlyHoldCommittedPlayers()
        {
            var pop = new BotPopulation(200, _grid, new BotSimSettings(), seed: 5);
            int checks = 0, partied = 0;
            for (double t = 0; t < 900; t += 0.5)
            {
                Run(pop, t, t);
                if ((int)(t * 2) % 20 != 0) continue;
                foreach (var a in pop.Accounts)
                {
                    if (a.Party == null) continue;
                    checks++;
                    Assert.That(a.Party.Members, Does.Contain(a));
                    Assert.That(a.Party.Members.Count, Is.InRange(2, 4));
                    Assert.That(a.WantsOnline || a.ReconnectAt >= 0, Is.True, "party member who logged off for good");
                }
                pop.PartyCount(out int inParties);
                partied = Math.Max(partied, inParties);
            }
            Assert.That(checks, Is.GreaterThan(0));
            Assert.That(partied, Is.GreaterThan(10));
        }

        [Test]
        public void SameSeed_ReplaysTheSameRun()
        {
            string Trace(int seed)
            {
                var pop = new BotPopulation(100, _grid, new BotSimSettings(), seed);
                Run(pop, 0, 600);
                return string.Join(",", pop.Accounts.Select(a => $"{a.WantsOnline}:{a.PartyId}")) +
                       $"|{pop.Stats.Logins}|{pop.Stats.Drops}";
            }
            Assert.That(Trace(9), Is.EqualTo(Trace(9)));
            Assert.That(Trace(9), Is.Not.EqualTo(Trace(10)));
        }

        [Test]
        public void SoloStrangers_InTheSameLobbyAndZone_TeamUp()
        {
            var settings = new BotSimSettings
            {
                StrangerPartyPerHour = 1000, PartyLoginChance = 0, JoinFriendChance = 0, DropPerHour = 0,
                TroughOnline = 0.99, PeakOnline = 0.99, MinSessionMinutes = 600, MaxSessionMinutes = 600,
            };
            var pop = new BotPopulation(2, _grid, settings, seed: 1);
            pop.Tick(0);
            pop.Tick(settings.PrewarmSeconds + 1);
            Assume.That(pop.Accounts.All(a => a.WantsOnline));
            pop.ReportPosition(0, 10, 10, lobbyId: 7);
            pop.ReportPosition(1, 15, 12, lobbyId: 7);
            pop.Tick(settings.PrewarmSeconds + 5);
            Assert.That(pop.Accounts[0].Party, Is.Not.Null);
            Assert.That(pop.Accounts[0].Party, Is.SameAs(pop.Accounts[1].Party));
            Assert.That(pop.Stats.StrangerParties, Is.EqualTo(1));
        }

        [Test]
        public void StrangersInDifferentLobbies_NeverTeamUp()
        {
            var settings = new BotSimSettings
            {
                StrangerPartyPerHour = 1000, PartyLoginChance = 0, JoinFriendChance = 0, DropPerHour = 0,
                TroughOnline = 0.99, PeakOnline = 0.99, MinSessionMinutes = 600, MaxSessionMinutes = 600,
            };
            var pop = new BotPopulation(2, _grid, settings, seed: 1);
            pop.Tick(0);
            pop.Tick(settings.PrewarmSeconds + 1);
            pop.ReportPosition(0, 10, 10, lobbyId: 7);
            pop.ReportPosition(1, 15, 12, lobbyId: 8);
            pop.Tick(settings.PrewarmSeconds + 5);
            Assert.That(pop.Accounts[0].Party, Is.Null);
        }
    }

    public class BotMindTests
    {
        private readonly WorldGrid _grid = new WorldGrid(16, 16, 30f);

        /// <summary>Two bots online for a long time and partied as friends.</summary>
        private BotPopulation Party(out BotAccount leader, out BotAccount member)
        {
            var settings = new BotSimSettings
            {
                PartyLoginChance = 0, JoinFriendChance = 1, DropPerHour = 0, LeavePartyPerHour = 0,
                StrangerPartyPerHour = 0, TroughOnline = 0.99, PeakOnline = 0.99,
                MinSessionMinutes = 600, MaxSessionMinutes = 600, FriendsPerAccount = 1, PrewarmSeconds = 0,
            };
            var pop = new BotPopulation(2, _grid, settings, seed: 2);
            pop.Tick(0);
            pop.Tick(0.1);
            Assume.That(pop.Accounts[0].Party, Is.Not.Null, "the second login should join the first");
            leader = pop.Accounts[0].Party.Leader;
            member = pop.Accounts[0].Party.Members[1];
            return pop;
        }

        [Test]
        public void Member_WalksTowardTheLeader()
        {
            var pop = Party(out var leader, out var member);
            pop.ReportPosition(leader.Seat, 200, 200, 1);
            pop.ReportPosition(member.Seat, 150, 200, 1);
            var (h, v) = pop.Steer(member.Seat, 1);
            Assert.That(h, Is.EqualTo(1));
            Assert.That(v, Is.EqualTo(0));
            Assert.That(member.Activity, Is.EqualTo(BotActivity.Follow));
        }

        [Test]
        public void Member_TakesTheShortWayAroundTheWrap()
        {
            var pop = Party(out var leader, out var member);
            // World is 480 m wide: from x=470 the leader at x=20 is 30 m to the right, across the edge.
            pop.ReportPosition(leader.Seat, 20, 200, 1);
            pop.ReportPosition(member.Seat, 470, 200, 1);
            var (h, _) = pop.Steer(member.Seat, 1);
            Assert.That(h, Is.EqualTo(1));
        }

        [Test]
        public void Leader_WaitsForAMemberFarBehind()
        {
            var pop = Party(out var leader, out var member);
            pop.ReportPosition(leader.Seat, 200, 200, 1);
            pop.ReportPosition(member.Seat, 200, 100, 1);
            for (double t = 1; t < 3; t += 0.25)
            {
                Assert.That(pop.Steer(leader.Seat, t), Is.EqualTo((0, 0)));
            }
        }

        [Test]
        public void SoloPlayer_KeepsMovingOverTime()
        {
            var pop = Party(out var leader, out _);
            pop.ReportPosition(leader.Seat, 200, 200, 1);
            int moving = 0;
            for (double t = 1; t < 300; t += 0.1)
            {
                var (h, v) = pop.Steer(leader.Seat, t);
                if (h != 0 || v != 0) moving++;
            }
            Assert.That(moving, Is.GreaterThan(300), "should walk for a good part of five minutes");
        }

        [Test]
        public void Steering_UsesTheClosestOfEightDirections()
        {
            var pop = Party(out var leader, out _);
            pop.ReportPosition(leader.Seat, 0, 0, 1);
            Assert.That(BotMind.Toward(pop, leader, 10, 10, 1), Is.EqualTo((1, 1)));
            Assert.That(BotMind.Toward(pop, leader, 10, 1, 1), Is.EqualTo((1, 0)));
            Assert.That(BotMind.Toward(pop, leader, -1, -10, 1), Is.EqualTo((0, -1)));
            Assert.That(BotMind.Toward(pop, leader, 0.5, 0.5, 1), Is.EqualTo((0, 0)));
        }
    }
}
