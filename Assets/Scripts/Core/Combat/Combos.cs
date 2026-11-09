using System.Collections.Generic;
using System.Numerics;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>A combo formed on a target.</summary>
    public struct ComboEvent
    {
        public ComboKind Kind;
        public int TargetId;
        public bool TargetIsPlayer;
        /// <summary>True the first time anyone in this lobby set off this combo: show the discovery message.</summary>
        public bool FirstDiscovery;
    }

    /// <summary>
    /// The combos and which effects make them:
    /// - Plague: Wither + Tag on an enemy. Its Wither spreads to nearby enemies (see <see cref="Plague"/>).
    /// - Blitz: Haste + Might on a player. Their Empower bonus counts double (see <see cref="PlayerVitals.DamageBonus"/>).
    /// - Overdrive: Haste + Frenzy on a player. Their shots use no ammo (see <see cref="PlayerVitals.NoReload"/>).
    /// - Deadeye: Might + Focus on a player. Their weak-point hits deal 3x (see <see cref="HitResolver.EnemyDamage(PlayerVitals, EnemyVitals, float, bool)"/>).
    /// - Bastion: Vigor (its shield) + Ward on a player. The Ward shield regenerates (see <see cref="PlayerVitals.Tick"/>).
    /// - Pinned: Slow + Mark on an enemy. It freezes for a moment when the combo forms (see <see cref="EnemyVitals.IsFrozen"/>).
    /// - Shatter: Expose + Brittle on an enemy. Armor stops working (see <see cref="EnemyVitals.Armor"/>).
    /// </summary>
    public static class Combos
    {
        /// <summary>Combos active on a player's buffs.</summary>
        public static ComboKind OnPlayer(EffectSet buffs)
        {
            var c = ComboKind.None;
            if (buffs.Has(EffectKind.Hasted) && buffs.Has(EffectKind.Empower)) c |= ComboKind.Blitz;
            if (buffs.Has(EffectKind.Hasted) && buffs.Has(EffectKind.Frenzied)) c |= ComboKind.Overdrive;
            if (buffs.Has(EffectKind.Empower) && buffs.Has(EffectKind.Focused)) c |= ComboKind.Deadeye;
            if (buffs.Has(EffectKind.Shield) && buffs.Has(EffectKind.Warded)) c |= ComboKind.Bastion;
            return c;
        }

        /// <summary>Combos active on an enemy's debuffs.</summary>
        public static ComboKind OnEnemy(EffectSet debuffs)
        {
            var c = ComboKind.None;
            if (debuffs.Has(EffectKind.Wither) && debuffs.Has(EffectKind.Tag)) c |= ComboKind.Plague;
            if (debuffs.Has(EffectKind.Slow) && debuffs.Has(EffectKind.Mark)) c |= ComboKind.Pinned;
            if (debuffs.Has(EffectKind.Expose) && debuffs.Has(EffectKind.Brittle)) c |= ComboKind.Shatter;
            return c;
        }
    }

    /// <summary>
    /// Notices combos forming, per lobby. The game hides the combo list until someone in the lobby
    /// discovers each combo, so every event says whether it is the lobby's first.
    /// Call <see cref="Check"/> for a target after any hit on it (LobbyCombat does) and, to notice
    /// combos ending, once per tick for targets that have effects; call <see cref="Forget"/> when a
    /// target despawns.
    /// </summary>
    public sealed class ComboTracker
    {
        public static readonly ComboKind[] AllKinds =
        {
            ComboKind.Plague, ComboKind.Blitz, ComboKind.Overdrive, ComboKind.Deadeye, ComboKind.Bastion, ComboKind.Pinned, ComboKind.Shatter,
        };

        private readonly Dictionary<long, ComboKind> _active = new Dictionary<long, ComboKind>();

        /// <summary>Combos someone in this lobby has set off.</summary>
        public ComboKind Discovered { get; private set; }

        public bool IsDiscovered(ComboKind kind) => (Discovered & kind) == kind;

        /// <summary>Marks combos as known, for example when two lobbies merge (pass the other lobby's <see cref="Discovered"/>).</summary>
        public void MarkDiscovered(ComboKind kinds) => Discovered |= kinds;

        /// <summary>Checks a player's buffs. Adds one event per combo that formed since the last check.</summary>
        public int Check(PlayerVitals player, List<ComboEvent> events)
        {
            return Update(player.Id, true, Combos.OnPlayer(player.Buffs), events);
        }

        /// <summary>Checks an enemy's debuffs. Adds one event per combo that formed since the last check.</summary>
        public int Check(EnemyVitals enemy, List<ComboEvent> events)
        {
            return Update(enemy.Id, false, enemy.IsDead ? ComboKind.None : Combos.OnEnemy(enemy.Debuffs), events);
        }

        /// <summary>Combos active on a target as of its last check.</summary>
        public ComboKind ActiveOn(int targetId, bool isPlayer)
        {
            return _active.TryGetValue(Key(targetId, isPlayer), out var c) ? c : ComboKind.None;
        }

        public void Forget(int targetId, bool isPlayer) => _active.Remove(Key(targetId, isPlayer));

        private int Update(int id, bool isPlayer, ComboKind now, List<ComboEvent> events)
        {
            long key = Key(id, isPlayer);
            _active.TryGetValue(key, out var before);
            if (now == ComboKind.None) _active.Remove(key);
            else _active[key] = now;

            var formed = now & ~before;
            if (formed == ComboKind.None) return 0;
            int added = 0;
            foreach (var kind in AllKinds)
            {
                if ((formed & kind) == 0) continue;
                bool first = !IsDiscovered(kind);
                Discovered |= kind;
                events?.Add(new ComboEvent { Kind = kind, TargetId = id, TargetIsPlayer = isPlayer, FirstDiscovery = first });
                added++;
            }
            return added;
        }

        private static long Key(int id, bool isPlayer) => ((long)id << 1) | (isPlayer ? 1L : 0L);
    }

    /// <summary>
    /// The Plague combo (Wither + Tag on one enemy): as soon as it forms, and every
    /// <see cref="CombatSettings.PlagueInterval"/> after, the enemy gives Wither to every other
    /// living enemy within <see cref="CombatSettings.PlagueRadius"/>. The spread Wither deals a
    /// share of the source's Wither damage for a short time. It doesn't carry Tag, so it only
    /// spreads further from enemies that are Tagged too.
    /// </summary>
    public static class Plague
    {
        /// <summary>
        /// The effect source id used for Wither spread by enemy <paramref name="enemyId"/>. Negative,
        /// so it never matches a player id; each plagued enemy counts as its own source for stacking.
        /// </summary>
        public static int SourceId(int enemyId) => ~enemyId;

        /// <summary>
        /// Runs Plague for one tick; call it before ticking the enemies. <paramref name="positions"/>
        /// holds each enemy's body centre, in the same order as <paramref name="enemies"/>. Returns
        /// how many enemies were given Wither this tick.
        /// </summary>
        public static int Step(float dt, IReadOnlyList<EnemyVitals> enemies, IReadOnlyList<Vector3> positions, CombatSettings s)
        {
            int spread = 0;
            float r2 = s.PlagueRadius * s.PlagueRadius;
            for (int i = 0; i < enemies.Count; i++)
            {
                var source = enemies[i];
                if (source.IsDead || !source.IsPlagued)
                {
                    source.PlagueTimer = 0f;
                    continue;
                }

                source.PlagueTimer -= dt;
                if (source.PlagueTimer > 0f) continue;
                source.PlagueTimer += s.PlagueInterval;
                if (source.PlagueTimer < 0f) source.PlagueTimer = s.PlagueInterval;

                float dps = source.Debuffs.StrengthOf(EffectKind.Wither) * s.PlagueShare;
                for (int j = 0; j < enemies.Count; j++)
                {
                    var other = enemies[j];
                    if (j == i || other.IsDead) continue;
                    if (Vector3.DistanceSquared(positions[i], positions[j]) > r2) continue;
                    other.Debuffs.Apply(EffectKind.Wither, SourceId(source.Id), dps, s.PlagueWitherDuration);
                    spread++;
                }
            }
            return spread;
        }
    }
}
