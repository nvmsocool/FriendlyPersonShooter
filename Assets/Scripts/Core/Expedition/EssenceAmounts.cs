using System;
using System.Text;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>A whole-number amount of each essence color. Never negative.</summary>
    public sealed class EssenceAmounts
    {
        private readonly int[] _amounts = new int[EssenceColors.Count];

        public EssenceAmounts()
        {
        }

        /// <summary>An amount holding just one color.</summary>
        public EssenceAmounts(EssenceColor color, int amount)
        {
            this[color] = amount;
        }

        public int this[EssenceColor color]
        {
            get => _amounts[(int)color];
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Essence can't go below zero.");
                _amounts[(int)color] = value;
            }
        }

        /// <summary>All colors added together.</summary>
        public int Total
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < _amounts.Length; i++) sum += _amounts[i];
                return sum;
            }
        }

        public bool IsEmpty => Total == 0;

        public void Add(EssenceColor color, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            _amounts[(int)color] += amount;
        }

        public void Add(EssenceAmounts other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            for (int i = 0; i < _amounts.Length; i++) _amounts[i] += other._amounts[i];
        }

        public void Clear()
        {
            Array.Clear(_amounts, 0, _amounts.Length);
        }

        public EssenceAmounts Clone()
        {
            var copy = new EssenceAmounts();
            Array.Copy(_amounts, copy._amounts, _amounts.Length);
            return copy;
        }

        /// <summary>For example "12 Yellow, 3 Red", or "nothing".</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _amounts.Length; i++)
            {
                if (_amounts[i] == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(_amounts[i]).Append(' ').Append((EssenceColor)i);
            }
            return sb.Length == 0 ? "nothing" : sb.ToString();
        }
    }
}
