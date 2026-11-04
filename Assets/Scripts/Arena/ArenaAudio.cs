using System.Collections.Generic;
using FriendlyPersonShooter.Core.Combat;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// Shooting sounds, synthesized in code (no audio files): one voice per payload, a few variants each, and a
    /// random pitch and volume on every shot so a long burst doesn't drone. Your own shots play flat and soft;
    /// everyone else's play from where they were fired and fade with distance.
    /// </summary>
    public static class ArenaAudio
    {
        public static float Volume = 0.8f;

        private const int Rate = 44100;
        private const int Variants = 4;
        private const int Voices = 24;
        private const float HearingRange = 70f;

        private static Dictionary<PayloadKind, AudioClip[]> _clips;
        private static AudioSource[] _pool;
        private static int _next;
        private static readonly System.Random Rng = new System.Random();

        /// <summary>Plays one shot. <paramref name="own"/>: the local player's shot (not positioned).</summary>
        public static void Shot(PayloadKind payload, Vector3 position, bool own)
        {
            if (Application.isBatchMode || !EnsurePool()) return;
            var listener = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            float distance = own ? 0f : Vector3.Distance(position, listener);
            if (distance > HearingRange) return;

            var source = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            var variants = _clips[payload];
            source.clip = variants[Rng.Next(variants.Length)];
            source.pitch = Range(0.9f, 1.1f);
            source.volume = Volume * Range(0.8f, 1f) * (own ? 0.45f : 0.7f);
            source.spatialBlend = own ? 0f : 1f;
            source.transform.position = own ? listener : position;
            source.Play();
        }

        private static AudioClip[] _hurt;
        private static AudioClip _levelUp;

        /// <summary>The local player got hit by an enemy: a low, gritty buzz, unlike any shot.</summary>
        public static void Hurt()
        {
            if (Application.isBatchMode || !EnsurePool()) return;
            if (_hurt == null)
            {
                _hurt = new AudioClip[3];
                for (int v = 0; v < _hurt.Length; v++) _hurt[v] = MakeHurt(v);
            }
            PlayFlat(_hurt[Rng.Next(_hurt.Length)], Range(0.92f, 1.08f), 0.9f);
        }

        /// <summary>The local player reached a new level: a rising three-note arpeggio.</summary>
        public static void LevelUp()
        {
            if (Application.isBatchMode || !EnsurePool()) return;
            if (_levelUp == null) _levelUp = MakeLevelUp();
            PlayFlat(_levelUp, 1f, 0.7f);
        }

        private static void PlayFlat(AudioClip clip, float pitch, float volume)
        {
            var source = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            source.clip = clip;
            source.pitch = pitch;
            source.volume = Volume * volume;
            source.spatialBlend = 0f;
            source.Play();
        }

        private static AudioClip MakeHurt(int variant)
        {
            var rng = new System.Random(77 + variant);
            const float length = 0.28f;
            int n = (int)(Rate * length);
            var data = new float[n];
            float phase = 0f, low = 0f;
            float baseF = 110f + 12f * variant;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float u = t / length;
                float f = baseF * (1f - 0.35f * u);
                phase += f / Rate;
                // Square-ish buzz plus filtered noise: harsh, so it reads as "ouch".
                float sq = Mathf.Sign(Mathf.Sin(phase * 2f * Mathf.PI)) * 0.5f + 0.5f * Mathf.Sin(phase * 2f * Mathf.PI);
                float noise = (float)rng.NextDouble() * 2f - 1f;
                low += (noise - low) * 0.25f;
                float env = Mathf.Clamp01(t / 0.005f) * Mathf.Exp(-u * 4.5f);
                float tail = Mathf.Clamp01((n - i) / (Rate * 0.01f));
                data[i] = Mathf.Clamp((0.55f * sq + 0.6f * low) * env * tail * 0.8f, -1f, 1f);
            }
            var clip = AudioClip.Create($"Hurt {variant}", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeLevelUp()
        {
            const float note = 0.11f;
            float[] freqs = { 660f, 880f, 1320f };
            int per = (int)(Rate * note);
            int n = per * freqs.Length + (int)(Rate * 0.25f);
            var data = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                float phase = 0f;
                for (int i = per * k; i < n; i++)
                {
                    float t = (i - per * k) / (float)Rate;
                    phase += freqs[k] / Rate;
                    float env = Mathf.Clamp01(t / 0.004f) * Mathf.Exp(-t * 7f);
                    data[i] += 0.32f * Mathf.Sin(phase * 2f * Mathf.PI) * env;
                }
            }
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i] * Mathf.Clamp01((n - i) / (Rate * 0.01f)), -1f, 1f);
            var clip = AudioClip.Create("Level up", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Range(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        private static bool EnsurePool()
        {
            if (_pool != null && _pool[0] != null) return true;
            if (_clips == null)
            {
                _clips = new Dictionary<PayloadKind, AudioClip[]>();
                foreach (PayloadKind p in System.Enum.GetValues(typeof(PayloadKind)))
                {
                    var set = new AudioClip[Variants];
                    for (int v = 0; v < Variants; v++) set[v] = Make(p, v);
                    _clips[p] = set;
                }
            }
            var root = new GameObject("Arena Audio");
            Object.DontDestroyOnLoad(root);
            _pool = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("Voice");
                go.transform.SetParent(root.transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 4f;
                s.maxDistance = HearingRange;
                s.dopplerLevel = 0f;
                _pool[i] = s;
            }
            return true;
        }

        // ---------- Synthesis ----------

        private static AudioClip Make(PayloadKind payload, int variant)
        {
            var rng = new System.Random(1000 * (int)payload + variant + 7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float length;
            switch (payload)
            {
                case PayloadKind.Haste: length = 0.11f; break;
                case PayloadKind.Relay: length = 0.22f; break;
                case PayloadKind.Vigor: length = 0.16f; break;
                default: length = 0.14f; break;
            }
            int n = (int)(Rate * length);
            var data = new float[n];
            float phase = 0f, phase2 = 0f, low = 0f;
            // Each variant shifts the voice a little.
            float tune = R(0.93f, 1.07f), bright = R(0.8f, 1.2f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float u = t / length;
                float attack = Mathf.Clamp01(t / 0.004f);
                float s;
                switch (payload)
                {
                    case PayloadKind.Vigor:
                    {
                        // Soft rising "bloop" with a sparkle on top.
                        float f = Mathf.Lerp(620f, 1150f, Mathf.Sqrt(u)) * tune;
                        phase += f / Rate;
                        phase2 += f * 2.01f / Rate;
                        float env = attack * Mathf.Exp(-u * 5f);
                        s = (Mathf.Sin(phase * 2f * Mathf.PI) + 0.25f * bright * Mathf.Sin(phase2 * 2f * Mathf.PI)) * env;
                        break;
                    }
                    case PayloadKind.Haste:
                    {
                        // Quick falling zip.
                        float f = Mathf.Lerp(2300f, 520f, u) * tune;
                        phase += f / Rate;
                        float tri = 1f - 4f * Mathf.Abs(Mathf.Repeat(phase, 1f) - 0.5f);
                        float env = attack * (1f - u) * (1f - u);
                        s = tri * 0.9f * env;
                        break;
                    }
                    case PayloadKind.Relay:
                    {
                        // Two-tone chime that rings out.
                        float f1 = 880f * tune, f2 = 1320f * tune * R(0.995f, 1.005f);
                        phase += f1 / Rate;
                        phase2 += f2 / Rate;
                        float env = attack * Mathf.Exp(-u * 6f);
                        float second = u > 0.18f ? 1f : 0f;
                        s = (0.6f * Mathf.Sin(phase * 2f * Mathf.PI) + 0.5f * second * Mathf.Sin(phase2 * 2f * Mathf.PI)) * env;
                        break;
                    }
                    default:
                    {
                        // Might: a punchy thump, low sine drop under a filtered noise burst.
                        float f = Mathf.Lerp(190f, 70f, Mathf.Sqrt(u)) * tune;
                        phase += f / Rate;
                        float noise = (float)rng.NextDouble() * 2f - 1f;
                        low += (noise - low) * 0.18f * bright;
                        float env = attack * Mathf.Exp(-u * 7f);
                        s = (0.85f * Mathf.Sin(phase * 2f * Mathf.PI) + 0.9f * low * Mathf.Exp(-u * 14f)) * env;
                        break;
                    }
                }
                // Fade the last few milliseconds to avoid a click.
                float tail = Mathf.Clamp01((n - i) / (Rate * 0.006f));
                data[i] = Mathf.Clamp(s * tail * 0.8f, -1f, 1f);
            }
            var clip = AudioClip.Create($"{payload} shot {variant}", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
