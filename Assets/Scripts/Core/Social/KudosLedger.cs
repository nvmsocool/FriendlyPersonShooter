using System;
using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.Social
{
    /// <summary>The meaningful interaction a kudos thanks someone for.</summary>
    public enum KudosReason : byte
    {
        /// <summary>Shot your revive fragments.</summary>
        Revive = 0,
        /// <summary>Buffed you.</summary>
        Buff = 1,
        /// <summary>Linked a chain with you.</summary>
        Chain = 2,
        /// <summary>Picked up your cache and sent it back.</summary>
        CacheReturn = 3,
    }

    public enum KudosOutcome : byte
    {
        Given = 0,
        /// <summary>You can't kudos yourself.</summary>
        Self = 1,
        /// <summary>The receiver hasn't helped the giver recently.</summary>
        NoInteraction = 2,
        /// <summary>The giver already sent this receiver kudos within <see cref="KudosSettings.PairCooldown"/>.</summary>
        PairCooldown = 3,
        /// <summary>The giver has sent <see cref="KudosSettings.MaxGivenPerDay"/> kudos in the last day.</summary>
        DailyCap = 4,
    }

    /// <summary>Tunables for kudos. Seconds on the game clock (the bot test clock compresses a day).</summary>
    public sealed class KudosSettings
    {
        /// <summary>A game day: 24 hours.</summary>
        public double DayLength { get; set; } = 86400.0;

        /// <summary>
        /// One kudos per giver → receiver pair per this long (doc: "one kudos per player per day").
        /// Defaults to <see cref="DayLength"/> when zero or less.
        /// </summary>
        public double PairCooldown { get; set; } = 86400.0;

        /// <summary>
        /// Kudos only count after a real interaction: the receiver must have helped the giver
        /// within this long. 10 minutes covers "they revived me, then we fought together a bit".
        /// </summary>
        public double InteractionWindow { get; set; } = 600.0;

        /// <summary>
        /// Most kudos one player can send in a rolling day. Caps rings of alt accounts and
        /// keeps kudos meaningful; a normal evening of helping strangers stays well under it.
        /// </summary>
        public int MaxGivenPerDay { get; set; } = 20;

        /// <summary>
        /// Returning a cache sends the finder kudos from the owner automatically (doc: the finder
        /// "gets a small kudos reward"), under the same pair and daily rules.
        /// </summary>
        public bool AutoKudosForCacheReturn { get; set; } = true;
    }

    /// <summary>One kudos sent.</summary>
    public readonly struct KudosRecord
    {
        public readonly int Giver;
        public readonly int Receiver;
        public readonly KudosReason Reason;
        public readonly double Time;

        public KudosRecord(int giver, int receiver, KudosReason reason, double time)
        {
            Giver = giver;
            Receiver = receiver;
            Reason = reason;
            Time = time;
        }

        public override string ToString() => $"{Giver}->{Receiver} {Reason} at {Time:0}s";
    }

    /// <summary>
    /// Who sent kudos to whom and why. Anti-farming rules:
    /// <list type="number">
    /// <item>No kudos to yourself.</item>
    /// <item>Only after a real interaction: the server calls <see cref="NoteHelp"/> when someone
    /// revives, buffs, chains with, gifts to, returns a cache to, or tips another player, and the
    /// helped player may then send kudos within <see cref="KudosSettings.InteractionWindow"/>. Sending
    /// uses up that interaction.</item>
    /// <item>One kudos per giver → receiver pair per <see cref="KudosSettings.PairCooldown"/> (a day).</item>
    /// <item>At most <see cref="KudosSettings.MaxGivenPerDay"/> sent per giver per rolling day.</item>
    /// </list>
    /// Party members may kudos each other: helping a friend is still helping, and rules 2–3 already
    /// hold a pair of friends to one kudos each per day, which isn't worth farming.
    /// Kept per account on the backend.
    /// </summary>
    public sealed class KudosLedger
    {
        private readonly struct Interaction
        {
            public readonly KudosReason Reason;
            public readonly double Time;

            public Interaction(KudosReason reason, double time)
            {
                Reason = reason;
                Time = time;
            }
        }

        private readonly KudosSettings _settings;
        private readonly List<KudosRecord> _records = new List<KudosRecord>();
        // Key (helped, helper): the latest help the helped player can thank.
        private readonly Dictionary<long, Interaction> _interactions = new Dictionary<long, Interaction>();
        private readonly Dictionary<long, double> _lastGiven = new Dictionary<long, double>();
        private readonly Dictionary<long, int> _between = new Dictionary<long, int>();
        private readonly Dictionary<int, int> _received = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _given = new Dictionary<int, int>();
        private readonly Dictionary<int, List<double>> _givenTimes = new Dictionary<int, List<double>>();

        public KudosLedger(KudosSettings settings = null)
        {
            _settings = settings ?? new KudosSettings();
        }

        public KudosSettings Settings => _settings;

        /// <summary>Every kudos sent, oldest first.</summary>
        public IReadOnlyList<KudosRecord> All => _records;

        /// <summary>
        /// <paramref name="helper"/> just helped <paramref name="helped"/>; the helped player may now
        /// send them kudos. A newer interaction replaces an older one between the same two players.
        /// </summary>
        public void NoteHelp(int helper, int helped, KudosReason reason, double now)
        {
            if (helper == helped) return;
            _interactions[Key(helped, helper)] = new Interaction(reason, now);
        }

        /// <summary>Whether <paramref name="giver"/> may send <paramref name="receiver"/> kudos right now.</summary>
        public KudosOutcome CanGive(int giver, int receiver, double now)
        {
            if (giver == receiver) return KudosOutcome.Self;
            if (!TryGetInteraction(giver, receiver, now, out _)) return KudosOutcome.NoInteraction;
            if (_lastGiven.TryGetValue(Key(giver, receiver), out double last) && now - last < PairCooldown)
                return KudosOutcome.PairCooldown;
            if (_settings.MaxGivenPerDay > 0 && GivenInLastDay(giver, now) >= _settings.MaxGivenPerDay)
                return KudosOutcome.DailyCap;
            return KudosOutcome.Given;
        }

        /// <summary>Sends kudos if the rules allow, for the reason of the interaction being thanked.</summary>
        public KudosOutcome Give(int giver, int receiver, double now)
        {
            var outcome = CanGive(giver, receiver, now);
            if (outcome != KudosOutcome.Given) return outcome;
            TryGetInteraction(giver, receiver, now, out var interaction);
            _interactions.Remove(Key(giver, receiver));

            _records.Add(new KudosRecord(giver, receiver, interaction.Reason, now));
            _lastGiven[Key(giver, receiver)] = now;
            long pair = PairKey(giver, receiver);
            _between.TryGetValue(pair, out int b);
            _between[pair] = b + 1;
            _received.TryGetValue(receiver, out int r);
            _received[receiver] = r + 1;
            _given.TryGetValue(giver, out int g);
            _given[giver] = g + 1;
            if (!_givenTimes.TryGetValue(giver, out var times))
            {
                times = new List<double>();
                _givenTimes.Add(giver, times);
            }
            times.Add(now);
            return KudosOutcome.Given;
        }

        /// <summary>
        /// <paramref name="finder"/> returned <paramref name="owner"/>'s cache. Notes the help and, with
        /// <see cref="KudosSettings.AutoKudosForCacheReturn"/>, sends the owner's kudos at once.
        /// </summary>
        public KudosOutcome RecordCacheReturn(int finder, int owner, double now)
        {
            if (finder == owner) return KudosOutcome.Self;
            NoteHelp(finder, owner, KudosReason.CacheReturn, now);
            return _settings.AutoKudosForCacheReturn ? Give(owner, finder, now) : KudosOutcome.NoInteraction;
        }

        /// <summary>Players <paramref name="giver"/> could send kudos to right now, by id (for the HUD prompt).</summary>
        public IReadOnlyList<int> Eligible(int giver, double now)
        {
            var list = new List<int>();
            foreach (var pair in _interactions)
            {
                int helped = (int)(pair.Key >> 32);
                int helper = (int)(pair.Key & 0xFFFFFFFF);
                if (helped == giver && CanGive(giver, helper, now) == KudosOutcome.Given) list.Add(helper);
            }
            list.Sort();
            return list;
        }

        /// <summary>Lifetime kudos a player has received.</summary>
        public int ReceivedBy(int playerId) => _received.TryGetValue(playerId, out int n) ? n : 0;

        /// <summary>Lifetime kudos a player has sent.</summary>
        public int GivenBy(int playerId) => _given.TryGetValue(playerId, out int n) ? n : 0;

        /// <summary>Kudos a player has received for one reason.</summary>
        public int ReceivedFor(int playerId, KudosReason reason)
        {
            int n = 0;
            foreach (var r in _records)
            {
                if (r.Receiver == playerId && r.Reason == reason) n++;
            }
            return n;
        }

        /// <summary>Kudos sent either way between two players.</summary>
        public int KudosBetween(int playerA, int playerB) =>
            _between.TryGetValue(PairKey(playerA, playerB), out int n) ? n : 0;

        /// <summary>Every kudos between two players (either way), oldest first.</summary>
        public IReadOnlyList<KudosRecord> History(int playerA, int playerB)
        {
            var list = new List<KudosRecord>();
            foreach (var r in _records)
            {
                if ((r.Giver == playerA && r.Receiver == playerB) || (r.Giver == playerB && r.Receiver == playerA)) list.Add(r);
            }
            return list;
        }

        private double PairCooldown => _settings.PairCooldown > 0 ? _settings.PairCooldown : _settings.DayLength;

        private bool TryGetInteraction(int giver, int receiver, double now, out Interaction interaction)
        {
            if (_interactions.TryGetValue(Key(giver, receiver), out interaction) &&
                now - interaction.Time <= _settings.InteractionWindow && now >= interaction.Time)
                return true;
            interaction = default;
            return false;
        }

        private int GivenInLastDay(int giver, double now)
        {
            if (!_givenTimes.TryGetValue(giver, out var times)) return 0;
            times.RemoveAll(t => now - t >= _settings.DayLength);
            return times.Count;
        }

        /// <summary>Ordered key: (first, second).</summary>
        private static long Key(int first, int second) => ((long)first << 32) | (uint)second;

        /// <summary>Unordered key: the same for (a, b) and (b, a).</summary>
        private static long PairKey(int a, int b) => a <= b ? Key(a, b) : Key(b, a);
    }
}
