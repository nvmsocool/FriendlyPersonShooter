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
        /// <summary>Blue per Sentinel: it takes patience to catch its eye open.</summary>
        public float SentinelDrop { get; set; } = 2f;
        /// <summary>Purple per roaming Bulwark: a long fight, so a big drop.</summary>
        public float BulwarkDrop { get; set; } = 4f;
        /// <summary>Orange per Diver.</summary>
        public float DiverDrop { get; set; } = 2f;
        /// <summary>
        /// Chance that a Glimmer kill drops one essence of a random color (among the colors enemies drop
        /// today): a small bonus for always having something to shoot, never a reliable source of any color.
        /// </summary>
        public float GlimmerChance { get; set; } = 0.25f;
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
                // Glimmers roll a chance at a random color instead (see GlimmerChance).
                case EnemyFamily.Glimmer: return 0f;
                // Roaming Bulwarks only: raid bosses pay through the raid's clear reward (raids never pay essence).
                case EnemyFamily.Bulwark: return BulwarkDrop;
                case EnemyFamily.Sentinel: return SentinelDrop;
                case EnemyFamily.Diver: return DiverDrop;
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

        private CombatRandom _rng;

        /// <param name="seed">Seeds the Glimmer drop rolls.</param>
        public DropLedger(EssenceDropSettings settings = null, uint seed = 1)
        {
            _settings = settings ?? new EssenceDropSettings();
            _rng = new CombatRandom(seed == 0 ? 1u : seed);
        }

        /// <summary>The colors a Glimmer can drop: any of the six.</summary>
        private static readonly EssenceColor[] GlimmerColors =
        {
            EssenceColor.Yellow, EssenceColor.Red, EssenceColor.Green, EssenceColor.Blue, EssenceColor.Purple, EssenceColor.Orange,
        };

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
        public IReadOnlyList<EssenceAward> Killed(int enemyId, EnemyFamily family) => Killed(enemyId, family, EliteKind.None);

        /// <summary>
        /// As above for an elite: <see cref="Elites.DropMultiplier"/> times the main color, plus
        /// <see cref="Elites.SecondDrop"/> of the second family's color.
        /// </summary>
        public IReadOnlyList<EssenceAward> Killed(int enemyId, EnemyFamily family, EliteKind elite)
        {
            var awards = new List<EssenceAward>();
            if (!_hitters.TryGetValue(enemyId, out var list)) return awards;
            _hitters.Remove(enemyId);

            if (family == EnemyFamily.Glimmer)
            {
                // Each hitter rolls on their own: a chance at one essence of a random color.
                foreach (int player in list)
                {
                    if (!_rng.Chance(_settings.GlimmerChance)) continue;
                    awards.Add(new EssenceAward(player, GlimmerColors[_rng.NextInt(GlimmerColors.Length)], 1));
                }
                return awards;
            }

            var color = EssenceColors.Of(family);
            float drop = _settings.DropFor(family) * (elite != EliteKind.None ? Elites.DropMultiplier : 1f);
            foreach (int player in list)
            {
                if (elite != EliteKind.None) awards.Add(new EssenceAward(player, EssenceColors.Of(Elites.Second(elite)), Elites.SecondDrop));
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
