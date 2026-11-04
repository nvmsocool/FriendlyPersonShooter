using System;
using System.Collections.Generic;

namespace FriendlyPersonShooter.Core.Combat
{
    /// <summary>What a hit did to chains.</summary>
    public enum ChainEvent : byte
    {
        None = 0,
        /// <summary>A new 1-link chain started and the target now holds it.</summary>
        Started,
        /// <summary>The shooter held a chain and passed it on to someone new: one more link.</summary>
        Grew,
        /// <summary>The target was already in the shooter's chain (A → B → A): that chain didn't change.</summary>
        Looped,
        /// <summary>The shooter's enemy hit cashed the chain they held.</summary>
        Cashed,
    }

    /// <summary>A chain as the UI sees it.</summary>
    public struct ChainInfo
    {
        public int ChainId;
        /// <summary>The player who started it.</summary>
        public int StarterId;
        /// <summary>The player holding it: their next enemy hit cashes it.</summary>
        public int HolderId;
        /// <summary>Links, capped (see <see cref="ChainTracker"/> for how they're counted).</summary>
        public int Links;
        /// <summary>Distinct players in the chain, holder included.</summary>
        public int MemberCount;
        /// <summary>Seconds left to pass it on or cash it.</summary>
        public float TimeLeft;
        /// <summary>True when a Relay-carried shot has already added its link.</summary>
        public bool RelayLink;
        /// <summary>Bonus a body hit would cash right now (a weak-point hit gets more).</summary>
        public float Bonus;
    }

    /// <summary>What an ally hit did to chains.</summary>
    public struct ChainBuff
    {
        public ChainEvent Event;
        public int ChainId;
        /// <summary>Links in the chain the target now holds (0 if the target holds none).</summary>
        public int Links;
    }

    /// <summary>What an enemy hit cashed.</summary>
    public struct ChainCash
    {
        /// <summary>True when the shooter held a live chain and this hit used it up.</summary>
        public bool Cashed;
        public int ChainId;
        public int StarterId;
        /// <summary>Links counted for the bonus, including a Relay-carried shot's link.</summary>
        public int Links;
        /// <summary>Damage bonus fraction for this hit, weak-point factor included (0.15 = +15%).</summary>
        public float Bonus;
        public bool WeakPoint;
    }

    /// <summary>
    /// Tracks buff chains for one lobby (or arena). Feed it every accepted ally hit
    /// (<see cref="OnBuff"/>) and every enemy hit (<see cref="OnEnemyHit"/>), with the server time.
    ///
    /// Counting rule. A chain is an ordered list of distinct players; the last one holds it.
    /// - Links = (players in the chain - 1) + 1 if a Relay-carried shot took part, capped at 4.
    ///   So A buffs B = 1 link; A → B → C = 2 links (the doc's example, +15%).
    /// - Any accepted buff S → T where S doesn't hold a live chain starts a new chain [S, T].
    /// - If S holds a live chain (S was given it at most 5 s ago) and T isn't in it, T joins: the
    ///   chain grows by one link, T holds it, and T gets a fresh 5 s. S no longer holds it.
    /// - If T is already in S's chain (A → B → A) that chain doesn't change: no link, no extra time.
    ///   The buff still starts a separate 1-link chain [S, T] (see the next rule).
    /// - A player holds at most one chain. When a buff would hand T a second one, T keeps the one
    ///   with more links (the newer one on a tie) and the other is dropped.
    /// - The holder's next enemy hit within 5 s cashes the chain, whatever its length (a 1-link
    ///   chain cashes for nothing). Bonus: 2 links +15%, 3 links +35%, 4 links +60%; a weak-point
    ///   cashing hit gets 1.5x that.
    /// - Relay: a shot fired while the shooter is Relayed counts as one extra link, once per chain,
    ///   whether it passes the chain on (ally hit) or cashes it (enemy hit). A Relayed shooter with
    ///   no chain who hits an enemy cashes 1 link, which is worth nothing.
    /// </summary>
    public sealed class ChainTracker
    {
        private sealed class Chain
        {
            public int Id;
            public readonly List<int> Members = new List<int>(5);
            public double ExpiresAt;
            public bool RelayLink;
            /// <summary>Extra links from the starter's Conductor mod.</summary>
            public int ExtraLinks;
            public int Holder => Members[Members.Count - 1];
        }

        private readonly CombatSettings _settings;
        private readonly List<Chain> _chains = new List<Chain>();
        private int _nextId = 1;

        public ChainTracker(CombatSettings settings)
        {
            _settings = settings;
        }

