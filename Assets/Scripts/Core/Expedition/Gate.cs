using System;
using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for a gate down to the next layer.</summary>
    public sealed class GateSettings
    {
        /// <summary>Gates cost one color in bulk.</summary>
        public EssenceColor Color { get; set; } = EssenceColor.Yellow;
        /// <summary>The first opening's price: 40 Yellow.</summary>
        public int Cost { get; set; } = 40;
        /// <summary>Each time the gate closes again, the next opening costs this much more.</summary>
        public int CostStep { get; set; } = 20;
        /// <summary>Seconds the gate stays open after someone pays, for the party to go through.</summary>
        public double OpenSeconds { get; set; } = 90.0;
    }

    public enum GateUnlockOutcome : byte
    {
        /// <summary>Paid; the player now has access.</summary>
        Unlocked = 0,
        /// <summary>The player already had access on this seed; nothing was spent.</summary>
        AlreadyOpen = 1,
        /// <summary>Not enough banked essence; nothing was spent.</summary>
        NotEnough = 2,
    }

    /// <summary>
    /// A gate down one layer. Access is personal: each player pays the cost once from their banked
    /// essence and can then pass for the rest of the seed. A group passes only when every member has
    /// access (gifting a friend the shortfall is the natural fix). Going back up is always free.
    /// Access is stored with the seed it was bought on, so it stops counting after a reseed.
    /// An opening lasts <see cref="GateSettings.OpenSeconds"/>; then the gate shuts, everyone's
    /// access ends, and the next opening costs <see cref="GateSettings.CostStep"/> more.
    /// </summary>
    public sealed class Gate
    {
        private readonly Dictionary<int, uint> _accessSeed = new Dictionary<int, uint>();
        private readonly int _baseCost, _costStep;
        private readonly double _openSeconds;
        private double? _closesAt;

        public Gate(GateSite site, uint seed, GateSettings settings = null)
        {
            Site = site ?? throw new ArgumentNullException(nameof(site));
            settings = settings ?? new GateSettings();
            if (settings.Cost < 0 || settings.CostStep < 0) throw new ArgumentOutOfRangeException(nameof(settings), "Cost can't be negative.");
            Color = settings.Color;
            _baseCost = settings.Cost;
            _costStep = settings.CostStep;
            _openSeconds = settings.OpenSeconds;
            Seed = seed;
        }

        public GateSite Site { get; }
        public EssenceColor Color { get; }
        /// <summary>The price of the current (or next) opening: rises each time the gate closes.</summary>
        public int Cost => _baseCost + _costStep * TimesClosed;
        /// <summary>How much the price rises each time the gate shuts.</summary>
        public int CostStep => _costStep;
        /// <summary>How many openings have ended.</summary>
        public int TimesClosed { get; private set; }
        /// <summary>True from a payment until the opening ends.</summary>
        public bool IsOpen => _closesAt.HasValue;

        /// <summary>Seconds until the gate shuts (0 when closed).</summary>
        public double OpenLeft(double now) => _closesAt.HasValue ? Math.Max(0.0, _closesAt.Value - now) : 0.0;

        /// <summary>
        /// Shuts the gate once its opening has run out: access ends for everyone and the price goes up.
        /// Returns true on the step it shuts.
        /// </summary>
        public bool Step(double now)
        {
            if (!_closesAt.HasValue || now < _closesAt.Value) return false;
            _closesAt = null;
            _accessSeed.Clear();
            TimesClosed++;
            return true;
        }
        /// <summary>The world seed currently live. Access bought on any other seed doesn't count.</summary>
        public uint Seed { get; private set; }

        public bool HasAccess(int playerId) => _accessSeed.TryGetValue(playerId, out uint s) && s == Seed;

        /// <summary>The seed a player bought access on, if they ever did.</summary>
        public bool TryGetAccessSeed(int playerId, out uint seed) => _accessSeed.TryGetValue(playerId, out seed);

        /// <summary>How much more banked essence the player needs (0 if they can pay or already have access).</summary>
        public int Shortfall(EssenceWallet wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (HasAccess(wallet.PlayerId)) return 0;
            return Math.Max(0, Cost - wallet.Banked(Color));
        }

        /// <summary>
        /// Pays the cost from the wallet's banked essence and grants its owner access. Opens the gate
        /// until <paramref name="now"/> plus the open time, if it wasn't open already.
        /// </summary>
        public GateUnlockOutcome Unlock(EssenceWallet wallet, double now = 0.0)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (HasAccess(wallet.PlayerId)) return GateUnlockOutcome.AlreadyOpen;
            if (!wallet.TrySpend(Color, Cost)) return GateUnlockOutcome.NotEnough;
            _accessSeed[wallet.PlayerId] = Seed;
            if (!_closesAt.HasValue) _closesAt = now + _openSeconds;
            return GateUnlockOutcome.Unlocked;
        }

        /// <summary>Gives a player access without paying (a party member already paid with the shared bank).</summary>
        public void GrantAccess(int playerId) => _accessSeed[playerId] = Seed;

        /// <summary>True when every member has access. An empty group can't pass.</summary>
        public bool CanGroupPass(IEnumerable<int> members)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            bool any = false;
            foreach (int id in members)
            {
                any = true;
                if (!HasAccess(id)) return false;
            }
            return any;
        }

        /// <summary>Members still without access, in the order given.</summary>
        public IReadOnlyList<int> MissingAccess(IEnumerable<int> members)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var missing = new List<int>();
            foreach (int id in members)
            {
                if (!HasAccess(id)) missing.Add(id);
            }
            return missing;
        }

        /// <summary>Going back up a layer is always free, with or without access.</summary>
        public bool CanGoUp(int playerId) => true;

        /// <summary>The world moved to a new seed: everyone's access from the old one stops counting.</summary>
        public void Reseed(uint newSeed)
        {
            Seed = newSeed;
        }
    }
}
