using System;
using System.Numerics;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Arsenal
{
    /// <summary>Where a gun's payload capsule is.</summary>
    public enum CapsuleState : byte
    {
        /// <summary>On the gun (full, part-full or empty).</summary>
        Seated = 0,
        /// <summary>Flung into the air: shoot it for a full capsule.</summary>
        Flying = 1,
        /// <summary>Missed: a new, empty one is growing on the gun.</summary>
        Regrowing = 2,
    }

    /// <summary>What one <see cref="FireControl.Step"/> fired.</summary>
    public struct FireStep
    {
        /// <summary>Bullets (pulses, projectiles) fired.</summary>
        public int Shots;
        /// <summary>The first this many of them carry the payload; the rest are plain.</summary>
        public int Payloaded;
        /// <summary>One of the shots flung the empty capsule into the air.</summary>
        public bool Launched;
    }

    /// <summary>One gun's firing state: payload charge and capsule, the burst in progress and the beam's target.</summary>
    public struct GunState
    {
        /// <summary>Payload charge in the capsule: each payload shot uses one.</summary>
        public int Charge;
        public CapsuleState Capsule;
        /// <summary>Flying: when it was launched. Regrowing: when the new one is grown.</summary>
        public double CapsuleAt;
        /// <summary>Regrowing: how long the new one takes.</summary>
        public float RegrowTime;
        public double NextShot;
        /// <summary>Bullets left in the burst in progress, and when the next burst may start.</summary>
        public int BurstLeft;
        public double CycleEnds;
        public int BeamTarget;
        public double BeamSince;
        public double BeamLastHit;

        public static GunState Full(GunStats stats) => new GunState { Charge = stats?.Magazine ?? 0, BeamTarget = -1 };

        /// <summary>How grown the capsule on the gun is, 0..1 (1 when seated, 0 while it's flying).</summary>
        public float CapsuleGrown(double now)
        {
            switch (Capsule)
            {
                case CapsuleState.Flying: return 0f;
                case CapsuleState.Regrowing:
                    return RegrowTime <= 0f ? 1f : (float)Math.Max(0, Math.Min(1, 1 - (CapsuleAt - now) / RegrowTime));
                default: return 1f;
            }
        }
    }

    /// <summary>
    /// Fire timing for every frame, shared by the server and (for drawing) the client. Holding the trigger fires at
    /// the gun's rate; a Burst frame fires its whole burst once started. Guns never stop firing: what runs out is the
    /// payload. Each payload shot uses one charge from the capsule on top of the gun (its size is the frame's
    /// magazine). Once it's empty, the next shot flings the capsule into the air (<see cref="CapsuleFlight"/>): shoot
    /// it for a full capsule (<see cref="Catch"/>), or miss and a new, empty one regrows over the frame's reload time.
    /// Shots without charge are plain (damage, and the plain heal on allies).
    /// </summary>
    public static class FireControl
    {
        /// <summary>The beam builds up on a target as long as hits on it are no further apart than this.</summary>
        public const float BeamGrace = 0.3f;

        /// <summary>
        /// Advances one gun to <paramref name="now"/>: what it fires, how many carry the payload, and whether the
        /// capsule was launched. <paramref name="rateScale"/> speeds up firing (skills, Frenzy): 1.1 = 10% faster;
        /// <paramref name="regrowScale"/> speeds up regrowing the same way (Frenzy). With <paramref name="overdrive"/>
        /// payload shots use no charge.
        /// </summary>
        public static FireStep Step(ref GunState st, GunStats s, double now, bool trigger, float rateScale = 1f,
            float regrowScale = 1f, bool overdrive = false)
        {
            var r = new FireStep();
            if (s == null) return r;
            StepCapsule(ref st, s, now, regrowScale);

            float interval = s.Interval / Math.Max(0.1f, rateScale);
            for (int guard = 0; guard < 8; guard++)
            {
                if (st.BurstLeft > 0)
                {
                    if (now < st.NextShot) break;
                    Fire(ref st, ref r, overdrive, now);
                    st.BurstLeft--;
                    st.NextShot = st.BurstLeft > 0 ? st.NextShot + s.BurstGap : st.CycleEnds;
                    continue;
                }
                if (!trigger || now < st.NextShot) break;
                // Don't bank up shots while the trigger was up: a fresh pull starts from now.
                double start = now - st.NextShot > interval ? now : st.NextShot;
                if (s.Mode == FireMode.Burst)
                {
                    st.BurstLeft = Math.Max(1, s.BurstCount);
                    st.NextShot = start;
                    st.CycleEnds = start + interval;
                    continue;
                }
                Fire(ref st, ref r, overdrive, now);
                st.NextShot = start + interval;
            }
            return r;
        }

        /// <summary>One bullet: it carries the payload while the capsule has charge; an empty seated capsule is launched.</summary>
        private static void Fire(ref GunState st, ref FireStep r, bool overdrive, double now)
        {
            r.Shots++;
            if (st.Capsule != CapsuleState.Seated) return;
            if (st.Charge > 0)
            {
                // Charge only runs out within a step, so the payload shots always come first.
                r.Payloaded++;
                if (!overdrive) st.Charge--;
                return;
            }
            if (overdrive) return;
            st.Capsule = CapsuleState.Flying;
            st.CapsuleAt = now;
            r.Launched = true;
        }

        /// <summary>A flying capsule not shot in time is lost and a new one starts growing; a grown one seats, empty.</summary>
        public static void StepCapsule(ref GunState st, GunStats s, double now, float regrowScale = 1f)
        {
            if (st.Capsule == CapsuleState.Flying && now - st.CapsuleAt >= CapsuleFlight.Life)
            {
                st.Capsule = CapsuleState.Regrowing;
                st.RegrowTime = s.Reload / Math.Max(0.1f, regrowScale);
                st.CapsuleAt = now + st.RegrowTime;
            }
            if (st.Capsule == CapsuleState.Regrowing && now >= st.CapsuleAt)
            {
                st.Capsule = CapsuleState.Seated;
                st.Charge = 0;
            }
        }

        /// <summary>The flying capsule was shot: it's back on the gun, full.</summary>
        public static bool Catch(ref GunState st, GunStats s)
        {
            if (st.Capsule != CapsuleState.Flying || s == null) return false;
            st.Capsule = CapsuleState.Seated;
            st.Charge = s.Magazine;
            return true;
        }

        /// <summary>
        /// A friend shot your capsule's partner out of the air: yours is full too, whatever state it was in
        /// (a regrowing one is back at once). Use <see cref="Catch"/> for a capsule that's flying.
        /// </summary>
        public static bool Refill(ref GunState st, GunStats s)
        {
            if (s == null) return false;
            st.Capsule = CapsuleState.Seated;
            st.Charge = s.Magazine;
            return true;
        }

        /// <summary>After the gun's mods change: charge stays, capped by the new capsule size.</summary>
        public static void Refit(ref GunState st, GunStats s)
        {
            if (s == null) return;
            if (st.Charge > s.Magazine) st.Charge = s.Magazine;
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
