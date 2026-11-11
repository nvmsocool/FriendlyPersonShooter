using System;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>
    /// A player's health, shield and buff slots. Bullets never damage players, so the only way to
    /// lose health is <see cref="TakeEnemyDamage(float)"/>. Shield (from Vigor overflow) absorbs damage first.
    ///
    /// Downed: when health reaches 0 the player is down, not dead. Their buffs (and any Relay) are
    /// cleared, they refuse new buffs, and the game starts a <see cref="ReviveState"/> for them.
    /// The revive ends with <see cref="Revive"/> (fragments shot in time) or <see cref="Respawn"/>
    /// (bled out); <see cref="ReviveState"/> calls these itself.
    /// </summary>
    public sealed class PlayerVitals
    {
        private readonly CombatSettings _settings;
        private PayloadKind _relayCarry = PayloadKind.Might;

        public PlayerVitals(int id, CombatSettings settings, int partyId = 0)
        {
            _settings = settings;
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
        public float MaxHealth { get; private set; }

        // ---- Skill tree bonuses (0 = none). Set by the game from the player's skill ranks. ----
        /// <summary>Added to shot damage as a fraction (0.1 = +10%), on top of Might.</summary>
        public float SkillDamageBonus { get; set; }
        /// <summary>Added to the move speed multiplier (0.06 = +6%).</summary>
        public float SkillMoveBonus { get; set; }
        /// <summary>Added to the jump multiplier (0.15 = +15%).</summary>
        public float SkillJumpBonus { get; set; }
        /// <summary>Multiplies the heals and buff strengths this player gives (1 = normal).</summary>
        public float GivePotency { get; set; } = 1f;
        /// <summary>Multiplies the durations of the buffs this player gives (1 = normal).</summary>
        public float GiveDuration { get; set; } = 1f;

        /// <summary>Changes max health (skills), keeping the same share of health.</summary>
        public void SetMaxHealth(float max)
        {
            if (max <= 0f || Math.Abs(max - MaxHealth) < 1e-4f) return;
            float share = MaxHealth > 0f ? Health / MaxHealth : 1f;
            MaxHealth = max;
            if (!IsDown) Health = Math.Max(1f, Math.Min(MaxHealth, MaxHealth * share));
        }
        public EffectSet Buffs { get; }

        /// <summary>Vigor's shield (from heal overflow).</summary>
        public float Shield => Buffs.StrengthOf(EffectKind.Shield);

        /// <summary>Ward's shield; absorbs damage after Vigor's.</summary>
        public float Ward => Buffs.StrengthOf(EffectKind.Warded);

        /// <summary>Both shields together.</summary>
        public float TotalShield => Shield + Ward;

        /// <summary>Multiply fire rate by this (Frenzy; 1 when not Frenzied).</summary>
        public float FireRateMultiplier => 1f + Buffs.StrengthOf(EffectKind.Frenzied);

        /// <summary>Divide the capsule's regrowth time by this (Frenzy; 1 when not Frenzied).</summary>
        public float ReloadSpeedMultiplier => 1f + Buffs.StrengthOf(EffectKind.Frenzied);

        /// <summary>How much larger weak points are for this player's hits (Focus; 0 when not Focused).</summary>
        public float WeakPointBonus => Buffs.StrengthOf(EffectKind.Focused);

        /// <summary>Overdrive (Haste + Frenzy): shots don't drain the payload capsule.</summary>
        public bool NoReload => Buffs.Has(EffectKind.Hasted) && Buffs.Has(EffectKind.Frenzied);

        /// <summary>Deadeye (Might + Focus): weak-point hits deal 3x.</summary>
        public bool IsDeadeye => Buffs.Has(EffectKind.Empower) && Buffs.Has(EffectKind.Focused);

        /// <summary>Bastion (Vigor's shield + Ward): the Ward shield regenerates.</summary>
        public bool IsBastion => Buffs.Has(EffectKind.Shield) && Buffs.Has(EffectKind.Warded);

        /// <summary>
        /// Fraction added to this player's shot damage by Empower (Might). Doubled (by default) while
        /// the Blitz combo (Haste + Might) is active.
        /// </summary>
        public float DamageBonus
        {
            get
            {
                float empower = Buffs.StrengthOf(EffectKind.Empower);
                return (IsBlitzed ? empower * _settings.BlitzEmpowerMultiplier : empower) + SkillDamageBonus;
            }
        }

        /// <summary>True while Haste and Might sit on this player together.</summary>
        public bool IsBlitzed => Buffs.Has(EffectKind.Hasted) && Buffs.Has(EffectKind.Empower);

        /// <summary>Multiply the player's move speed by this (1 when not Hasted).</summary>
        public float MoveSpeedMultiplier => 1f + Buffs.StrengthOf(EffectKind.Hasted) + SkillMoveBonus;

        /// <summary>Multiply the player's jump strength by this (1 when not Hasted).</summary>
        public float JumpMultiplier
        {
            get
            {
                float haste = Buffs.StrengthOf(EffectKind.Hasted);
                if (haste <= 0f || _settings.HasteMoveBonus <= 0f) return 1f + SkillJumpBonus;
                return 1f + haste * (_settings.HasteJumpBonus / _settings.HasteMoveBonus) + SkillJumpBonus;
            }
        }

        public bool IsDown => Health <= 0f;

        // Relay.
        /// <summary>
        /// The payload this player hands on when their Relay bullet hits an ally (for example their
        /// sidearm's payload). Can't be Relay itself. Defaults to Might.
        /// </summary>
        public PayloadKind RelayCarry
        {
            get => _relayCarry;
            set
            {
                if (value == PayloadKind.Relay) throw new ArgumentException("Relay can't carry Relay.", nameof(value));
                _relayCarry = value;
            }
        }

        /// <summary>True while this player's hits also carry a relayed payload.</summary>
        public bool HasRelay => RelayHitsLeft > 0 && Buffs.Has(EffectKind.Relayed);
        /// <summary>The payload this player's hits carry while <see cref="HasRelay"/>.</summary>
        public PayloadKind RelayPayload { get; private set; }
        /// <summary>The player who relayed it (the effect's source).</summary>
        public int RelaySourceId { get; private set; }
        /// <summary>Hits left that carry the relayed payload.</summary>
        public int RelayHitsLeft { get; private set; }

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
        public float TakeEnemyDamage(float amount) => TakeEnemyDamage(amount, out _);

        /// <summary>
        /// Damage from an enemy attack. <paramref name="wentDown"/> is true when this hit downed the
        /// player; the game should then start a <see cref="ReviveState"/>.
        /// </summary>
        public float TakeEnemyDamage(float amount, out bool wentDown)
        {
            wentDown = false;
            if (amount <= 0f || IsDown) return 0f;
            float rest = amount - Buffs.DrainShield(amount);
            // An emptied Ward stays (at 0) until it runs out, so Bastion can refill it.
            rest -= Buffs.Drain(EffectKind.Warded, rest, false);
            float lost = Math.Min(rest, Health);
            Health -= lost;
            if (IsDown)
            {
                wentDown = true;
                ClearBuffs();
            }
            return lost;
        }

        /// <summary>Brings a downed player back up with a share of max health (soul fragments were shot in time).</summary>
        public void Revive(float healthFraction)
        {
            ClearBuffs();
            Health = Math.Max(1f, Math.Min(MaxHealth, MaxHealth * healthFraction));
        }

        /// <summary>Full health and no buffs, after bleeding out. The game moves the player to a shrine.</summary>
        public void Respawn()
        {
            ClearBuffs();
            Health = MaxHealth;
        }

        /// <summary>Sets health directly (spawns, revives, tests).</summary>
        public void SetHealth(float health)
        {
            Health = Math.Max(0f, Math.Min(health, MaxHealth));
        }

        /// <summary>Counts down buffs.</summary>
        public void Tick(float dt)
        {
            if (IsBastion) Buffs.Restore(EffectKind.Warded, _settings.BastionRegen * dt, Buffs.SingleStrengthOf(EffectKind.Warded));
            Buffs.Tick(dt);
            if (!Buffs.Has(EffectKind.Relayed)) RelayHitsLeft = 0;
        }

        /// <summary>Starts or restarts a relay: the next hits carry <paramref name="payload"/> from <paramref name="sourceId"/>.</summary>
        internal void SetRelay(int sourceId, PayloadKind payload, int hits)
        {
            RelaySourceId = sourceId;
            RelayPayload = payload;
            RelayHitsLeft = hits;
        }

        /// <summary>Uses up one relayed hit.</summary>
        internal void UseRelayHit()
        {
            if (RelayHitsLeft <= 0) return;
            RelayHitsLeft--;
            if (RelayHitsLeft == 0) Buffs.Remove(EffectKind.Relayed);
        }

        private void ClearBuffs()
        {
            Buffs.Clear();
            RelayHitsLeft = 0;
        }
    }
}
