using System;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>Which of the giver's pools a gift comes out of.</summary>
    public enum GiftSource : byte
    {
        /// <summary>Carried essence first, then banked to make up the rest.</summary>
        CarriedFirst = 0,
        CarriedOnly = 1,
        BankedOnly = 2,
    }

    /// <summary>
    /// One player's essence, split into <em>carried</em> (picked up, at risk until banked) and
    /// <em>banked</em> (safe; stored at the raid door or a gate, see <see cref="Banking"/>).
    ///
    /// Rules where the design doc was silent:
    /// <list type="bullet">
    /// <item>Spending (gates, later raid keys) uses banked essence only. Banking is the step that
    /// commits essence, so a run has a clear "bank it" beat; the gate banks too, so paying needs no detour.</item>
    /// <item>Gifts move pool to pool: carried essence arrives carried, banked essence arrives banked.
    /// So handing a friend banked essence at the gate lets them pay right away, while carried
    /// essence stays at risk until they bank it. By default carried goes first.</item>
    /// <item>A bleed-out cache drops the share of each carried color rounded down, so a single
    /// carried essence is never lost. Banked essence never drops.</item>
    /// </list>
    /// </summary>
    public sealed class EssenceWallet
    {
        private readonly EssenceAmounts _carried = new EssenceAmounts();
        private readonly EssenceAmounts _banked;

        public EssenceWallet(int playerId)
        {
            PlayerId = playerId;
            _banked = new EssenceAmounts();
        }

        /// <summary>
        /// A wallet whose banked essence is the party's shared pool: banking adds to it, spending (the gate) takes
        /// from it, and every wallet built on the same pool sees the same banked amounts. Carried essence stays
        /// each player's own.
        /// </summary>
        public EssenceWallet(int playerId, EssenceAmounts sharedBank)
        {
            PlayerId = playerId;
            _banked = sharedBank ?? throw new ArgumentNullException(nameof(sharedBank));
        }

        public int PlayerId { get; }

        public int Carried(EssenceColor color) => _carried[color];
        public int Banked(EssenceColor color) => _banked[color];
        /// <summary>Carried plus banked.</summary>
        public int Total(EssenceColor color) => _carried[color] + _banked[color];

        /// <summary>A copy of everything carried.</summary>
        public EssenceAmounts CarriedAll() => _carried.Clone();
        /// <summary>A copy of everything banked.</summary>
        public EssenceAmounts BankedAll() => _banked.Clone();

        /// <summary>Adds picked-up essence to carried.</summary>
        public void PickUp(EssenceColor color, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            _carried.Add(color, amount);
        }

        /// <summary>Adds essence straight to carried or banked (gifts, cache returns).</summary>
        public void Receive(EssenceAmounts amounts, bool banked)
        {
            if (amounts == null) throw new ArgumentNullException(nameof(amounts));
            (banked ? _banked : _carried).Add(amounts);
        }

        /// <summary>Moves all carried essence to banked (standing at the raid door or a gate). Returns what moved.</summary>
        public EssenceAmounts BankAll()
        {
            var moved = _carried.Clone();
            _banked.Add(moved);
            _carried.Clear();
            return moved;
        }

        /// <summary>True when banked essence covers the amount. Carried essence can't be spent.</summary>
        public bool CanSpend(EssenceColor color, int amount) => amount >= 0 && _banked[color] >= amount;

        /// <summary>Spends banked essence. Does nothing and returns false if there isn't enough.</summary>
        public bool TrySpend(EssenceColor color, int amount)
        {
            if (!CanSpend(color, amount)) return false;
            _banked[color] -= amount;
            return true;
        }

        /// <summary>How much a gift from <paramref name="source"/> could give right now.</summary>
        public int Giftable(EssenceColor color, GiftSource source)
        {
            switch (source)
            {
                case GiftSource.CarriedOnly: return _carried[color];
                case GiftSource.BankedOnly: return _banked[color];
                default: return _carried[color] + _banked[color];
            }
        }

        /// <summary>
        /// Gives any amount of one color to another player, for free. All or nothing: returns false
        /// (and moves nothing) if the amount isn't available from <paramref name="source"/>, or the
        /// recipient is this same player.
        /// </summary>
        public bool TryGift(EssenceWallet to, EssenceColor color, int amount, GiftSource source = GiftSource.CarriedFirst)
        {
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (amount <= 0 || to == this || to.PlayerId == PlayerId) return false;
            if (Giftable(color, source) < amount) return false;

            int fromCarried = source == GiftSource.BankedOnly ? 0 : Math.Min(amount, _carried[color]);
            int fromBanked = amount - fromCarried;
            _carried[color] -= fromCarried;
            _banked[color] -= fromBanked;
            to._carried.Add(color, fromCarried);
            to._banked.Add(color, fromBanked);
            return true;
        }

        /// <summary>
        /// Takes up to <paramref name="amount"/> of one color, carried first, then banked (a raid key pledge).
        /// Returns how much was taken.
        /// </summary>
        public int Take(EssenceColor color, int amount)
        {
            if (amount <= 0) return 0;
            int fromCarried = Math.Min(amount, _carried[color]);
            _carried[color] -= fromCarried;
            int fromBanked = Math.Min(amount - fromCarried, _banked[color]);
            _banked[color] -= fromBanked;
            return fromCarried + fromBanked;
        }

        /// <summary>
        /// Removes <paramref name="share"/> of each carried color (rounded down) for a bleed-out
        /// cache and returns it. Use <see cref="Combat.ReviveSettings.EssenceDropShare"/> for the share.
        /// </summary>
        public EssenceAmounts DropCache(float share)
        {
            if (share < 0f || share > 1f) throw new ArgumentOutOfRangeException(nameof(share));
            var dropped = new EssenceAmounts();
            foreach (var color in EssenceColors.All)
            {
                int amount = (int)Math.Floor(_carried[color] * (double)share + 1e-9);
                if (amount <= 0) continue;
                _carried[color] -= amount;
                dropped[color] = amount;
            }
            return dropped;
        }
    }
}
