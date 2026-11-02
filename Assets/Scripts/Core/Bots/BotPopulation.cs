using System;
using System.Collections.Generic;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Bots
{
    /// <summary>Running totals for a bot population, for logs and reports.</summary>
    public sealed class BotPopulationStats
    {
        public int Logins;
        public int Logouts;
        public int Drops;
        public int Reconnects;
        /// <summary>Logins that brought offline friends along.</summary>
        public int PartyLogins;
        /// <summary>Logins that joined a friend who was already online.</summary>
        public int FriendJoins;
        /// <summary>Strangers who met in a zone and teamed up.</summary>
        public int StrangerParties;
        /// <summary>Members who quit their party but kept playing.</summary>
        public int PartyLeaves;
        /// <summary>Chaos bots jumping across the world.</summary>
        public int Teleports;
    }

    /// <summary>
    /// Decides when simulated players log on and off, who they party with, and where they walk.
    /// Follows a daily population curve, with log-normal session lengths, friends who log in together,
    /// strangers who team up, dropped connections and a mix of activities. Engine-free: the game feeds
    /// it real time and each bot's position, and asks it for each bot's movement.
    /// </summary>
    public sealed class BotPopulation
    {
        public BotSimSettings Settings { get; }
        public WorldGrid Grid { get; }
        public IReadOnlyList<BotAccount> Accounts => _accounts;
        public BotPopulationStats Stats { get; } = new BotPopulationStats();

        /// <summary>Gathering points (world x, z) such as gates and raid doors.</summary>
        public IReadOnlyList<(double X, double Z)> Hotspots => _hotspots;

        public double WorldWidth => Grid.Width * Grid.ZoneSize;
        public double WorldHeight => Grid.Height * Grid.ZoneSize;

        private readonly List<BotAccount> _accounts = new List<BotAccount>();
        private readonly List<(double X, double Z)> _hotspots = new List<(double, double)>();
        private readonly Random _random;
        private readonly int _partyIdBase;
        private int _nextPartyId = 1;
        private bool _started;
        private double _lastTick;
        private double _nextStrangerCheck;
        private readonly List<BotAccount> _scratch = new List<BotAccount>();

        public BotPopulation(int seats, WorldGrid grid, BotSimSettings settings, int seed, int accountBase = 1000)
        {
            if (seats < 0) throw new ArgumentOutOfRangeException(nameof(seats));
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _random = new Random(seed);
            _partyIdBase = accountBase * 1000;

            for (int i = 0; i < seats; i++)
            {
                var account = new BotAccount(i, accountBase + i + 1, _random.Next());
                double roll = _random.NextDouble();
                if (roll < settings.ChaosShare) account.Profile = BotProfile.Chaos;
                else if (roll < settings.ChaosShare + settings.GrouperShare) account.Profile = BotProfile.Grouper;
                _accounts.Add(account);
            }
            BuildFriendships();
            BuildHotspots();
        }

        /// <summary>Game hour of day (0-24) at a real time.</summary>
        public double HourAt(double now)
        {
            double h = (Settings.StartHour + now * Settings.TimeScale / 3600.0) % 24.0;
            return h < 0 ? h + 24.0 : h;
        }

        /// <summary>Real seconds that last the given number of game minutes.</summary>
        public double GameMinutes(double minutes) => minutes * 60.0 / Settings.TimeScale;

        public double TargetShare(double now) => PopulationCurve.TargetOnline(HourAt(now), Settings);

        public int OnlineCount
        {
            get
            {
                int n = 0;
                foreach (var a in _accounts) if (a.WantsOnline) n++;
                return n;
            }
        }

        /// <summary>Parties with at least two members online.</summary>
        public int PartyCount(out int playersInParties)
        {
            playersInParties = 0;
            int parties = 0;
            var seen = new HashSet<int>();
            foreach (var a in _accounts)
            {
                if (a.Party == null || !seen.Add(a.Party.Id)) continue;
                int online = 0;
                foreach (var m in a.Party.Members) if (m.WantsOnline) online++;
                if (online < 2) continue;
                parties++;
                playersInParties += online;
            }
            return parties;
        }

        /// <summary>Advances logins, logouts, drops and party changes to real time <paramref name="now"/> (seconds).</summary>
        public void Tick(double now)
        {
            if (!_started)
            {
                _started = true;
                _lastTick = now;
                _nextStrangerCheck = now;
                Prewarm(now);
                return;
            }
            double dt = now - _lastTick;
            if (dt <= 0) return;
            _lastTick = now;
            double dtHours = dt * Settings.TimeScale / 3600.0;

            foreach (var a in _accounts)
            {
                if (a.WantsOnline)
                {
                    if (now >= a.SessionEnd) EndSession(a, now);
                    else if (Chance(a.Profile == BotProfile.Chaos ? Settings.ChaosDropPerHour : Settings.DropPerHour, dtHours))
                    {
                        Drop(a, now);
                    }
                    else if (a.Profile == BotProfile.Chaos && a.Party == null && a.HasPosition &&
                             Chance(Settings.ChaosTeleportPerHour, dtHours))
                    {
                        Teleport(a, _random.NextDouble() * WorldWidth, _random.NextDouble() * WorldHeight);
                    }
                    else if (a.Party != null && !a.IsPartyLeader && Chance(Settings.LeavePartyPerHour, dtHours))
                    {
                        LeaveParty(a);
                        Stats.PartyLeaves++;
                    }
                }
                else if (a.ReconnectAt >= 0)
                {
                    if (now >= a.SessionEnd)
                    {
                        // Gave up on the game while disconnected.
                        a.ReconnectAt = -1;
                        LeaveParty(a);
                        Stats.Logouts++;
                    }
                    else if (now >= a.ReconnectAt)
                    {
                        a.ReconnectAt = -1;
                        a.WantsOnline = true;
                        Stats.Reconnects++;
                    }
                }
                else if (a.LoginAt >= 0 && now >= a.LoginAt)
                {
                    a.LoginAt = -1;
                    var host = a.LoginHost;
                    a.LoginHost = null;
                    Login(a, now, host);
                }
            }

            // New logins: an open-loop rate that holds the target share, nudged toward it.
            double target = TargetShare(now);
            int committed = 0;
            foreach (var a in _accounts) if (a.IsCommitted) committed++;
            double share = _accounts.Count == 0 ? 0 : (double)committed / _accounts.Count;
            double rate = PopulationCurve.LoginRatePerHour(target, MeanSessionHours) / PlayersPerLogin;
            double feedback = Math.Max(0.1, Math.Min(6.0, 1.0 + 12.0 * (target - share)));
            foreach (var a in _accounts)
            {
                if (!a.IsCommitted && Chance(rate * feedback, dtHours)) Login(a, now, null);
            }

            if (now >= _nextStrangerCheck)
            {
                double interval = GameMinutes(1);
                _nextStrangerCheck = now + interval;
                FormStrangerParties(interval * Settings.TimeScale / 3600.0);
            }
        }

        /// <summary>The server says where a bot is and which lobby it's in.</summary>
        public void ReportPosition(int seat, double x, double z, int lobbyId)
        {
            var a = _accounts[seat];
            if (!a.WantsOnline) return;
            a.X = x;
            a.Z = z;
            a.LobbyId = lobbyId;
            a.Visited.Add(Grid.ZoneAt(x, z));
            if (!a.HasPosition)
            {
                a.HasPosition = true;
                a.LastSteer = -1;
            }
        }

        /// <summary>The movement input (-1, 0 or 1 on each axis) a bot sends this frame.</summary>
        public (int H, int V) Steer(int seat, double now) => BotMind.Steer(this, _accounts[seat], now);

        internal BotParty NewParty() => new BotParty(_partyIdBase + _nextPartyId++);

        internal double MeanSessionHours =>
            Settings.MedianSessionMinutes * Math.Exp(Settings.SessionSpread * Settings.SessionSpread / 2.0) / 60.0;

        /// <summary>Average players per login event, counting friends brought along.</summary>
        private double PlayersPerLogin
        {
            get
            {
                var w = Settings.PartySizeWeights;
                double total = 0, sum = 0;
                for (int i = 0; i < w.Length; i++)
                {
                    total += w[i];
                    sum += w[i] * (i + 2);
                }
                double meanParty = total > 0 ? sum / total : 1;
                return 1.0 + Settings.PartyLoginChance * (meanParty - 1.0);
            }
        }

        private void Prewarm(double now)
        {
            double target = TargetShare(now);
            foreach (var a in _accounts)
            {
                if (_random.NextDouble() >= target) continue;
                a.LoginAt = now + _random.NextDouble() * Settings.PrewarmSeconds;
            }
        }

        private void Login(BotAccount a, double now, BotAccount host)
        {
            Stats.Logins++;
            a.WantsOnline = true;
            a.HasPosition = false;
            a.Activity = BotActivity.Offline;
            a.Visited.Clear();

            // Brought along by a friend: play together for about as long as they do.
            if (host != null && host.WantsOnline && HasRoom(host))
            {
                a.SessionEnd = Math.Max(now + GameMinutes(Settings.MinSessionMinutes),
                    host.SessionEnd + GameMinutes((_random.NextDouble() - 0.5) * 20));
                JoinParty(a, host);
                return;
            }

            a.SessionEnd = now + GameMinutes(SampleSessionMinutes());

            bool grouper = a.Profile == BotProfile.Grouper;
            var friend = PickOnlineFriend(a);
            if (friend != null && (grouper || _random.NextDouble() < Settings.JoinFriendChance))
            {
                JoinParty(a, friend);
                Stats.FriendJoins++;
                return;
            }

            if (_random.NextDouble() < (grouper ? 0.8 : Settings.PartyLoginChance))
            {
                int size = SamplePartySize();
                _scratch.Clear();
                foreach (int seat in a.Friends)
                {
                    if (!_accounts[seat].IsCommitted) _scratch.Add(_accounts[seat]);
                }
                Shuffle(_scratch);
                int brought = Math.Min(size - 1, _scratch.Count);
                for (int i = 0; i < brought; i++)
                {
                    var f = _scratch[i];
                    f.LoginAt = now + _random.NextDouble() * GameMinutes(Settings.FriendLoginDelayMinutes);
                    f.LoginHost = a;
                }
                if (brought > 0) Stats.PartyLogins++;
            }
        }

        private void EndSession(BotAccount a, double now)
        {
            Stats.Logouts++;
            var party = a.Party;
            bool wasLeader = a.IsPartyLeader;
            var others = party != null ? new List<BotAccount>(party.Members) : null;
            LeaveParty(a);
            GoOffline(a);

            if (wasLeader && others != null)
            {
                foreach (var m in others)
                {
                    if (m == a || !m.WantsOnline) continue;
                    if (_random.NextDouble() < Settings.LeaveWithLeaderChance)
                    {
                        m.SessionEnd = Math.Min(m.SessionEnd, now + _random.NextDouble() * GameMinutes(2));
                    }
                }
            }
        }

        private void Drop(BotAccount a, double now)
        {
            Stats.Drops++;
            GoOffline(a);
            // The party keeps the slot; the server puts them back where they were.
            double min = a.Profile == BotProfile.Chaos ? Settings.ChaosReconnectMinMinutes : Settings.ReconnectMinMinutes;
            double max = a.Profile == BotProfile.Chaos ? Settings.ChaosReconnectMaxMinutes : Settings.ReconnectMaxMinutes;
            a.ReconnectAt = now + GameMinutes(min + _random.NextDouble() * (max - min));
        }

        private void Teleport(BotAccount a, double x, double z)
        {
            Stats.Teleports++;
            a.TeleportPending = true;
            a.TeleportX = x;
            a.TeleportZ = z;
            // Assume the jump worked until the server says otherwise, and start fresh from there.
            a.X = x;
            a.Z = z;
            a.Activity = BotActivity.Offline;
        }

        /// <summary>Takes a teleport the game should perform for this bot, if one is waiting.</summary>
        public bool TryTakeTeleport(int seat, out double x, out double z)
        {
            var a = _accounts[seat];
            x = a.TeleportX;
            z = a.TeleportZ;
            if (!a.TeleportPending || !a.WantsOnline) return false;
            a.TeleportPending = false;
            return true;
        }

        private static void GoOffline(BotAccount a)
        {
            a.WantsOnline = false;
            a.HasPosition = false;
            a.LobbyId = 0;
            a.Activity = BotActivity.Offline;
            a.MoveH = 0;
            a.MoveV = 0;
        }

        private bool HasRoom(BotAccount host) => host.Party == null || host.Party.Members.Count < Settings.MaxPartySize;

        private void JoinParty(BotAccount joiner, BotAccount host)
        {
            if (joiner.Party != null && joiner.Party == host.Party) return;
            LeaveParty(joiner);
            if (host.Party == null)
            {
                host.Party = NewParty();
                host.Party.Members.Add(host);
            }
            host.Party.Members.Add(joiner);
            joiner.Party = host.Party;
        }

        private static void LeaveParty(BotAccount a)
        {
            var party = a.Party;
            if (party == null) return;
            party.Members.Remove(a);
            a.Party = null;
            if (party.Members.Count == 1)
            {
                party.Members[0].Party = null;
                party.Members.Clear();
            }
        }

        private BotAccount PickOnlineFriend(BotAccount a)
        {
            _scratch.Clear();
            foreach (int seat in a.Friends)
            {
                var f = _accounts[seat];
                if (f.WantsOnline && HasRoom(f)) _scratch.Add(f);
            }
            return _scratch.Count == 0 ? null : _scratch[_random.Next(_scratch.Count)];
        }

        /// <summary>Solo players who share a lobby and a zone sometimes team up.</summary>
        private void FormStrangerParties(double dtHours)
        {
            var buckets = new Dictionary<(int, ZoneCoord), List<BotAccount>>();
            foreach (var a in _accounts)
            {
                if (!a.WantsOnline || !a.HasPosition || a.LobbyId == 0) continue;
                // Solo players, and Grouper-led parties with room, can team up.
                if (a.Party != null && !(a.IsPartyLeader && a.Profile == BotProfile.Grouper && HasRoom(a))) continue;
                var key = (a.LobbyId, Grid.ZoneAt(a.X, a.Z));
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<BotAccount>();
                    buckets[key] = list;
                }
                list.Add(a);
            }
            foreach (var list in buckets.Values)
            {
                // Groupers go first so they become the host.
                list.Sort((x, y) => (y.Profile == BotProfile.Grouper).CompareTo(x.Profile == BotProfile.Grouper));
                for (int i = 0; i + 1 < list.Count; i += 2)
                {
                    var host = list[i];
                    var guest = list[i + 1];
                    if (guest.Party != null) continue; // never merge two parties
                    bool grouper = host.Profile == BotProfile.Grouper || guest.Profile == BotProfile.Grouper;
                    double rate = Settings.StrangerPartyPerHour * (grouper ? Settings.GrouperInviteMultiplier : 1);
                    if (!Chance(rate, dtHours)) continue;
                    if (guest.Profile == BotProfile.Grouper && host.Profile != BotProfile.Grouper) (host, guest) = (guest, host);
                    if (!HasRoom(host)) continue;
                    JoinParty(guest, host);
                    Stats.StrangerParties++;
                }
            }
        }

        private void BuildFriendships()
        {
            int n = _accounts.Count;
            if (n < 2) return;
            int want = Math.Min(Settings.FriendsPerAccount, n - 1);
            // Mostly friends with nearby seat numbers (friend circles), plus some random links.
            foreach (var a in _accounts)
            {
                int attempts = 0;
                while (a.Friends.Count < want && attempts++ < want * 10)
                {
                    int other = _random.NextDouble() < 0.7
                        ? (a.Seat + _random.Next(1, 6)) % n
                        : _random.Next(n);
                    if (other == a.Seat || a.Friends.Contains(other)) continue;
                    if (_accounts[other].Friends.Count >= want * 2) continue;
                    a.Friends.Add(other);
                    _accounts[other].Friends.Add(a.Seat);
                }
            }
        }

        private void BuildHotspots()
        {
            int zones = Grid.Width * Grid.Height;
            int count = Math.Max(1, (int)Math.Round(zones * Settings.HotspotsPerZone));
            var used = new HashSet<ZoneCoord>();
            int attempts = 0;
            while (_hotspots.Count < count && attempts++ < count * 20)
            {
                var zone = new ZoneCoord(_random.Next(Grid.Width), _random.Next(Grid.Height));
                if (!used.Add(zone)) continue;
                _hotspots.Add(((zone.X + 0.5) * Grid.ZoneSize, (zone.Y + 0.5) * Grid.ZoneSize));
            }
        }

        private double SampleSessionMinutes()
        {
            // Box-Muller normal, then log-normal around the median.
            double u1 = 1.0 - _random.NextDouble();
            double u2 = _random.NextDouble();
            double normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            double minutes = Settings.MedianSessionMinutes * Math.Exp(Settings.SessionSpread * normal);
            return Math.Max(Settings.MinSessionMinutes, Math.Min(Settings.MaxSessionMinutes, minutes));
        }

        private int SamplePartySize()
        {
            var w = Settings.PartySizeWeights;
            double total = 0;
            foreach (double x in w) total += x;
            double r = _random.NextDouble() * total;
            for (int i = 0; i < w.Length; i++)
            {
                r -= w[i];
                if (r < 0) return Math.Min(i + 2, Settings.MaxPartySize);
            }
            return Math.Min(w.Length + 1, Settings.MaxPartySize);
        }

        private bool Chance(double ratePerHour, double dtHours)
        {
            return _random.NextDouble() < 1.0 - Math.Exp(-ratePerHour * dtHours);
        }

        private void Shuffle(List<BotAccount> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
