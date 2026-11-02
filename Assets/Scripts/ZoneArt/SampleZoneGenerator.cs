using UnityEngine;

namespace FriendlyPersonShooter.ZoneArt
{
    /// <summary>
    /// The baseline: rolling hills, scattered blob trees and boulders, a gradient sky. Shows how the
    /// framework fits together; it is deliberately plain.
    /// </summary>
    public sealed class SampleZoneGenerator : IZoneGenerator
    {
        public string Name => "sample";

        public ZoneResult Build(ZoneRequest zone, Transform parent)
        {
            var root = new GameObject($"Zone {zone.Coord}");
            root.transform.SetParent(parent, false);
            var result = new ZoneResult { Root = root };

            // Sky, light, ambient, fog, post.
            var sky = ZoneLook.Material(ZoneLook.GradientSkyShader, "Sky");
            RenderSettings.skybox = sky;
            ZoneLook.Sun(root.transform, 40f, 30f, new Color(1f, 0.95f, 0.85f));
            ZoneLook.Ambient(new Color(0.45f, 0.6f, 0.9f), new Color(0.7f, 0.65f, 0.6f), new Color(0.3f, 0.28f, 0.25f));
            ZoneLook.Fog(new Color(0.85f, 0.8f, 0.78f), 0.0035f);
            ZoneLook.PostProcessing(root.transform);

            // Ground.
            var o = ZoneNoise.Offset(zone.Seed, 1);
            var field = new Heightfield(zone, 2f, (x, z) =>
                ZoneEdges.Apply(zone, x, z, 4f + 10f * ZoneNoise.Fbm(x + o.x, z + o.y, 1f / 120f)));
            var ground = ZoneLook.VertexColor("Ground");
            field.Build(root.transform, ground, (p, n) => Color.Lerp(new Color(0.45f, 0.4f, 0.32f), new Color(0.35f, 0.6f, 0.25f), n.y * n.y));

            // Trees and rocks.
            var rng = ZoneRandom.For(zone.Seed, "plants");
            var trees = new MeshBuilder();
            foreach (var p in Scatter.Poisson(zone, 9f, ref rng, q => Scatter.AwayFromAnchors(zone, q, 8f)))
            {
                float y = field.HeightAt(p.x, p.y);
                float h = rng.Range(4f, 9f);
                var b = new Vector3(p.x, y, p.y);
                trees.Tube(b, b + Vector3.up * h, 0.35f, 0.2f, 6, new Color(0.35f, 0.25f, 0.18f), new Color(0.4f, 0.3f, 0.2f));
                trees.Blob(b + Vector3.up * h, new Vector3(2.5f, 2.2f, 2.5f), rng.Jitter(new Color(0.25f, 0.55f, 0.2f), 0.03f, 0.2f, 0.2f), ref rng);
            }
            trees.ToGameObject("Trees", root.transform, ZoneLook.VertexColor("Trees"), collider: true);

            result.HeroViews.Add(new ZoneView(zone.Center + new Vector3(-60f, 25f, -60f), zone.Center + Vector3.up * 5f, 60f));
            result.Notes = $"sample: {trees.TriangleCount} tree triangles";
            return result;
        }
    }
}
