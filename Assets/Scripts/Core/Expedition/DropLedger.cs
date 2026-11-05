using System;
using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Expedition
{
    /// <summary>
    /// How much essence a normal enemy of each family drops, per kill, to every player who hit it.
    /// Fractions build up per player and pay out as whole essence once they reach 1, so tiny Mites
    /// (8–20 per swarm, one hit each) can be worth a quarter each.
    /// </summary>
    public sealed class EssenceDropSettings
    {
        /// <summary>
        /// Yellow per Wisp. 1, so a 40-Yellow gate is about 40 kills: roughly 13 minutes of hunting
        /// for a solo player landing 10–15% of shots (see <see cref="ExpeditionSettings"/>).
        /// </summary>
        public float WispDrop { get; set; } = 1f;
        /// <summary>Red per Skitter (twice a Wisp's health, so a bit more).</summary>
        public float SkitterDrop { get; set; } = 2f;
        /// <summary>Green per single Mite; a 12-Mite swarm is worth 3.</summary>
        public float MiteDrop { get; set; } = 0.25f;
        /// <summary>Yellow per Glimmer: a little, for always having something to shoot.</summary>
        public float GlimmerDrop { get; set; } = 1f;
        /// <summary>
        /// Seconds after a payload buff during which the buffer shares in whatever the buffed player
        /// hits, so supports earn essence and kill XP too.
        /// </summary>
        public float AssistWindow { get; set; } = 8f;

        public float DropFor(EnemyFamily family)
        {
            switch (family)
            {
                case EnemyFamily.Wisp: return WispDrop;
                case EnemyFamily.Skitter: return SkitterDrop;
                case EnemyFamily.Glimmer: return GlimmerDrop;
                // The raid boss pays out through the raid's clear reward instead.
                case EnemyFamily.Bulwark: return 0f;
                default: return MiteDrop;
            }
        }
    }

    /// <summary>Essence one player earns from one kill.</summary>
    public readonly struct EssenceAward
    {
        public readonly int PlayerId;
        public readonly EssenceColor Color;
        public readonly int Amount;

        public EssenceAward(int playerId, EssenceColor color, int amount)
        {
            PlayerId = playerId;
            Color = color;
            Amount = amount;
        }

        public override string ToString() => $"player {PlayerId}: {Amount} {Color}";
    }

    /// <summary>
    /// Records which players landed a hit on each live enemy, and on its death hands every one of
    /// them the full drop. No kill stealing: the first hitter, the last hitter and everyone between
    /// all get the same amount. Awards come back in first-hit order so every machine agrees.
    /// Buffing counts too: for <see cref="EssenceDropSettings.AssistWindow"/> seconds after a payload
    /// buff, the buffer is credited with every enemy the buffed player hits.
    /// </summary>
    public sealed class DropLedger
    {
        private readonly EssenceDropSettings _settings;
        private readonly Dictionary<int, List<int>> _hitters = new Dictionary<int, List<int>>();
        private readonly Dictionary<(int player, EssenceColor color), float> _fractions =
            new Dictionary<(int, EssenceColor), float>();
        /// <summary>For each buffed player: who buffed them, and until when that counts.</summary>
        private readonly Dictionary<int, Dictionary<int, double>> _backers = new Dictionary<int, Dictionary<int, double>>();

        public DropLedger(EssenceDropSettings settings = null)
        {
            _settings = settings ?? new EssenceDropSettings();
        }

        /// <summary>Notes that a player hit an enemy. Repeat hits by the same player count once.</summary>
        public void RecordHit(int enemyId, int playerId)
        {
            if (!_hitters.TryGetValue(enemyId, out var list))
            {
                list = new List<int>();
                _hitters.Add(enemyId, list);
            }
            if (!list.Contains(playerId)) list.Add(playerId);
        }

        /// <summary>
        /// Notes that a player hit an enemy at <paramref name="time"/>: they count, and so does everyone
        /// who buffed them within the assist window.
        /// </summary>
        public void RecordHit(int enemyId, int playerId, double time)
        {
            RecordHit(enemyId, playerId);
            if (!_backers.TryGetValue(playerId, out var backers)) return;
            foreach (var kv in backers)
            {
                if (time <= kv.Value) RecordHit(enemyId, kv.Key);
            }
        }

        /// <summary>A payload buff landed: <paramref name="helperId"/> shares in <paramref name="targetId"/>'s hits for a while.</summary>
        public void RecordBuff(int helperId, int targetId, double time)
        {
            if (helperId == targetId) return;
            if (!_backers.TryGetValue(targetId, out var backers))
            {
                backers = new Dictionary<int, double>();
                _backers.Add(targetId, backers);
            }
            backers[helperId] = time + _settings.AssistWindow;
        }

        /// <summary>Players who have hit this enemy so far, in first-hit order.</summary>
        public IReadOnlyList<int> HittersOf(int enemyId)
        {
            return _hitters.TryGetValue(enemyId, out var list) ? list : (IReadOnlyList<int>)Array.Empty<int>();
        }

        /// <summary>
        /// The enemy died: returns what each hitter earns and forgets the enemy. A player whose share
        /// is still a fraction below 1 is left out until a later kill tops it up.
        /// </summary>
        public IReadOnlyList<EssenceAward> Killed(int enemyId, EnemyFamily family)
        {
            var awards = new List<EssenceAward>();
            if (!_hitters.TryGetValue(enemyId, out var list)) return awards;
            _hitters.Remove(enemyId);

            var color = EssenceColors.Of(family);
            float drop = _settings.DropFor(family);
            foreach (int player in list)
            {
                var key = (player, color);
                _fractions.TryGetValue(key, out float owed);
                owed += drop;
                // A small tolerance so 0.1 x 10 still pays out 1.
                int whole = (int)Math.Floor(owed + 1e-4f);
                owed = Math.Max(0f, owed - whole);
                if (owed < 1e-4f) _fractions.Remove(key);
                else _fractions[key] = owed;
                if (whole > 0) awards.Add(new EssenceAward(player, color, whole));
            }
            return awards;
        }

        /// <summary>The enemy left without dying (despawned, faded out on a merge): nobody earns anything.</summary>
        public void Forget(int enemyId)
        {
            _hitters.Remove(enemyId);
        }

        /// <summary>A player left the lobby: drop their hits and unpaid fractions.</summary>
        public void PlayerGone(int playerId)
        {
            foreach (var list in _hitters.Values) list.Remove(playerId);
            _backers.Remove(playerId);
            foreach (var backers in _backers.Values) backers.Remove(playerId);
            var stale = new List<(int, EssenceColor)>();
            foreach (var key in _fractions.Keys)
            {
                if (key.player == playerId) stale.Add(key);
            }
            foreach (var key in stale) _fractions.Remove(key);
        }

        /// <summary>Adds each award to its player's wallet as carried essence. Players with no wallet are skipped.</summary>
        public static void Pay(IReadOnlyList<EssenceAward> awards, Func<int, EssenceWallet> walletOf)
        {
            if (walletOf == null) throw new ArgumentNullException(nameof(walletOf));
            foreach (var award in awards)
            {
                walletOf(award.PlayerId)?.PickUp(award.Color, award.Amount);
            }
        }
    }
}
