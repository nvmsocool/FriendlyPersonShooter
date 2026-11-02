using System;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Core.Bots
{
    /// <summary>
    /// Where a simulated player walks. Solo players and party leaders pick activities (explore, hunt
    /// in a zone, go to a gathering point, stand idle); leaders wait for members who fall behind;
    /// members follow their leader. Between position updates from the server, a bot estimates its
    /// own position from the input it sent.
    /// </summary>
    internal static class BotMind
    {
        private const double ArriveRadius = 2.5;
        private const double HotspotRadius = 8;

        public static (int H, int V) Steer(BotPopulation pop, BotAccount a, double now)
        {
            if (!a.WantsOnline || !a.HasPosition) return (0, 0);

            if (a.LastSteer >= 0)
            {
                double dt = Math.Min(now - a.LastSteer, 0.5);
                if (dt > 0 && (a.MoveH != 0 || a.MoveV != 0))
                {
                    double len = Math.Sqrt(a.MoveH * a.MoveH + a.MoveV * a.MoveV);
                    double step = pop.Settings.PlayerSpeed * dt / len;
                    a.X = Wrap(a.X + a.MoveH * step, pop.WorldWidth);
                    a.Z = Wrap(a.Z + a.MoveV * step, pop.WorldHeight);
                }
            }
            a.LastSteer = now;

            if (now < a.NextDecision) return (a.MoveH, a.MoveV);
            a.NextDecision = now + pop.Settings.DecisionInterval;

            var (h, v) = Decide(pop, a, now);
            a.MoveH = h;
            a.MoveV = v;
            return (h, v);
        }

        private static (int, int) Decide(BotPopulation pop, BotAccount a, double now)
        {
            var s = pop.Settings;
            var leader = a.Party != null ? a.Party.Leader : null;

            if (leader != null && leader != a && leader.WantsOnline && leader.HasPosition)
            {
                a.Activity = BotActivity.Follow;
                // Each member keeps its own spot around the leader, so the party walks as a loose group.
                double angle = a.Seat * 2.399963;
                double tx = leader.X + Math.Cos(angle) * s.FollowDistance;
                double tz = leader.Z + Math.Sin(angle) * s.FollowDistance;
                return Toward(pop, a, tx, tz, 1.5);
            }

            if (a.Activity == BotActivity.Offline || a.Activity == BotActivity.Follow ||
                (a.Activity != BotActivity.Explore && now >= a.ActivityEnd))
            {
                PickActivity(pop, a, now);
            }

            if (a.IsPartyLeader && MemberFarBehind(pop, a)) return (0, 0);

            switch (a.Activity)
            {
                case BotActivity.Explore:
                    if (Arrived(pop, a, a.TargetX, a.TargetZ, ArriveRadius) || now >= a.ActivityEnd)
                    {
                        PickActivity(pop, a, now);
                        if (a.Activity != BotActivity.Explore) return (0, 0);
                    }
                    return Toward(pop, a, a.TargetX, a.TargetZ, ArriveRadius);

                case BotActivity.Hunt:
                    return Mill(pop, a, now, pop.Grid.ZoneSize * 0.4);

                case BotActivity.Hotspot:
                    if (!a.HasTarget && !Arrived(pop, a, a.CenterX, a.CenterZ, HotspotRadius))
                    {
                        // Travel time doesn't count against time spent waiting at the hotspot.
                        a.ActivityEnd = Math.Max(a.ActivityEnd, now + 1);
                        return Toward(pop, a, a.CenterX, a.CenterZ, HotspotRadius);
                    }
                    return Mill(pop, a, now, HotspotRadius);

                default:
                    return (0, 0);
            }
        }

        private static void PickActivity(BotPopulation pop, BotAccount a, double now)
        {
            var s = pop.Settings;
            var r = a.Random;
            a.HasTarget = false;
            a.PauseUntil = 0;
            double total = s.ExploreWeight + s.HuntWeight + s.HotspotWeight + s.AfkWeight;
            double roll = r.NextDouble() * total;

            if ((roll -= s.ExploreWeight) < 0)
            {
                a.Activity = BotActivity.Explore;
                var zone = PickExploreZone(pop, a);
                a.TargetX = (zone.X + 0.2 + 0.6 * r.NextDouble()) * pop.Grid.ZoneSize;
                a.TargetZ = (zone.Y + 0.2 + 0.6 * r.NextDouble()) * pop.Grid.ZoneSize;
                // A safety limit in case the walk is blocked.
                a.ActivityEnd = now + pop.GameMinutes(60);
            }
            else if ((roll -= s.HuntWeight) < 0)
            {
                a.Activity = BotActivity.Hunt;
                var zone = pop.Grid.ZoneAt(a.X, a.Z);
                a.CenterX = (zone.X + 0.5) * pop.Grid.ZoneSize;
                a.CenterZ = (zone.Y + 0.5) * pop.Grid.ZoneSize;
                a.ActivityEnd = now + pop.GameMinutes(Between(r, s.HuntMinMinutes, s.HuntMaxMinutes));
            }
            else if ((roll -= s.HotspotWeight) < 0 && pop.Hotspots.Count > 0)
            {
                a.Activity = BotActivity.Hotspot;
                var (hx, hz) = NearestHotspot(pop, a);
                a.CenterX = hx;
                a.CenterZ = hz;
                a.ActivityEnd = now + pop.GameMinutes(Between(r, s.HotspotMinMinutes, s.HotspotMaxMinutes));
            }
            else
            {
                a.Activity = BotActivity.Afk;
                a.ActivityEnd = now + pop.GameMinutes(Between(r, s.AfkMinMinutes, s.AfkMaxMinutes));
            }
        }

        /// <summary>A zone 1-3 away, preferring ones this player hasn't visited.</summary>
        private static ZoneCoord PickExploreZone(BotPopulation pop, BotAccount a)
        {
            var here = pop.Grid.ZoneAt(a.X, a.Z);
            ZoneCoord fallback = here;
            for (int i = 0; i < 8; i++)
            {
                int range = a.Random.Next(1, 4);
                int dx = a.Random.Next(-range, range + 1);
                int dy = a.Random.Next(-range, range + 1);
                if (dx == 0 && dy == 0) dx = 1;
                var zone = pop.Grid.Wrap(here.X + dx, here.Y + dy);
                if (i == 0) fallback = zone;
                if (!a.Visited.Contains(zone)) return zone;
            }
            return fallback;
        }

        private static (double, double) NearestHotspot(BotPopulation pop, BotAccount a)
        {
            double best = double.MaxValue;
            (double, double) pick = pop.Hotspots[0];
            foreach (var h in pop.Hotspots)
            {
                var (dx, dz) = Delta(pop, a.X, a.Z, h.X, h.Z);
                double d = dx * dx + dz * dz;
                if (d < best)
                {
                    best = d;
                    pick = h;
                }
            }
            return pick;
        }

        /// <summary>Wanders between random points near the centre, pausing at each, as if fighting there.</summary>
        private static (int, int) Mill(BotPopulation pop, BotAccount a, double now, double radius)
        {
            if (now < a.PauseUntil) return (0, 0);
            if (!a.HasTarget)
            {
                double angle = a.Random.NextDouble() * 2 * Math.PI;
                double dist = Math.Sqrt(a.Random.NextDouble()) * radius;
                a.TargetX = Wrap(a.CenterX + Math.Cos(angle) * dist, pop.WorldWidth);
                a.TargetZ = Wrap(a.CenterZ + Math.Sin(angle) * dist, pop.WorldHeight);
                a.HasTarget = true;
            }
            if (Arrived(pop, a, a.TargetX, a.TargetZ, ArriveRadius))
            {
                a.HasTarget = false;
                a.PauseUntil = now + pop.GameMinutes(Between(a.Random, 0.2, 1.5));
                return (0, 0);
            }
            return Toward(pop, a, a.TargetX, a.TargetZ, ArriveRadius);
        }

        private static bool MemberFarBehind(BotPopulation pop, BotAccount leader)
        {
            double limit = pop.Settings.LeaderWaitDistance;
            foreach (var m in leader.Party.Members)
            {
                if (m == leader || !m.WantsOnline || !m.HasPosition) continue;
                var (dx, dz) = Delta(pop, leader.X, leader.Z, m.X, m.Z);
                if (dx * dx + dz * dz > limit * limit) return true;
            }
            return false;
        }

        private static bool Arrived(BotPopulation pop, BotAccount a, double tx, double tz, double radius)
        {
            var (dx, dz) = Delta(pop, a.X, a.Z, tx, tz);
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>The input (8 directions) that heads toward a point, or none once within the radius.</summary>
        internal static (int, int) Toward(BotPopulation pop, BotAccount a, double tx, double tz, double radius)
        {
            var (dx, dz) = Delta(pop, a.X, a.Z, tx, tz);
            double dist = Math.Sqrt(dx * dx + dz * dz);
            if (dist <= radius) return (0, 0);
            // Use an axis only when it's a fair share of the way; gives the closest of 8 directions.
            int h = Math.Abs(dx) > 0.4 * dist ? Math.Sign(dx) : 0;
            int v = Math.Abs(dz) > 0.4 * dist ? Math.Sign(dz) : 0;
            return (h, v);
        }

        /// <summary>Shortest offset from (ax, az) to (bx, bz) on the wrapping world.</summary>
        internal static (double, double) Delta(BotPopulation pop, double ax, double az, double bx, double bz)
        {
            return (WrapDelta(bx - ax, pop.WorldWidth), WrapDelta(bz - az, pop.WorldHeight));
        }

        private static double WrapDelta(double d, double size)
        {
            d %= size;
            if (d > size / 2) d -= size;
            else if (d < -size / 2) d += size;
            return d;
        }

        private static double Wrap(double value, double size)
        {
            double r = value % size;
            return r < 0 ? r + size : r;
        }

        private static double Between(Random r, double min, double max) => min + r.NextDouble() * (max - min);
    }
}
