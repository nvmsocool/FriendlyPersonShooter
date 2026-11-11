using System;
using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.Social
{
    /// <summary>Something useful that passed between two strangers. Step 5 asks for info, buff, revive or group.</summary>
    public enum ExchangeKind : byte
    {
        /// <summary>Heard an info quick-chat line from the other.</summary>
        Info = 0,
        /// <summary>One hit the other with an ally shot (buff or chain).</summary>
        Buff = 1,
        /// <summary>One shot the other's revive fragments.</summary>
        Revive = 2,
        /// <summary>One returned the other's cache.</summary>
        CacheReturn = 3,
    }

    /// <summary>Tunables for measuring stranger encounters. Seconds.</summary>
    public sealed class ExchangeSettings
    {
        /// <summary>An exchange counts toward an encounter if it happens within this long of meeting.</summary>
        public double Window { get; set; } = 120.0;

        /// <summary>
        /// Two players who keep drifting in and out of view only start a new encounter this long
        /// after their last one began, so flapping view checks don't inflate the count.
        /// </summary>
        public double RemeetCooldown { get; set; } = 600.0;
    }

    /// <summary>Share of stranger encounters that led to an exchange, overall and by kind.</summary>
    public sealed class ExchangeReport
    {
        private readonly int[] _byKind;

        internal ExchangeReport(int encounters, int withExchange, int[] byKind)
        {
            Encounters = encounters;
            WithExchange = withExchange;
            _byKind = byKind;
        }

        /// <summary>Encounters that are settled: their window has closed, or they already had an exchange.</summary>
        public int Encounters { get; }
        public int WithExchange { get; }
        /// <summary>Step 5 passes when this is above one half ("most stranger encounters").</summary>
        public double Share => Encounters == 0 ? 0 : (double)WithExchange / Encounters;

        /// <summary>Encounters that had at least one exchange of this kind (an encounter can count under several kinds).</summary>
        public int CountOf(ExchangeKind kind) => _byKind[(int)kind];
        public double ShareOf(ExchangeKind kind) => Encounters == 0 ? 0 : (double)_byKind[(int)kind] / Encounters;

        public override string ToString()
        {
            var parts = new List<string>();
            foreach (ExchangeKind kind in Enum.GetValues(typeof(ExchangeKind)))
            {
                parts.Add($"{kind} {ShareOf(kind):P0}");
            }
            return $"{WithExchange}/{Encounters} encounters with an exchange ({Share:P0}): {string.Join(", ", parts)}";
        }
    }

    /// <summary>
    /// Measures step 5's pass criterion: of the encounters between strangers, how many led to an
    /// exchange within <see cref="ExchangeSettings.Window"/>. The server (or bot sim) calls
    /// <see cref="Met"/> when two players from different groups first come within view of each other,
    /// and <see cref="Exchange"/> for each useful thing that passes between any two players; exchanges
    /// between players with no open encounter are ignored. Pairs are unordered.
    /// </summary>
    public sealed class ExchangeTracker
    {
        private sealed class Encounter
        {
            public double Start;
            public bool[] Kinds = new bool[KindCount];
            public bool Any;
        }

        private static readonly int KindCount = Enum.GetValues(typeof(ExchangeKind)).Length;

        private readonly ExchangeSettings _settings;
        private readonly List<Encounter> _encounters = new List<Encounter>();
        private readonly Dictionary<long, Encounter> _latestByPair = new Dictionary<long, Encounter>();

        public ExchangeTracker(ExchangeSettings settings = null)
        {
            _settings = settings ?? new ExchangeSettings();
        }

        /// <summary>Every encounter started so far, settled or not.</summary>
        public int TotalEncounters => _encounters.Count;

        /// <summary>
        /// Two strangers came within view. Starts an encounter unless they had one that began less
        /// than <see cref="ExchangeSettings.RemeetCooldown"/> ago. Returns true if a new one started.
        /// </summary>
        public bool Met(int a, int b, double time)
        {
            if (a == b) return false;
            long key = PairKey(a, b);
            if (_latestByPair.TryGetValue(key, out var last) && time - last.Start < _settings.RemeetCooldown) return false;
            var encounter = new Encounter { Start = time };
            _encounters.Add(encounter);
            _latestByPair[key] = encounter;
            return true;
        }

        /// <summary>
        /// Something useful passed between two players. Counts toward their latest encounter if it is
        /// still inside the window. Returns true if it counted.
        /// </summary>
        public bool Exchange(int a, int b, ExchangeKind kind, double time)
        {
            if (a == b) return false;
            if (!_latestByPair.TryGetValue(PairKey(a, b), out var encounter)) return false;
            if (time < encounter.Start || time - encounter.Start > _settings.Window) return false;
            encounter.Kinds[(int)kind] = true;
            encounter.Any = true;
            return true;
        }

        /// <summary>
        /// A delivered quick-chat: every recipient who heard an info line from the sender
        /// (<see cref="QuickChatEntry.IsInfo"/>) counts an info exchange. Returns how many counted.
        /// </summary>
        public int RecordQuickChat(QuickChatSendResult result)
        {
            if (!result.Delivered || !QuickChatCatalog.IsInfo(result.Delivery.Message.Id)) return 0;
            int counted = 0;
            foreach (int recipient in result.Recipients)
            {
                if (Exchange(result.Delivery.SenderId, recipient, ExchangeKind.Info, result.Delivery.Time)) counted++;
            }
            return counted;
        }

        /// <summary>The report so far. Encounters still inside their window with no exchange yet aren't counted.</summary>
        public ExchangeReport Report(double now)
        {
            int settled = 0, with = 0;
            var byKind = new int[KindCount];
            foreach (var e in _encounters)
            {
                if (!e.Any && now - e.Start <= _settings.Window) continue;
                settled++;
                if (!e.Any) continue;
                with++;
                for (int k = 0; k < KindCount; k++)
                {
                    if (e.Kinds[k]) byKind[k]++;
                }
            }
            return new ExchangeReport(settled, with, byKind);
        }

        private static long PairKey(int a, int b) => a <= b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
    }
}
