using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace FriendlyPersonShooter.ZoneArt.EditorTools
{
    /// <summary>
    /// Renders a generator's zones to PNGs for judging, with a walkability report.
    ///
    /// For each zone: aerial (fixed pose, same for every generator), hero (the generator's first
    /// HeroView), ground (eye height at a seeded random spot of two-way walkable ground, looking
    /// toward the zone centre) and a walkability map. Plus contact sheets and report.md.
    ///
    /// Batch mode (from a project not open in an Editor):
    ///   unity run &lt;project&gt; -- -executeMethod FriendlyPersonShooter.ZoneArt.EditorTools.ZoneGallery.RunFromCommandLine
    ///     -zoneGenerator &lt;name&gt; -zoneOut &lt;dir&gt; [-zoneCount 9] [-zoneSeed 1] [-zoneOnly 3] [-zoneWidth 1600] [-zoneHeight 900]
    /// In a live Editor: ZoneGallery.Run("name", "dir", 9, 1, -1).
    /// </summary>
    public static class ZoneGallery
    {
        /// <summary>The zones judged for a gallery seed: one zone from each of <paramref name="count"/> different layers.</summary>
        public static List<ZoneRequest> Zones(int count, uint seed)
        {
            var list = new List<ZoneRequest>();
            for (int i = 0; i < count; i++)
            {
                uint layerSeed = ZoneRandom.Hash(seed, 0x6A11, i);
                var map = LayerGenerator.Generate(layerSeed, LayerSettings.MvpGrid(), LayerSettings.ForMvp());
                var zones = map.Zones;
                // Prefer zones with something on them, and vary the pick.
                int start = (int)(ZoneRandom.Hash(layerSeed, 99) % (uint)zones.Count);
                ZoneCoord coord = zones[start].Coord;
                for (int k = 0; k < zones.Count; k++)
                {
                    var z = zones[(start + k) % zones.Count];
                    if (z.Landmarks.Count + z.Shrines.Count > 0)
                    {
                        coord = z.Coord;
                        break;
                    }
                }
                list.Add(ZoneRequest.FromLayer(map, coord));
            }
            return list;
        }

        public static void RunFromCommandLine()
        {
            int exit = 0;
            try
            {
                var args = Environment.GetCommandLineArgs();
                string Arg(string name, string fallback)
                {
                    int i = Array.IndexOf(args, name);
                    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
                }
                string generator = Arg("-zoneGenerator", "sample");
                string outDir = Arg("-zoneOut", Path.Combine("Temp", "ZoneGallery", generator));
                int count = int.Parse(Arg("-zoneCount", "9"));
                uint seed = uint.Parse(Arg("-zoneSeed", "1"));
                int only = int.Parse(Arg("-zoneOnly", "-1"));
                int width = int.Parse(Arg("-zoneWidth", "1600"));
                int height = int.Parse(Arg("-zoneHeight", "900"));
                Run(generator, outDir, count, seed, only, width, height);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZoneGallery] FAILED: {e}");
                exit = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(exit);
        }

        public static string Run(string generatorName, string outDir, int count = 9, uint seed = 1, int only = -1, int width = 1600, int height = 900)
        {
            var generator = ZoneGenerators.Find(generatorName);
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);
            var zones = Zones(count, seed);
            var report = new StringBuilder();
            report.AppendLine($"# Zone gallery: {generator.Name} (seed {seed})\n");
            report.AppendLine("| # | Zone | Build s | Triangles | Two-way walkable | Edges N/E/S/W | Height p10..p90 (span) | Multi-level | Anchors | Notes |");
            report.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float oldShadowDistance = urp != null ? urp.shadowDistance : 0f;
            if (urp != null) urp.shadowDistance = 350f;

            bool batch = Application.isBatchMode;
            var previousActive = SceneManager.GetActiveScene();
            var shots = new List<(int index, string kind, Texture2D tex)>();
            try
            {
                for (int i = 0; i < zones.Count; i++)
                {
                    if (only >= 0 && i != only) continue;
                    var zone = zones[i];
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batch ? NewSceneMode.Single : NewSceneMode.Additive);
                    SceneManager.SetActiveScene(scene);
                    try
                    {
                        ZoneLook.Reset();
                        var holder = new GameObject("ZoneGallery");
                        var sw = Stopwatch.StartNew();
                        var result = generator.Build(zone, holder.transform);
                        double buildSeconds = sw.Elapsed.TotalSeconds;
                        if (RenderSettings.sun == null && UnityEngine.Object.FindAnyObjectByType<Light>() == null) ZoneLook.Sun(holder.transform, 45f, 30f, Color.white);
                        Physics.SyncTransforms();

                        var walk = Walkability.Analyze(zone);
                        long tris = CountTriangles(holder);

                        var aerial = new ZoneView(zone.Origin + new Vector3(-45f, 150f, -45f), zone.Center + Vector3.up * 8f, 55f);
                        var hero = result.HeroViews.Count > 0 ? result.HeroViews[0] : new ZoneView(zone.Center + new Vector3(-70f, 35f, -70f), zone.Center, 60f);
                        var ground = GroundView(zone, walk, i, seed);

                        Save(shots, outDir, i, "aerial", Render(aerial, width, height));
                        Save(shots, outDir, i, "hero", Render(hero, width, height));
                        Save(shots, outDir, i, "ground", Render(ground, width, height));
                        File.WriteAllBytes(Path.Combine(outDir, $"zone{i}_walk.png"), WalkMap(walk).EncodeToPNG());

                        string edges = $"{walk.EdgeReach[0] * 100f:0}/{walk.EdgeReach[1] * 100f:0}/{walk.EdgeReach[2] * 100f:0}/{walk.EdgeReach[3] * 100f:0}%";
                        string notes = (result.Notes ?? "").Replace("|", "/").Replace("\n", " ");
                        report.AppendLine($"| {i} | {zone.Coord} {zone.Tile} | {buildSeconds:0.0} | {tris:N0} | {walk.TwoWayShare * 100f:0}% of {walk.Walkable:N0} m² | {edges} | {walk.P10:0}..{walk.P90:0} m ({walk.Verticality:0}) | {walk.MultiLevelShare * 100f:0}% | {walk.AnchorsReachable}/{walk.AnchorsTotal} | {notes} |");
                        Debug.Log($"[ZoneGallery] {generator.Name} zone {i} {zone}: built in {buildSeconds:0.0}s, {tris:N0} tris; {walk}");
                    }
                    finally
                    {
                        if (!batch)
                        {
                            if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
                            EditorSceneManager.CloseScene(scene, true);
                        }
                    }
                }
            }
            finally
            {
                if (urp != null) urp.shadowDistance = oldShadowDistance;
            }

            foreach (var kind in new[] { "aerial", "hero", "ground" }) Sheet(shots, kind, outDir);
            File.WriteAllText(Path.Combine(outDir, "report.md"), report.ToString());
            Debug.Log($"[ZoneGallery] wrote {outDir}\n{report}");
            return report.ToString();
        }

        /// <summary>Builds one gallery zone into a new scene in the open Editor so you can fly around it.</summary>
        public static void OpenInScene(string generatorName, int index, uint seed = 1)
        {
            var generator = ZoneGenerators.Find(generatorName);
            var zone = Zones(index + 1, seed)[index];
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ZoneLook.Reset();
            var holder = new GameObject("ZoneGallery");
            var result = generator.Build(zone, holder.transform);
            var view = result.HeroViews.Count > 0 ? result.HeroViews[0] : new ZoneView(zone.Center + new Vector3(-70f, 35f, -70f), zone.Center, 60f);
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.transform.position = view.Position;
            cam.transform.LookAt(view.LookAt);
            cam.farClipPlane = 3000f;
            cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        }

        private static ZoneView GroundView(ZoneRequest zone, Walkability.Report walk, int index, uint seed)
        {
            var rng = new ZoneRandom(ZoneRandom.Hash(seed, 0x6D0E, index));
            if (walk.TwoWayPoints.Count == 0) return new ZoneView(zone.Center + Vector3.up * 30f, zone.Center + new Vector3(50f, 0f, 50f), 70f);
            // Prefer spots 30-110 m from the centre so the shot looks across the zone.
            Vector3 p = walk.TwoWayPoints[rng.Range(0, walk.TwoWayPoints.Count)];
            for (int t = 0; t < 40; t++)
            {
                var q = walk.TwoWayPoints[rng.Range(0, walk.TwoWayPoints.Count)];
                float d = Vector2.Distance(new Vector2(q.x, q.z), new Vector2(zone.Center.x, zone.Center.z));
                if (d > 30f && d < 110f)
                {
                    p = q;
                    break;
                }
            }
            var eye = p + Vector3.up * 1.6f;
            var target = new Vector3(zone.Center.x, eye.y + 4f, zone.Center.z);
            return new ZoneView(eye, target, 75f);
        }

        private static long CountTriangles(GameObject root)
        {
            long tris = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                for (int s = 0; s < m.subMeshCount; s++) tris += m.GetIndexCount(s) / 3;
            }
            return tris;
        }

        public static Texture2D Render(ZoneView view, int width, int height)
        {
            var go = new GameObject("GalleryCamera");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.position = view.Position;
                cam.transform.LookAt(view.LookAt);
                cam.fieldOfView = view.FieldOfView;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 4000f;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.allowHDR = true;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.renderShadows = true;

                var rt = new RenderTexture(new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_SRGB, 24) { msaaSamples = 4 });
                cam.targetTexture = rt;
                cam.Render(); // warm-up (shadow atlases, volumes)
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                return tex;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void Save(List<(int, string, Texture2D)> shots, string dir, int index, string kind, Texture2D tex)
        {
            File.WriteAllBytes(Path.Combine(dir, $"zone{index}_{kind}.png"), tex.EncodeToPNG());
            shots.Add((index, kind, tex));
        }

        private static Texture2D WalkMap(Walkability.Report walk)
        {
            int n = Mathf.Max(walk.MapSize, 1);
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length && walk.Map != null; i++)
            {
                px[i] = walk.Map[i] switch
                {
                    2 => new Color32(60, 200, 90, 255),
                    1 => new Color32(230, 170, 40, 255),
                    _ => new Color32(30, 30, 40, 255),
                };
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>A 3-wide contact sheet of one kind of shot.</summary>
        private static void Sheet(List<(int index, string kind, Texture2D tex)> shots, string kind, string dir)
        {
            var list = shots.FindAll(s => s.kind == kind);
            if (list.Count == 0) return;
            const int tw = 640, th = 360, cols = 3;
            int rows = (list.Count + cols - 1) / cols;
            var sheet = new Texture2D(tw * cols, th * rows, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(16, 16, 20, 255);
            sheet.SetPixels32(fill);
            for (int k = 0; k < list.Count; k++)
            {
                var small = Scale(list[k].tex, tw, th);
                int cx = k % cols, cy = rows - 1 - k / cols;
                sheet.SetPixels(cx * tw, cy * th, tw, th, small.GetPixels());
                UnityEngine.Object.DestroyImmediate(small);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"sheet_{kind}.png"), sheet.EncodeToPNG());
        }

        private static Texture2D Scale(Texture2D src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }
    }
}
