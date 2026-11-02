using System;
using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>A read-only view of one effect in a set of slots.</summary>
    public readonly struct ActiveEffect
    {
        public readonly EffectKind Kind;
        /// <summary>Current strength (meaning depends on the kind; see <see cref="EffectKind"/>).</summary>
        public readonly float Strength;
        /// <summary>Seconds left before it ends.</summary>
        public readonly float Remaining;
        /// <summary>How many different sources have added to it.</summary>
        public readonly int SourceCount;

        public ActiveEffect(EffectKind kind, float strength, float remaining, int sourceCount)
        {
            Kind = kind;
            Strength = strength;
            Remaining = remaining;
            SourceCount = sourceCount;
        }
    }

    /// <summary>
    /// A small set of timed effect slots (a player's buffs or an enemy's debuffs) with the design's
    /// stacking rules:
    /// - At most <see cref="MaxSlots"/> effects. A new kind when full pushes out the oldest one,
    ///   where oldest means the one applied or refreshed longest ago.
    /// - The same kind from the same source only resets the duration.
    /// - The same kind from a new source resets the duration and adds a share (50%) of the new
    ///   source's strength, capped at a multiple (2x) of the strongest single source.
    /// Shield is the exception: its strength is points that Vigor overflow fills up and damage drains.
    /// </summary>
    public sealed class EffectSet
    {
        private sealed class Slot
        {
            public EffectKind Kind;
            public float Strength;
            public float SingleStrength;
            public float Remaining;
            public long Order;
            public readonly List<int> Sources = new List<int>(4);
        }

        private readonly List<Slot> _slots;
        private readonly float _extraSourceShare;
        private readonly float _stackCap;
        private long _nextOrder;

        public EffectSet(int maxSlots, float extraSourceShare = 0.5f, float stackCapMultiplier = 2f)
        {
            if (maxSlots < 1) throw new ArgumentOutOfRangeException(nameof(maxSlots));
            MaxSlots = maxSlots;
            _slots = new List<Slot>(maxSlots);
            _extraSourceShare = extraSourceShare;
            _stackCap = stackCapMultiplier;
        }

        public int MaxSlots { get; }
        public int Count => _slots.Count;

        /// <summary>The effect in slot <paramref name="index"/> (0 to Count-1), in no set order.</summary>
        public ActiveEffect this[int index]
        {
            get
            {
                var s = _slots[index];
                return new ActiveEffect(s.Kind, s.Strength, s.Remaining, s.Sources.Count);
            }
        }

        public bool Has(EffectKind kind) => Find(kind) != null;

        /// <summary>Strength of the effect, or 0 if it isn't present.</summary>
        public float StrengthOf(EffectKind kind)
        {
            var s = Find(kind);
            return s == null ? 0f : s.Strength;
        }

        /// <summary>Seconds left on the effect, or 0 if it isn't present.</summary>
        public float RemainingOf(EffectKind kind)
        {
            var s = Find(kind);
            return s == null ? 0f : s.Remaining;
        }

        /// <summary>Applies an effect with the stacking rules above.</summary>
        public EffectOutcome Apply(EffectKind kind, int sourceId, float strength, float duration)
        {
            var slot = Find(kind);
            if (slot != null)
            {
                slot.Remaining = Math.Max(slot.Remaining, duration);
                slot.Order = _nextOrder++;
                if (slot.Sources.Contains(sourceId)) return EffectOutcome.Refreshed;

                slot.Sources.Add(sourceId);
                slot.SingleStrength = Math.Max(slot.SingleStrength, strength);
                float raised = Math.Max(slot.Strength + _extraSourceShare * strength, strength);
                slot.Strength = Math.Min(raised, _stackCap * slot.SingleStrength);
                return EffectOutcome.Stacked;
            }

            var outcome = MakeRoom();
            NewSlot(kind, sourceId, strength, duration);
            return outcome;
        }

        /// <summary>
        /// Adds shield points (up to <paramref name="cap"/>) and resets the shield's duration.
        /// Returns the points actually added and what happened to the slot.
        /// </summary>
        public float AddShield(int sourceId, float amount, float cap, float duration, out EffectOutcome outcome)
        {
            var slot = Find(EffectKind.Shield);
            if (slot == null)
            {
                outcome = MakeRoom();
                float added = Math.Min(amount, cap);
                NewSlot(EffectKind.Shield, sourceId, added, duration);
                return added;
            }

            float before = slot.Strength;
            slot.Strength = Math.Min(before + amount, cap);
            slot.SingleStrength = cap;
            slot.Remaining = Math.Max(slot.Remaining, duration);
            slot.Order = _nextOrder++;
            if (slot.Sources.Contains(sourceId))
            {
                outcome = EffectOutcome.Refreshed;
            }
            else
            {
                slot.Sources.Add(sourceId);
                outcome = EffectOutcome.Stacked;
            }
            return slot.Strength - before;
        }

        /// <summary>Takes up to <paramref name="amount"/> points out of the shield; returns what it absorbed.</summary>
        public float DrainShield(float amount)
        {
            var slot = Find(EffectKind.Shield);
            if (slot == null || amount <= 0) return 0f;
            float absorbed = Math.Min(amount, slot.Strength);
            slot.Strength -= absorbed;
            if (slot.Strength <= 0f) _slots.Remove(slot);
            return absorbed;
        }

        public bool Remove(EffectKind kind)
        {
            var s = Find(kind);
            return s != null && _slots.Remove(s);
        }

        public void Clear() => _slots.Clear();

        /// <summary>Counts down every effect and removes the ones that ran out.</summary>
        public void Tick(float dt)
        {
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                _slots[i].Remaining -= dt;
                if (_slots[i].Remaining <= 0f) _slots.RemoveAt(i);
            }
        }

        private Slot Find(EffectKind kind)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Kind == kind) return _slots[i];
            }
            return null;
        }

        private EffectOutcome MakeRoom()
        {
            if (_slots.Count < MaxSlots) return EffectOutcome.Added;
            int oldest = 0;
            for (int i = 1; i < _slots.Count; i++)
            {
                if (_slots[i].Order < _slots[oldest].Order) oldest = i;
            }
            _slots.RemoveAt(oldest);
            return EffectOutcome.ReplacedOldest;
        }

        private Slot NewSlot(EffectKind kind, int sourceId, float strength, float duration)
        {
            var slot = new Slot
            {
                Kind = kind,
                Strength = strength,
                SingleStrength = strength,
                Remaining = duration,
                Order = _nextOrder++,
            };
            slot.Sources.Add(sourceId);
            _slots.Add(slot);
            return slot;
        }
    }
}
