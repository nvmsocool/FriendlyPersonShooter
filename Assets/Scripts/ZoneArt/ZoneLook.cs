using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>Scene-level look helpers: materials, sun, sky, ambient, fog and post-processing.</summary>
    public static class ZoneLook
    {
        public const string VertexColorLitShader = "ZoneArt/VertexColorLit";
        public const string GradientSkyShader = "ZoneArt/GradientSky";

        /// <summary>
        /// A material from a shader name (throws if the shader is missing, so typos fail loudly).
        /// Player builds only contain shaders something references: give every new zone shader a
        /// "ShaderRef" material in ZoneArt/Resources so Shader.Find still finds it in a build.
        /// </summary>
        public static Material Material(string shader, string name = null)
        {
            var s = Shader.Find(shader);
            if (s == null) throw new System.ArgumentException($"Shader '{shader}' not found.");
            return new Material(s) { name = name ?? shader, enableInstancing = true };
        }

        /// <summary>The starter vertex-color material.</summary>
        public static Material VertexColor(string name = "VertexColor", float wrap = 0.35f, float glow = 4f, bool doubleSided = false)
        {
            var m = Material(VertexColorLitShader, name);
            m.SetFloat("_Wrap", wrap);
            m.SetFloat("_Glow", glow);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            return m;
        }

        /// <summary>A directional light (the sun) with soft shadows, pointing along the given angles (degrees).</summary>
        public static Light Sun(Transform parent, float elevation, float azimuth, Color color, float intensity = 1.2f)
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;
            RenderSettings.sun = light;
            return light;
        }

        /// <summary>Three-color ambient (sky, horizon, ground). Cheap, deterministic, works in batch mode.</summary>
        public static void Ambient(Color sky, Color equator, Color ground, float intensity = 1f)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky * intensity;
            RenderSettings.ambientEquatorColor = equator * intensity;
            RenderSettings.ambientGroundColor = ground * intensity;
        }

        /// <summary>Exponential-squared distance fog. density ~0.004 hides a zone's far side softly.</summary>
        public static void Fog(Color color, float density)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = color;
            RenderSettings.fogDensity = density;
        }

        /// <summary>A global post-processing volume under <paramref name="parent"/>. Returns its profile to add or tweak overrides.</summary>
        public static VolumeProfile PostProcessing(Transform parent, float bloom = 0.8f, float bloomThreshold = 1f, float saturation = 10f,
            float contrast = 10f, float vignette = 0.2f, float postExposure = 0f)
        {
            var go = new GameObject("Post-processing");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var b = profile.Add<Bloom>(true);
            b.intensity.Override(bloom);
            b.threshold.Override(bloomThreshold);
            b.scatter.Override(0.7f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(saturation);
            ca.contrast.Override(contrast);
            ca.postExposure.Override(postExposure);
            var v = profile.Add<Vignette>(true);
            v.intensity.Override(vignette);
            v.smoothness.Override(0.5f);
            return profile;
        }

        /// <summary>Resets the scene look to a neutral default (the gallery calls this before each zone).</summary>
        public static void Reset()
        {
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            RenderSettings.fog = false;
            Ambient(new Color(0.5f, 0.55f, 0.6f), new Color(0.4f, 0.4f, 0.4f), new Color(0.2f, 0.2f, 0.2f));
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        }

        /// <summary>Hex color helper: Hex("#7fd1ae").</summary>
        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }
    }
}
