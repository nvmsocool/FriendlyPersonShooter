using System;
using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Tunables for a gate down to the next layer.</summary>
    public sealed class GateSettings
    {
        /// <summary>Gates cost one color in bulk.</summary>
        public EssenceColor Color { get; set; } = EssenceColor.Yellow;
        /// <summary>The doc's example: 40 Yellow.</summary>
        public int Cost { get; set; } = 40;
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
    /// </summary>
    public sealed class Gate
    {
        private readonly Dictionary<int, uint> _accessSeed = new Dictionary<int, uint>();

        public Gate(GateSite site, uint seed, GateSettings settings = null)
        {
            Site = site ?? throw new ArgumentNullException(nameof(site));
            settings = settings ?? new GateSettings();
            if (settings.Cost < 0) throw new ArgumentOutOfRangeException(nameof(settings), "Cost can't be negative.");
            Color = settings.Color;
            Cost = settings.Cost;
            Seed = seed;
        }

        public GateSite Site { get; }
        public EssenceColor Color { get; }
        public int Cost { get; }
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

        /// <summary>Pays the cost from the wallet's banked essence and grants its owner access.</summary>
        public GateUnlockOutcome Unlock(EssenceWallet wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (HasAccess(wallet.PlayerId)) return GateUnlockOutcome.AlreadyOpen;
            if (!wallet.TrySpend(Color, Cost)) return GateUnlockOutcome.NotEnough;
            _accessSeed[wallet.PlayerId] = Seed;
            return GateUnlockOutcome.Unlocked;
        }

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