        /// <summary>An accepted buff (any payload) from <paramref name="shooterId"/> on <paramref name="targetId"/>.</summary>
        /// <param name="relayed">True when the shooter was Relayed when they fired (read before the hit).</param>
        /// <param name="conductor">True when the shooter's gun has Conductor: a chain this buff starts gets one extra link.</param>
        public ChainBuff OnBuff(int shooterId, int targetId, double time, bool relayed = false, bool conductor = false)
        {
            Prune(time);
            if (shooterId == targetId) return default;

            var held = FindHeld(shooterId);
            if (held != null && !held.Members.Contains(targetId))
            {
                held.Members.Add(targetId);
                held.ExpiresAt = time + _settings.ChainWindow;
                if (relayed) held.RelayLink = true;
                var keptOld = Settle(targetId, held);
                // If the target already held a longer chain, they keep that one and this one ends.
                var ev = keptOld == held ? ChainEvent.Grew : ChainEvent.None;
                return new ChainBuff { Event = ev, ChainId = keptOld.Id, Links = Links(keptOld) };
            }

            var started = new Chain { Id = _nextId++, ExpiresAt = time + _settings.ChainWindow, RelayLink = relayed, ExtraLinks = conductor ? 1 : 0 };
            started.Members.Add(shooterId);
            started.Members.Add(targetId);
            _chains.Add(started);
            var kept = Settle(targetId, started);

            var result = new ChainBuff { Event = ChainEvent.None, ChainId = kept.Id, Links = Links(kept) };
            if (held != null) result.Event = ChainEvent.Looped;
            else if (kept == started) result.Event = ChainEvent.Started;
            return result;
        }

        /// <summary>
        /// An enemy hit by <paramref name="shooterId"/> (call only for live enemies). Cashes the
        /// chain the shooter holds, if any, and returns the bonus to apply to this hit.
        /// </summary>
        /// <param name="relayed">True when the shooter was Relayed when they fired (read before the hit).</param>
        public ChainCash OnEnemyHit(int shooterId, double time, bool weakPoint, bool relayed = false)
        {
            Prune(time);
            var held = FindHeld(shooterId);
            int links = 0;
            var cash = new ChainCash { WeakPoint = weakPoint };
            if (held != null)
            {
                _chains.Remove(held);
                if (relayed) held.RelayLink = true;
                links = Links(held);
                cash.Cashed = true;
                cash.ChainId = held.Id;
                cash.StarterId = held.Members[0];
            }
            else if (relayed)
            {
                links = 1;
            }

            cash.Links = links;
            cash.Bonus = _settings.ChainBonus(links) * (weakPoint ? _settings.ChainWeakPointFactor : 1f);
            return cash;
        }

        /// <summary>True when a buff from <paramref name="shooterId"/> on <paramref name="targetId"/> would grow a chain.</summary>
        public bool WouldGrow(int shooterId, int targetId, double time)
        {
            if (shooterId == targetId) return false;
            var held = FindHeld(shooterId, time);
            return held != null && !held.Members.Contains(targetId);
        }

        /// <summary>The live chain <paramref name="playerId"/> holds, if any.</summary>
        public bool TryGetHeld(int playerId, double time, out ChainInfo info)
        {
            var held = FindHeld(playerId, time);
            info = held == null ? default : Info(held, time);
            return held != null;
        }

        /// <summary>Adds every live chain to <paramref name="results"/>; returns how many were added.</summary>
        public int GetChains(double time, List<ChainInfo> results)
        {
            int added = 0;
            for (int i = 0; i < _chains.Count; i++)
            {
                if (_chains[i].ExpiresAt < time) continue;
                results.Add(Info(_chains[i], time));
                added++;
            }
            return added;
        }

        /// <summary>Drops the chain a player holds (they went down or left). They stay counted in other chains.</summary>
        public void DropPlayer(int playerId)
        {
            for (int i = _chains.Count - 1; i >= 0; i--)
            {
                if (_chains[i].Holder == playerId) _chains.RemoveAt(i);
            }
        }

        /// <summary>Forgets chains whose time ran out. Called by the other methods; call it yourself to free memory early.</summary>
        public void Prune(double time)
        {
            for (int i = _chains.Count - 1; i >= 0; i--)
            {
                if (_chains[i].ExpiresAt < time) _chains.RemoveAt(i);
            }
        }

        private int Links(Chain c)
        {
            int raw = c.Members.Count - 1 + (c.RelayLink ? 1 : 0) + c.ExtraLinks;
            return Math.Min(raw, _settings.ChainMaxLinks);
        }

        private ChainInfo Info(Chain c, double time)
        {
            int links = Links(c);
            return new ChainInfo
            {
                ChainId = c.Id,
                StarterId = c.Members[0],
                HolderId = c.Holder,
                Links = links,
                MemberCount = c.Members.Count,
                TimeLeft = (float)Math.Max(0.0, c.ExpiresAt - time),
                RelayLink = c.RelayLink,
                Bonus = _settings.ChainBonus(links),
            };
        }

        private Chain FindHeld(int playerId)
        {
            for (int i = 0; i < _chains.Count; i++)
            {
                if (_chains[i].Holder == playerId) return _chains[i];
            }
            return null;
        }

        private Chain FindHeld(int playerId, double time)
        {
            var c = FindHeld(playerId);
            return c != null && c.ExpiresAt >= time ? c : null;
        }

        /// <summary>
        /// Makes sure <paramref name="holderId"/> holds only one chain: keeps the one with more links
        /// (<paramref name="newest"/> on a tie) and drops the others. Returns the one kept.
        /// </summary>
        private Chain Settle(int holderId, Chain newest)
        {
            Chain best = newest;
            for (int i = 0; i < _chains.Count; i++)
            {
                var c = _chains[i];
                if (c == newest || c.Holder != holderId) continue;
                if (Links(c) > Links(best)) best = c;
            }
            for (int i = _chains.Count - 1; i >= 0; i--)
            {
                var c = _chains[i];
                if (c != best && c.Holder == holderId) _chains.RemoveAt(i);
            }
            return best;
        }
    }
}
