using System;
using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>
    /// A row of essence slots (the gate's tablet, one color of the raid key): one slot per essence needed, lit as
    /// essence goes in. Shooting an empty slot lights that one; essence arriving another way (a bot banking) lights
    /// the first empty slots, and essence taken out (the gate paid) dims the last lit ones, so the lit count always
    /// matches the amount.
    /// </summary>
    public sealed class SlotBoard
    {
        private readonly List<bool> _lit = new List<bool>();

        public int Count => _lit.Count;
        public int LitCount { get; private set; }

        public bool IsLit(int i) => i >= 0 && i < _lit.Count && _lit[i];

        /// <summary>
        /// Makes it <paramref name="slots"/> slots with <paramref name="amount"/> lit (clamped), keeping the slots
        /// already lit where it can.
        /// </summary>
        public void Sync(int slots, int amount)
        {
            slots = Math.Max(0, slots);
            while (_lit.Count > slots)
            {
                if (_lit[_lit.Count - 1]) LitCount--;
                _lit.RemoveAt(_lit.Count - 1);
            }
            while (_lit.Count < slots) _lit.Add(false);
            int want = Math.Clamp(amount, 0, slots);
            for (int i = 0; i < _lit.Count && LitCount < want; i++)
            {
                if (_lit[i]) continue;
                _lit[i] = true;
                LitCount++;
            }
            for (int i = _lit.Count - 1; i >= 0 && LitCount > want; i--)
            {
                if (!_lit[i]) continue;
                _lit[i] = false;
                LitCount--;
            }
        }

        /// <summary>Lights slot <paramref name="i"/> if it's empty. Returns whether it did.</summary>
        public bool Light(int i)
        {
            if (i < 0 || i >= _lit.Count || _lit[i]) return false;
            _lit[i] = true;
            LitCount++;
            return true;
        }

        /// <summary>Slots <paramref name="from"/> to from + 63 as bits (bit n = slot from + n).</summary>
        public ulong Bits(int from = 0)
        {
            ulong bits = 0;
            for (int n = 0; n < 64 && from + n < _lit.Count; n++)
            {
                if (from + n >= 0 && _lit[from + n]) bits |= 1UL << n;
            }
            return bits;
        }
    }
}
