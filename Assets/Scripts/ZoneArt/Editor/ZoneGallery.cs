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
    ///     [-zoneSeeds 2026,3,7] (several gallery seeds in one run, a sub-folder each) [-zoneInWorld] (build with InWorld = true)
    ///     [-zoneNoRender] (walkability and timing only) [-zoneHeadlessCheck] (also build each zone headless, time it and
    ///     compare its colliders with the full build)
    /// Zones are built through <see cref="ZoneBuild.Steps"/>; the report gives the wall time, the main-thread time
    /// (sum of all MoveNext calls) and the longest single MoveNext.
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

        /// <summary>Gallery options beyond generator, folder and seed.</summary>
        public sealed class Options
        {
            public int Count = 9;
            public int Only = -1;
            public int Width = 1600, Height = 900;
            public bool InWorld;
            public bool NoRender;
            public bool HeadlessCheck;
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
                bool Flag(string name) => Array.IndexOf(args, name) >= 0;
                string generator = Arg("-zoneGenerator", "sample");
                string outDir = Arg("-zoneOut", Path.Combine("Temp", "ZoneGallery", generator));
                var options = new Options
                {
                    Count = int.Parse(Arg("-zoneCount", "9")),
                    Only = int.Parse(Arg("-zoneOnly", "-1")),
                    Width = int.Parse(Arg("-zoneWidth", "1600")),
                    Height = int.Parse(Arg("-zoneHeight", "900")),
                    InWorld = Flag("-zoneInWorld"),
                    NoRender = Flag("-zoneNoRender"),
                    HeadlessCheck = Flag("-zoneHeadlessCheck"),
                };
                string seeds = Arg("-zoneSeeds", null);
                if (string.IsNullOrEmpty(seeds))
                {
                    Run(generator, outDir, uint.Parse(Arg("-zoneSeed", "1")), options);
                }
                else
                {
                    var all = new StringBuilder();
                    foreach (var part in seeds.Split(','))
                    {
                        uint seed = uint.Parse(part.Trim());
                        all.AppendLine(Run(generator, Path.Combine(outDir, $"seed{seed}"), seed, options));
                    }
                    File.WriteAllText(Path.Combine(Path.GetFullPath(outDir), "report.md"), all.ToString());
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZoneGallery] FAILED: {e}");
                exit = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(exit);
        }

        public static string Run(string generatorName, string outDir, int count = 9, uint seed = 1, int only = -1, int width = 1600, int height = 900) =>
            Run(generatorName, outDir, seed, new Options { Count = count, Only = only, Width = width, Height = height });

        /// <summary>Wall time, main-thread time (sum of MoveNext calls), longest MoveNext and step count of one zone build.</summary>
        public struct BuildTiming
        {
            public double Wall, MainMs, LongestMs;
            public int Steps;
            public override string ToString() => $"wall {Wall:0.00}s, main {MainMs:0}ms, longest step {LongestMs:0.0}ms, {Steps} steps";
        }

        /// <summary>Builds a zone through <see cref="ZoneBuild.Steps"/> right away (like <see cref="ZoneBuild.RunToEnd"/>), timing every step.</summary>
        public static ZoneResult BuildTimed(IZoneGenerator generator, ZoneRequest zone, Transform parent, out BuildTiming timing)
        {
            var result = new ZoneResult();
            var wall = Stopwatch.StartNew();
            var step = new Stopwatch();
            timing = default;
            var stack = new Stack<System.Collections.IEnumerator>();
            stack.Push(ZoneBuild.Steps(generator, zone, parent, result));
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                step.Restart();
                bool more = top.MoveNext();
                step.Stop();
                double ms = step.Elapsed.TotalMilliseconds;
                timing.MainMs += ms;
                timing.Steps++;
                if (ms > timing.LongestMs) timing.LongestMs = ms;
                if (!more)
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is System.Collections.IEnumerator nested) stack.Push(nested);
                else
                {
                    // A yield means "continue next frame": leave a short gap (not counted as main-thread time) so a
                    // generator waiting on worker threads is polled like a game would poll it, not millions of times.
                    var gap = Stopwatch.StartNew();
                    while (gap.Elapsed.TotalMilliseconds < 0.25) { }
                }
            }
            timing.Wall = wall.Elapsed.TotalSeconds;
            return result;
        }

        public static string Run(string generatorName, string outDir, uint seed, Options options)
        {
            var generator = ZoneGenerators.Find(generatorName);
            outDir = Path.GetFullPath(outDir);
            Directory.CreateDirectory(outDir);
            var zones = Zones(options.Count, seed);
            var report = new StringBuilder();
            string mode = (options.InWorld ? "in world" : "standalone") + (options.HeadlessCheck ? ", headless check" : "");
            report.AppendLine($"# Zone gallery: {generator.Name} (seed {seed}, {mode})\n");
            report.AppendLine("| # | Zone | Build s | Main ms | Longest step ms | Triangles | Two-way walkable | Edges N/E/S/W | Height p10..p90 (span) | Multi-level | Anchors | Headless s / main ms / longest ms | Notes |");
            report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float oldShadowDistance = urp != null ? urp.shadowDistance : 0f;
            if (urp != null) urp.shadowDistance = 350f;

            bool batch = Application.isBatchMode;
            var previousActive = SceneManager.GetActiveScene();
            var shots = new List<(int index, string kind, Color32[] thumb)>();
            try
            {
                for (int i = 0; i < zones.Count; i++)
                {
                    if (options.Only >= 0 && i != options.Only) continue;
                    var zone = zones[i];
                    zone.InWorld = options.InWorld;
                    zone.Headless = false;
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batch ? NewSceneMode.Single : NewSceneMode.Additive);
                    SceneManager.SetActiveScene(scene);
                    try
                    {
                        ZoneLook.Reset();
                        var holder = new GameObject("ZoneGallery");
                        var result = BuildTimed(generator, zone, holder.transform, out var timing);
                        if (RenderSettings.sun == null && UnityEngine.Object.FindAnyObjectByType<Light>() == null) ZoneLook.Sun(holder.transform, 45f, 30f, Color.white);
                        Physics.SyncTransforms();

                        var walk = Walkability.Analyze(zone);
                        long tris = CountTriangles(holder);

                        if (!options.NoRender)
                        {
                            var aerial = new ZoneView(zone.Origin + new Vector3(-45f, 150f, -45f), zone.Center + Vector3.up * 8f, 55f);
                            var hero = result.HeroViews.Count > 0 ? result.HeroViews[0] : new ZoneView(zone.Center + new Vector3(-70f, 35f, -70f), zone.Center, 60f);
                            var ground = GroundView(zone, walk, i, seed);

                            Save(shots, outDir, i, "aerial", Render(aerial, options.Width, options.Height));
                            Save(shots, outDir, i, "hero", Render(hero, options.Width, options.Height));
                            Save(shots, outDir, i, "ground", Render(ground, options.Width, options.Height));
                        }
                        File.WriteAllBytes(Path.Combine(outDir, $"zone{i}_walk.png"), WalkMap(walk).EncodeToPNG());

                        string headless = "";
                        if (options.HeadlessCheck)
                        {
                            string full = ColliderFingerprint(holder, out int fullColliders);
                            var hscene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batch ? NewSceneMode.Single : NewSceneMode.Additive);
                            SceneManager.SetActiveScene(hscene);
                            try
                            {
                                ZoneLook.Reset();
                                zone.Headless = true;
                                var hholder = new GameObject("ZoneGallery headless");
                                BuildTimed(generator, zone, hholder.transform, out var htiming);
                                string hf = ColliderFingerprint(hholder, out int headlessColliders);
                                bool same = hf == full;
                                headless = $"{htiming.Wall:0.00} / {htiming.MainMs:0} / {htiming.LongestMs:0.0}, colliders {(same ? "match" : $"MISMATCH ({fullColliders} vs {headlessColliders})")}";
                                Debug.Log($"[ZoneGallery] {generator.Name} zone {i} headless: {htiming}; colliders {(same ? "match" : "MISMATCH")} ({fullColliders} full, {headlessColliders} headless)");
                            }
                            finally
                            {
                                zone.Headless = false;
                                if (!batch)
                                {
                                    if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
                                    EditorSceneManager.CloseScene(hscene, true);
                                }
                            }
                        }

                        string edges = $"{walk.EdgeReach[0] * 100f:0}/{walk.EdgeReach[1] * 100f:0}/{walk.EdgeReach[2] * 100f:0}/{walk.EdgeReach[3] * 100f:0}%";
                        string notes = (result.Notes ?? "").Replace("|", "/").Replace("\n", " ");
                        report.AppendLine($"| {i} | {zone.Coord} {zone.Tile} | {timing.Wall:0.00} | {timing.MainMs:0} | {timing.LongestMs:0.0} | {tris:N0} | {walk.TwoWayShare * 100f:0}% of {walk.Walkable:N0} m² | {edges} | {walk.P10:0}..{walk.P90:0} m ({walk.Verticality:0}) | {walk.MultiLevelShare * 100f:0}% | {walk.AnchorsReachable}/{walk.AnchorsTotal}{(walk.UnreachableAnchors.Count > 0 ? " (missing " + string.Join(", ", walk.UnreachableAnchors) + ")" : "")} | {headless} | {notes} |");
                        Debug.Log($"[ZoneGallery] {generator.Name} zone {i} {zone}: built {timing}, {tris:N0} tris; {walk}");
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

        /// <summary>
        /// A digest of every collider under <paramref name="root"/> in world space (type, state, shape data), independent
        /// of hierarchy order and object names, so a headless build can be compared with a full one.
        /// </summary>
        public static string ColliderFingerprint(GameObject root, out int count)
        {
            var parts = new List<string>();
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                var sb = new StringBuilder();
                var tr = c.transform;
                sb.Append(c.GetType().Name).Append(c.enabled && c.gameObject.activeInHierarchy ? "+" : "-").Append(c.isTrigger ? "T" : "").Append(c.gameObject.layer).Append(':');
                ulong h = 1469598103934665603UL;
                void Mix(float f)
                {
                    unchecked
                    {
                        h ^= (uint)BitConverter.SingleToInt32Bits(f);
                        h *= 1099511628211UL;
                    }
                }
                void MixV(Vector3 v)
                {
                    Mix(v.x);
                    Mix(v.y);
                    Mix(v.z);
                }
                var m = tr.localToWorldMatrix;
                switch (c)
                {
                    case MeshCollider mc:
                    {
                        var mesh = mc.sharedMesh;
                        sb.Append(mc.convex ? "convex " : "").Append((int)mc.cookingOptions).Append(' ');
                        if (mesh == null) break;
                        var verts = mesh.vertices;
                        var tris = mesh.triangles;
                        sb.Append(verts.Length).Append('/').Append(tris.Length / 3).Append(' ');
                        foreach (var v in verts) MixV(m.MultiplyPoint3x4(v));
                        foreach (int t in tris) Mix(t);
                        break;
                    }
                    case BoxCollider bc:
                        MixV(m.MultiplyPoint3x4(bc.center));
                        MixV(Vector3.Scale(bc.size, tr.lossyScale));
                        MixV(tr.rotation.eulerAngles);
                        break;
                    case SphereCollider scol:
                        MixV(m.MultiplyPoint3x4(scol.center));
                        Mix(scol.radius * tr.lossyScale.x);
                        break;
                    case CapsuleCollider cc:
                        MixV(m.MultiplyPoint3x4(cc.center));
                        Mix(cc.radius);
                        Mix(cc.height);
                        Mix(cc.direction);
                        MixV(tr.lossyScale);
                        MixV(tr.rotation.eulerAngles);
                        break;
                    default:
                        MixV(c.bounds.min);
                        MixV(c.bounds.max);
                        break;
                }
                sb.Append(h.ToString("x16"));
                parts.Add(sb.ToString());
            }
            parts.Sort(StringComparer.Ordinal);
            count = parts.Count;
            return string.Join("\n", parts);
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

        // Contact-sheet tiles are kept as CPU thumbnails: in batch mode every NewScene(Single) unloads unused assets,
        // which destroyed the earlier zones' textures before the sheets were made (tiles came out repeated or scrambled).
        private const int ThumbW = 640, ThumbH = 360;

        private static void Save(List<(int, string, Color32[])> shots, string dir, int index, string kind, Texture2D tex)
        {
            File.WriteAllBytes(Path.Combine(dir, $"zone{index}_{kind}.png"), tex.EncodeToPNG());
            shots.Add((index, kind, Downscale(tex, ThumbW, ThumbH)));
            UnityEngine.Object.DestroyImmediate(tex);
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
        private static void Sheet(List<(int index, string kind, Color32[] thumb)> shots, string kind, string dir)
        {
            var list = shots.FindAll(s => s.kind == kind);
            if (list.Count == 0) return;
            const int tw = ThumbW, th = ThumbH, cols = 3;
            int rows = (list.Count + cols - 1) / cols;
            var sheet = new Texture2D(tw * cols, th * rows, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(16, 16, 20, 255);
            for (int k = 0; k < list.Count; k++)
            {
                var small = list[k].thumb;
                int cx = k % cols, cy = rows - 1 - k / cols;
                for (int y = 0; y < th; y++) Array.Copy(small, y * tw, fill, (cy * th + y) * sheet.width + cx * tw, tw);
            }
            sheet.SetPixels32(fill);
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"sheet_{kind}.png"), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        /// <summary>Box-filtered downscale (rows bottom-up, like Texture2D pixels).</summary>
        private static Color32[] Downscale(Texture2D src, int w, int h)
        {
            var px = src.GetPixels32();
            int sw = src.width, sh = src.height;
            var dst = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int y0 = y * sh / h, y1 = Mathf.Max(y0 + 1, (y + 1) * sh / h);
                for (int x = 0; x < w; x++)
                {
                    int x0 = x * sw / w, x1 = Mathf.Max(x0 + 1, (x + 1) * sw / w);
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        int row = yy * sw;
                        for (int xx = x0; xx < x1; xx++)
                        {
                            var c = px[row + xx];
                            r += c.r;
                            g += c.g;
                            b += c.b;
                            n++;
                        }
                    }
                    dst[y * w + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            }
            return dst;
        }
    }
}
