using System;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Arsenal
{
    /// <summary>One gun's firing state: ammo, reload, the burst in progress and the beam's target.</summary>
    public struct GunState
    {
        public int Ammo;
        /// <summary>When the reload in progress finishes (0 = not reloading).</summary>
        public double ReloadEnds;
        public float ReloadTime;
        public double NextShot;
        /// <summary>Bullets left in the burst in progress, and when the next burst may start.</summary>
        public int BurstLeft;
        public double CycleEnds;
        /// <summary>No firing before this (just swapped to this gun).</summary>
        public double ReadyAt;
        public int BeamTarget;
        public double BeamSince;
        public double BeamLastHit;

        public bool Reloading => ReloadEnds > 0;

        public static GunState Full(GunStats stats) => new GunState { Ammo = stats?.Magazine ?? 0, BeamTarget = -1 };

        /// <summary>Reload progress 0..1 (1 when not reloading).</summary>
        public float ReloadProgress(double now) =>
            ReloadEnds <= 0 || ReloadTime <= 0f ? 1f : (float)Math.Max(0, Math.Min(1, 1 - (ReloadEnds - now) / ReloadTime));
    }

    /// <summary>
    /// Fire timing for every frame, shared by the server and (for drawing) the client. Holding the
    /// trigger fires at the gun's rate; a Burst frame fires its whole burst once started. An empty
    /// magazine reloads by itself; R reloads early. Reserve ammo is unlimited.
    /// </summary>
    public static class FireControl
    {
        /// <summary>Seconds after swapping guns before the new one can fire.</summary>
        public const float SwapTime = 0.3f;
        /// <summary>The beam builds up on a target as long as hits on it are no further apart than this.</summary>
        public const float BeamGrace = 0.3f;

        /// <summary>
        /// Advances one gun to <paramref name="now"/> and returns how many bullets (pulses, projectiles)
        /// it fires. <paramref name="rateScale"/> speeds up firing (skills, Frenzy): 1.1 = 10% faster;
        /// <paramref name="reloadScale"/> speeds up reloads the same way (Frenzy). With <paramref name="noReload"/>
        /// (Overdrive) shots use no ammo, and a reload in progress finishes at once.
        /// </summary>
        public static int Step(ref GunState st, GunStats s, double now, bool trigger, bool reloadPressed, float rateScale = 1f,
            float reloadScale = 1f, bool noReload = false)
        {
            if (s == null) return 0;
            if (st.ReloadEnds > 0)
            {
                if (now < st.ReloadEnds && !noReload) return 0;
                st.ReloadEnds = 0;
                st.Ammo = s.Magazine;
            }
            if (reloadPressed && st.Ammo < s.Magazine && !noReload)
            {
                StartReload(ref st, s, now, reloadScale);
                return 0;
            }
            // Overdrive: a free bullet for every one fired, so the magazine never runs down.
            int spare = noReload ? 1 : 0;
            if (now < st.ReadyAt) return 0;

            float interval = s.Interval / Math.Max(0.1f, rateScale);
            int shots = 0;
            for (int guard = 0; guard < 8; guard++)
            {
                if (st.BurstLeft > 0)
                {
                    if (now < st.NextShot) break;
                    if (st.Ammo <= 0) { st.BurstLeft = 0; break; }
                    st.Ammo -= 1 - spare;
                    shots++;
                    st.BurstLeft--;
                    st.NextShot = st.BurstLeft > 0 ? st.NextShot + s.BurstGap : st.CycleEnds;
                    if (st.Ammo == 0) { st.BurstLeft = 0; StartReload(ref st, s, now, reloadScale); break; }
                    continue;
                }
                if (!trigger || now < st.NextShot) break;
                if (st.Ammo <= 0)
                {
                    StartReload(ref st, s, now, reloadScale);
                    break;
                }
                // Don't bank up shots while the trigger was up: a fresh pull starts from now.
                double start = now - st.NextShot > interval ? now : st.NextShot;
                if (s.Mode == FireMode.Burst)
                {
                    st.BurstLeft = Math.Max(1, s.BurstCount);
                    st.NextShot = start;
                    st.CycleEnds = start + interval;
                    continue;
                }
                st.Ammo -= 1 - spare;
                shots++;
                st.NextShot = start + interval;
                if (st.Ammo == 0)
                {
                    StartReload(ref st, s, now, reloadScale);
                    break;
                }
            }
            return shots;
        }

        public static void StartReload(ref GunState st, GunStats s, double now, float reloadScale = 1f)
        {
            st.BurstLeft = 0;
            st.ReloadTime = s.Reload / Math.Max(0.1f, reloadScale);
            st.ReloadEnds = now + st.ReloadTime;
        }

        /// <summary>Call on the gun being swapped to: a short delay before it fires; a reload in progress restarts later.</summary>
        public static void SwappedIn(ref GunState st, double now)
        {
            st.ReadyAt = now + SwapTime;
            st.ReloadEnds = 0;
            st.BurstLeft = 0;
        }

        /// <summary>After the gun's mods change: ammo stays, capped by the new magazine.</summary>
        public static void Refit(ref GunState st, GunStats s)
        {
            if (s == null) return;
            if (st.Ammo > s.Magazine) st.Ammo = s.Magazine;
        }

        /// <summary>The beam's damage multiplier for a pulse on <paramref name="targetId"/> (-1 = no target): it builds while it stays on one target.</summary>
        public static float BeamMultiplier(ref GunState st, GunStats s, int targetId, double now)
        {
            if (s.Mode != FireMode.Beam) return 1f;
            if (targetId < 0)
            {
                st.BeamTarget = -1;
                return s.Frame.BeamMin;
            }
            if (targetId != st.BeamTarget || now - st.BeamLastHit > BeamGrace)
            {
                st.BeamTarget = targetId;
                st.BeamSince = now;
            }
            st.BeamLastHit = now;
            var f = s.Frame;
            float t = f.BeamRamp <= 0f ? 1f : (float)Math.Min(1.0, (now - st.BeamSince) / f.BeamRamp);
            return f.BeamMin + (f.BeamMax - f.BeamMin) * t;
        }

        /// <summary>A direction inside a cone of half-angle <paramref name="spreadDegrees"/> around <paramref name="aim"/>, evenly spread.</summary>
        public static Vector3 Spread(Vector3 aim, float spreadDegrees, ref CombatRandom rng)
        {
            aim = Vector3.Normalize(aim);
            if (spreadDegrees <= 0f) return aim;
            float angle = spreadDegrees * (float)(Math.PI / 180.0) * (float)Math.Sqrt(rng.NextFloat());
            float around = rng.NextFloat() * 2f * (float)Math.PI;
            var helper = Math.Abs(aim.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            var right = Vector3.Normalize(Vector3.Cross(helper, aim));
            var up = Vector3.Cross(aim, right);
            var offset = right * (float)Math.Cos(around) + up * (float)Math.Sin(around);
            return Vector3.Normalize(aim * (float)Math.Cos(angle) + offset * (float)Math.Sin(angle));
        }
    }
}
