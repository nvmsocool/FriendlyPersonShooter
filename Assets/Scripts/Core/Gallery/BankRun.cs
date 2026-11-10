using System.Collections.Generic;
using FriendlyPersonShooter.Core.Expedition;

namespace FriendlyPersonShooter.Core.Gallery
{
    /// <summary>
    /// Banking one color as a shooting gallery. What the player carries of that color when it opens is dealt out in
    /// targets of up to <see cref="GalleryDifficulty.BankChunk"/>, one at a time. Hit a target and its essence is
    /// banked; let it run out and its essence is lost. Essence picked up meanwhile isn't part of it.
    /// </summary>
    public sealed class BankRun
    {
        private readonly HashSet<int> _done = new HashSet<int>();

        public EssenceColor Color { get; }
        /// <summary>Carried of <see cref="Color"/> when it opened.</summary>
        public int Amount { get; }
        public int Banked { get; private set; }
        public int Lost { get; private set; }

        public BankRun(EssenceColor color, int amount)
        {
            Color = color;
            Amount = amount < 0 ? 0 : amount;
        }

        /// <summary>How many targets (the gallery's target count).</summary>
        public int Targets => GalleryDifficulty.BankTargets(Amount);

        /// <summary>Essence target <paramref name="i"/> holds.</summary>
        public int Chunk(int i) => GalleryDifficulty.BankChunkAmount(Amount, i);

        /// <summary>Target <paramref name="i"/> was hit: banks its essence (no more than is still carried). Returns how much.</summary>
        public int Hit(int i, EssenceWallet wallet)
        {
            if (!_done.Add(i)) return 0;
            int n = wallet.BankSome(Color, Chunk(i));
            Banked += n;
            return n;
        }

        /// <summary>Target <paramref name="i"/> ran out: its essence is lost. Returns how much.</summary>
        public int Miss(int i, EssenceWallet wallet)
        {
            if (!_done.Add(i)) return 0;
            int n = wallet.LoseCarried(Color, Chunk(i));
            Lost += n;
            return n;
        }
    }
}
