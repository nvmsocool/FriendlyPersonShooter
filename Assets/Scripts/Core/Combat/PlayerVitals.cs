using System;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// A player's health, shield and buff slots. Bullets never damage players, so the only way to
    /// lose health is <see cref="TakeEnemyDamage"/>. Shield (from Vigor overflow) absorbs damage first.
    /// </summary>
    public sealed class PlayerVitals
    {
        public PlayerVitals(int id, CombatSettings settings, int partyId = 0)
        {
            Id = id;
            PartyId = partyId;
            MaxHealth = settings.PlayerMaxHealth;
            Health = MaxHealth;
            Buffs = new EffectSet(settings.MaxBuffSlots, settings.ExtraSourceShare, settings.StackCapMultiplier);
        }

        /// <summary>Unique player id; also used as the effect source id.</summary>
        public int Id { get; }
        /// <summary>Party id, or 0 when solo. Two players with the same non-zero id are in one party.</summary>
        public int PartyId { get; set; }
        public BuffAcceptance Acceptance { get; set; } = BuffAcceptance.Everyone;

        public float Health { get; private set; }
        public float MaxHealth { get; }
        public EffectSet Buffs { get; }

        public float Shield => Buffs.StrengthOf(EffectKind.Shield);
        /// <summary>Fraction added to this player's shot damage by Empower (Might).</summary>
        public float DamageBonus => Buffs.StrengthOf(EffectKind.Empower);
        public bool IsDown => Health <= 0f;

        public bool InPartyWith(PlayerVitals other)
        {
            return PartyId != 0 && other != null && other.PartyId == PartyId;
        }

        /// <summary>Raises health up to max; returns the part of <paramref name="amount"/> that didn't fit.</summary>
        public float Heal(float amount, out float healed)
        {
            healed = Math.Min(Math.Max(amount, 0f), MaxHealth - Health);
            Health += healed;
            return amount - healed;
        }

        /// <summary>Damage from an enemy attack. Shield absorbs first. Returns the health lost.</summary>
        public float TakeEnemyDamage(float amount)
        {
            if (amount <= 0f || IsDown) return 0f;
            float rest = amount - Buffs.DrainShield(amount);
            float lost = Math.Min(rest, Health);
            Health -= lost;
            return lost;
        }

        /// <summary>Sets health directly (spawns, revives, tests).</summary>
        public void SetHealth(float health)
        {
            Health = Math.Max(0f, Math.Min(health, MaxHealth));
        }

        /// <summary>Counts down buffs.</summary>
        public void Tick(float dt) => Buffs.Tick(dt);
    }
}
