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
        public float MaxHealth { get; }
        public EffectSet Buffs { get; }

        public float Shield => Buffs.StrengthOf(EffectKind.Shield);

        /// <summary>
        /// Fraction added to this player's shot damage by Empower (Might). Doubled (by default) while
        /// the Blitz combo (Haste + Might) is active.
        /// </summary>
        public float DamageBonus
        {
            get
            {
                float empower = Buffs.StrengthOf(EffectKind.Empower);
                return IsBlitzed ? empower * _settings.BlitzEmpowerMultiplier : empower;
            }
        }

        /// <summary>True while Haste and Might sit on this player together.</summary>
        public bool IsBlitzed => Buffs.Has(EffectKind.Hasted) && Buffs.Has(EffectKind.Empower);

        /// <summary>Multiply the player's move speed by this (1 when not Hasted).</summary>
        public float MoveSpeedMultiplier => 1f + Buffs.StrengthOf(EffectKind.Hasted);

        /// <summary>Multiply the player's jump strength by this (1 when not Hasted).</summary>
        public float JumpMultiplier
        {
            get
            {
                float haste = Buffs.StrengthOf(EffectKind.Hasted);
                if (haste <= 0f || _settings.HasteMoveBonus <= 0f) return 1f;
                return 1f + haste * (_settings.HasteJumpBonus / _settings.HasteMoveBonus);
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
