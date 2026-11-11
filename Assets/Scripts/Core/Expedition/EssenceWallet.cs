using System;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>
    /// One player's essence, split into <em>carried</em> (picked up, at risk until banked) and
    /// <em>banked</em> (safe, in the party's shared bank: shooting the gate's empty slots banks it).
    ///
    /// Rules where the design doc was silent:
    /// <list type="bullet">
    /// <item>The gate is paid from banked essence only. Banking is the step that commits essence, so a run
    /// has a clear "bank it" beat. A raid key takes carried essence first, then banked.</item>
    /// <item>A player carries at most <see cref="CarryCap"/> of each color; anything over it is lost. Banked
    /// essence (the party's pool) has no cap. The game's wallets pass the cap in; a wallet built without one
    /// (tests of other rules) carries any amount.</item>
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
        public EssenceWallet(int playerId, EssenceAmounts sharedBank, int carryCap = int.MaxValue)
        {
            PlayerId = playerId;
            _banked = sharedBank ?? throw new ArgumentNullException(nameof(sharedBank));
            Cap = carryCap;
        }

        /// <summary>The most of one color a player can carry.</summary>
        public const int CarryCap = 7;

        public int PlayerId { get; }

        /// <summary>This wallet's carry cap per color (<see cref="CarryCap"/> in the game).</summary>
        public int Cap { get; } = int.MaxValue;

        /// <summary>How much more of <paramref name="color"/> this player can carry.</summary>
        public int Room(EssenceColor color) => Math.Max(0, Cap - _carried[color]);

        public int Carried(EssenceColor color) => _carried[color];
        public int Banked(EssenceColor color) => _banked[color];
        /// <summary>Carried plus banked.</summary>
        public int Total(EssenceColor color) => _carried[color] + _banked[color];

        /// <summary>A copy of everything carried.</summary>
        public EssenceAmounts CarriedAll() => _carried.Clone();
        /// <summary>A copy of everything banked.</summary>
        public EssenceAmounts BankedAll() => _banked.Clone();

        /// <summary>Adds picked-up essence to carried, up to the cap. Returns how much fit; the rest is lost.</summary>
        public int PickUp(EssenceColor color, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int n = Math.Min(amount, Room(color));
            _carried.Add(color, n);
            return n;
        }

        /// <summary>Adds essence straight to carried (up to the cap; the rest is lost) or banked (cache returns).</summary>
        public void Receive(EssenceAmounts amounts, bool banked)
        {
            if (amounts == null) throw new ArgumentNullException(nameof(amounts));
            if (banked)
            {
                _banked.Add(amounts);
                return;
            }
            foreach (var c in EssenceColors.All) PickUp(c, amounts[c]);
        }

        /// <summary>Moves all carried essence to banked (a bot at the raid door or a gate). Returns what moved.</summary>
        public EssenceAmounts BankAll()
        {
            var moved = _carried.Clone();
            _banked.Add(moved);
            _carried.Clear();
            return moved;
        }

        /// <summary>Banks up to <paramref name="amount"/> carried essence of one color. Returns how much moved.</summary>
        public int BankSome(EssenceColor color, int amount)
        {
            int n = Math.Max(0, Math.Min(amount, _carried[color]));
            _carried[color] -= n;
            _banked.Add(color, n);
            return n;
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
