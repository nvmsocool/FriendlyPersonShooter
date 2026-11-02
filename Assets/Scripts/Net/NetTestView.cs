using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// Debug view for the networking test, with two modes (M switches):
    /// - Lobby map: the whole world from above, every player colored by lobby, flashes where lobbies
    ///   split or merge. Reads the in-process server, so it only works when the server runs here.
    /// - Player view: what the playable client actually receives, following the local player.
    /// Uses instanced drawing, so no Entities Graphics is needed.
    /// </summary>
    public class NetTestView : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 60f, -45f);
        [SerializeField] private bool startInMap = true;

        private const int PaletteSize = 16;
        private const float FlashSeconds = 2.5f;

        private Mesh _cube;
        private Material _localMat, _otherMat, _tileMatA, _tileMatB, _mergeMat, _splitMat, _duplicateMat;
        private readonly List<Matrix4x4> _duplicates = new List<Matrix4x4>();
        private readonly Material[] _lobbyMats = new Material[PaletteSize];
        private readonly List<Matrix4x4>[] _byColor = new List<Matrix4x4>[PaletteSize];
        private readonly List<Matrix4x4> _others = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _tilesA = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _tilesB = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _scratch = new List<Matrix4x4>();
        private readonly Dictionary<int, List<Vector3>> _members = new Dictionary<int, List<Vector3>>();
        private readonly Dictionary<int, int> _colorOf = new Dictionary<int, int>();
        private readonly int[] _colorUse = new int[PaletteSize];
        private readonly List<int> _staleLobbies = new List<int>();
        private readonly Stack<List<Vector3>> _spareLists = new Stack<List<Vector3>>();
        private readonly StringBuilder _text = new StringBuilder();
        private Text _stats;
        private EntityQuery _clientQuery, _serverQuery, _partyQuery;
        private readonly Dictionary<int, List<Vector3>> _parties = new Dictionary<int, List<Vector3>>();
        private readonly List<Matrix4x4> _partyMarks = new List<Matrix4x4>();
        private World _clientQueryWorld, _serverQueryWorld;
        private bool _mapMode;
        private float _fps;

        private void Start()
        {
            // Headless server and bot processes have nothing to draw.
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }
            if (viewCamera == null) viewCamera = Camera.main;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cube = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);

            var baseMat = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultMaterial
                : null;
            if (baseMat == null)
            {
                Debug.LogError("[NetTest] No render pipeline default material found.");
                enabled = false;
                return;
            }
            _localMat = MakeMat(baseMat, new Color(1f, 0.85f, 0.1f));
            _otherMat = MakeMat(baseMat, new Color(0.25f, 0.6f, 1f));
            _tileMatA = MakeMat(baseMat, new Color(0.32f, 0.34f, 0.3f));
            _tileMatB = MakeMat(baseMat, new Color(0.26f, 0.28f, 0.25f));
            _mergeMat = MakeMat(baseMat, Color.white);
            _splitMat = MakeMat(baseMat, new Color(1f, 0.15f, 0.15f));
            _duplicateMat = MakeMat(baseMat, new Color(0.75f, 0.42f, 0.12f));
            for (int i = 0; i < PaletteSize; i++)
            {
                // Golden-ratio hue steps keep consecutive colors far apart.
                float hue = (i * 0.618034f) % 1f;
                _lobbyMats[i] = MakeMat(baseMat, Color.HSVToRGB(hue, 0.7f, 0.95f));
                _byColor[i] = new List<Matrix4x4>();
            }

            BuildTiles();
            BuildStatsPanel();
            _mapMode = startInMap;
        }

        private static Material MakeMat(Material source, Color color)
        {
            return new Material(source) { enableInstancing = true, color = color };
        }

        private void BuildTiles()
        {
            float zone = NetTestSettings.ZoneSize;
            int grid = NetTestSettings.GridSize;
            for (int x = 0; x < grid; x++)
            {
                for (int z = 0; z < grid; z++)
                {
                    var m = Matrix4x4.TRS(new Vector3((x + 0.5f) * zone, -0.1f, (z + 0.5f) * zone),
                        Quaternion.identity, new Vector3(zone, 0.2f, zone));
                    ((x + z) % 2 == 0 ? _tilesA : _tilesB).Add(m);
                }
            }
        }

        private void BuildStatsPanel()
        {
            var canvasGo = new GameObject("NetTestStats", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var textGo = new GameObject("Text", typeof(Text), typeof(Shadow));
            textGo.transform.SetParent(canvasGo.transform, false);
            _stats = textGo.GetComponent<Text>();
            _stats.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _stats.fontSize = 16;
            _stats.color = Color.white;
            var rect = _stats.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, -12f);
            rect.sizeDelta = new Vector2(620f, 520f);
        }

        private static World FindWorld(bool server)
        {
            foreach (var world in World.All)
            {
                if (server ? world.IsServer() : world.IsClient() && !world.IsThinClient()) return world;
            }
            return null;
        }

        private static EntityQuery PlayerQuery(World world)
        {
            return world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<GhostOwner>());
        }

        private void LateUpdate()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f);
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame) _mapMode = !_mapMode;

            var client = FindWorld(server: false);
            var server = FindWorld(server: true);
            int myId = -1;
            if (client != null && client.IsCreated)
            {
                using var idQuery = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
                if (idQuery.CalculateEntityCount() == 1) myId = idQuery.GetSingleton<NetworkId>().Value;
            }
            int myLobby = 0;
            if (myId >= 0) LobbyStats.LobbyOfPlayer.TryGetValue(myId, out myLobby);

            _text.Clear();
            if (_mapMode && server != null && server.IsCreated)
            {
                DrawMap(server, myId);
                WriteMapText(myLobby);
            }
            else
            {
                DrawPlayerView(client, myId, myLobby);
            }
            if (_stats != null) _stats.text = _text.ToString();
        }

        // ---------- Lobby map ----------

        private void DrawMap(World server, int myId)
        {
            float size = NetTestSettings.WorldSize;
            float zone = NetTestSettings.ZoneSize;
            DrawList(_tilesA, _tileMatA, Matrix4x4.identity);
            DrawList(_tilesB, _tileMatB, Matrix4x4.identity);
            PlaceMapCamera(size);

            // Zones that more than one lobby has loaded, so the server simulates them more than once.
            _duplicates.Clear();
            foreach (var z in LobbyStats.Cost.DuplicatedZones)
            {
                _duplicates.Add(Matrix4x4.TRS(new Vector3((z.X + 0.5f) * zone, 0.05f, (z.Y + 0.5f) * zone),
                    Quaternion.identity, new Vector3(zone * 0.94f, 0.2f, zone * 0.94f)));
            }
            DrawList(_duplicates, _duplicateMat, Matrix4x4.identity);

            if (_serverQueryWorld != server)
            {
                _serverQueryWorld = server;
                _serverQuery = PlayerQuery(server);
                _partyQuery = server.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<PlayerAccount>());
            }

            // Group every player's position by lobby.
            foreach (var list in _members.Values)
            {
                list.Clear();
                _spareLists.Push(list);
            }
            _members.Clear();
            Vector3? myPos = null;
            using (var transforms = _serverQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var owners = _serverQuery.ToComponentDataArray<GhostOwner>(Allocator.Temp))
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    int id = owners[i].NetworkId;
                    Vector3 p = transforms[i].Position;
                    if (id == myId) myPos = p;
                    LobbyStats.LobbyOfPlayer.TryGetValue(id, out int lobby);
                    if (!_members.TryGetValue(lobby, out var list))
                    {
                        list = _spareLists.Count > 0 ? _spareLists.Pop() : new List<Vector3>();
                        _members[lobby] = list;
                    }
                    list.Add(p);
                }
            }

            AssignColors();
            foreach (var colorList in _byColor) colorList.Clear();
            foreach (var kv in _members)
            {
                var color = _byColor[_colorOf[kv.Key]];
                var players = kv.Value;
                for (int i = 0; i < players.Count; i++)
                {
                    color.Add(Matrix4x4.TRS(players[i] + Vector3.up * 3f, Quaternion.identity, Vector3.one * 7f));

                    // Link each player to the nearest player in the same lobby, the short way around the wrap.
                    Vector3 best = Vector3.zero;
                    float bestDist = float.MaxValue;
                    for (int j = 0; j < players.Count; j++)
                    {
                        if (i == j) continue;
                        Vector3 d = WrappedDelta(players[i], players[j], size);
                        float dist = d.sqrMagnitude;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = d;
                        }
                    }
                    if (bestDist < float.MaxValue && bestDist > 0.01f)
                    {
                        Vector3 mid = players[i] + best * 0.5f + Vector3.up * 1f;
                        color.Add(Matrix4x4.TRS(mid, Quaternion.LookRotation(best),
                            new Vector3(1.5f, 0.5f, best.magnitude)));
                    }
                }
            }
            for (int c = 0; c < PaletteSize; c++) DrawList(_byColor[c], _lobbyMats[c], Matrix4x4.identity);
            DrawParties(size);

            if (myPos.HasValue)
            {
                // A yellow square frame around you.
                const float frame = 22f, bar = 2.5f;
                var p = myPos.Value + Vector3.up * 2f;
                var rp = new RenderParams(_localMat);
                Graphics.RenderMesh(rp, _cube, 0, Matrix4x4.TRS(p + new Vector3(0f, 0f, frame / 2), Quaternion.identity, new Vector3(frame + bar, 1f, bar)));
                Graphics.RenderMesh(rp, _cube, 0, Matrix4x4.TRS(p - new Vector3(0f, 0f, frame / 2), Quaternion.identity, new Vector3(frame + bar, 1f, bar)));
                Graphics.RenderMesh(rp, _cube, 0, Matrix4x4.TRS(p + new Vector3(frame / 2, 0f, 0f), Quaternion.identity, new Vector3(bar, 1f, frame + bar)));
                Graphics.RenderMesh(rp, _cube, 0, Matrix4x4.TRS(p - new Vector3(frame / 2, 0f, 0f), Quaternion.identity, new Vector3(bar, 1f, frame + bar)));
            }

            // Flash a growing square on the zone where each recent split (red) or merge (white) happened.
            double now = Time.unscaledTimeAsDouble;
            foreach (var e in LobbyStats.RecentEvents)
            {
                float age = (float)(now - e.Time);
                if (age < 0f || age > FlashSeconds) continue;
                float t = age / FlashSeconds;
                float side = zone * Mathf.Lerp(0.3f, 1f, t);
                float height = Mathf.Lerp(3f, 0.3f, t);
                var center = new Vector3((e.Zone.X + 0.5f) * zone, height * 0.5f, (e.Zone.Y + 0.5f) * zone);
                var m = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(side, height, side));
                Graphics.RenderMesh(new RenderParams(e.Kind == LobbyEventKind.Split ? _splitMat : _mergeMat),
                    _cube, 0, m);
            }
        }

        /// <summary>Party members get a white cap and a white line to their party's first member.</summary>
        private void DrawParties(float size)
        {
            foreach (var list in _parties.Values)
            {
                list.Clear();
                _spareLists.Push(list);
            }
            _parties.Clear();
            using (var transforms = _partyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var accounts = _partyQuery.ToComponentDataArray<PlayerAccount>(Allocator.Temp))
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    int group = accounts[i].GroupId;
                    if (group == 0) continue;
                    if (!_parties.TryGetValue(group, out var list))
                    {
                        list = _spareLists.Count > 0 ? _spareLists.Pop() : new List<Vector3>();
                        _parties[group] = list;
                    }
                    list.Add(transforms[i].Position);
                }
            }

            _partyMarks.Clear();
            foreach (var members in _parties.Values)
            {
                if (members.Count < 2) continue;
                for (int i = 0; i < members.Count; i++)
                {
                    _partyMarks.Add(Matrix4x4.TRS(members[i] + Vector3.up * 7f, Quaternion.identity, new Vector3(4f, 1f, 4f)));
                    if (i == 0) continue;
                    Vector3 d = WrappedDelta(members[i], members[0], size);
                    if (d.sqrMagnitude < 0.01f) continue;
                    Vector3 mid = members[i] + d * 0.5f + Vector3.up * 7f;
                    _partyMarks.Add(Matrix4x4.TRS(mid, Quaternion.LookRotation(d), new Vector3(0.8f, 0.5f, d.magnitude)));
                }
            }
            DrawList(_partyMarks, _mergeMat, Matrix4x4.identity);
        }

        private void PlaceMapCamera(float size)
        {
            if (viewCamera == null) return;
            viewCamera.orthographic = true;
            float half = size * 0.5f;
            // Fit the whole square, leaving the left part of the screen for the text panel.
            float aspect = Mathf.Max(viewCamera.aspect, 0.1f);
            viewCamera.orthographicSize = Mathf.Max(half * 1.05f, half * 1.05f / aspect * 1.4f);
            float shift = viewCamera.orthographicSize * aspect * 0.3f;
            viewCamera.transform.SetPositionAndRotation(new Vector3(half - shift, 500f, half),
                Quaternion.Euler(90f, 0f, 0f));
        }

        private void WriteMapText(int myLobby)
        {
            _text.Append($"Lobby map   {_fps:0} fps   (M: player view)\n");
            _text.Append($"{LobbyStats.Players} players, {LobbyStats.Lobbies} lobbies, " +
                         $"avg {LobbyStats.AverageSize:0.0}, largest {LobbyStats.LargestLobby}\n");
            _text.Append($"Splits {LobbyStats.TotalSplits}, merges {LobbyStats.TotalMerges}, " +
                         $"party moves {LobbyStats.TotalGroupJoins}\n");
            _text.Append($"Parties {LobbyStats.Parties} ({LobbyStats.PlayersInParties} players)\n");
            var pop = BotDirector.Population;
            if (pop != null)
            {
                double now = BotDirector.Now;
                double hour = pop.HourAt(now);
                _text.Append($"Bot clock {(int)hour:00}:{(int)(hour % 1 * 60):00}, online {pop.OnlineCount} of {pop.Accounts.Count} " +
                             $"(target {pop.TargetShare(now) * pop.Accounts.Count:0})\n");
                var st = pop.Stats;
                _text.Append($"Logins {st.Logins}, logoffs {st.Logouts}, drops {st.Drops}\n");
            }
            var cost = LobbyStats.Cost;
            _text.Append($"Zone copies {cost.LoadedInstances} for {cost.DistinctLoadedZones} zones in use " +
                         $"(duplication {cost.Duplication:0.00}x), {cost.LoadedZonesPerPlayer:0.0} per player\n\n");
            _text.Append("Each color is one lobby. Lines join each player\nto the nearest player in the same lobby.\n");
            _text.Append("Orange zones are loaded by 2+ lobbies (simulated twice).\n");
            _text.Append("White flash = merge or party move, red flash = split.\n");
            _text.Append("White caps and lines join party members.\n");
            _text.Append($"Yellow frame = you (lobby {myLobby}).\n\n");
            _text.Append("Latest:\n");
            var events = LobbyStats.RecentEvents;
            for (int i = events.Count - 1; i >= 0 && i >= events.Count - 12; i--)
            {
                var e = events[i];
                if (e.Kind == LobbyEventKind.Merge)
                {
                    _text.Append($"  merge  lobby {e.LobbyB} joined lobby {e.LobbyA}, now {e.PlayersA} players\n");
                }
                else if (e.Kind == LobbyEventKind.Group)
                {
                    _text.Append($"  party  players left lobby {e.LobbyB} to join their party in lobby {e.LobbyA}\n");
                }
                else
                {
                    _text.Append($"  split  lobby {e.LobbyA} ({e.PlayersA}) and new lobby {e.LobbyB} ({e.PlayersB})\n");
                }
            }
        }

        // ---------- Player view ----------

        private void DrawPlayerView(World world, int myId, int myLobby)
        {
            if (viewCamera != null) viewCamera.orthographic = false;
            float size = NetTestSettings.WorldSize;
            // Draw the tiles 3x3 times so the wrapping world has no visible edge.
            for (int ox = -1; ox <= 1; ox++)
            {
                for (int oz = -1; oz <= 1; oz++)
                {
                    var offset = Matrix4x4.Translate(new Vector3(ox * size, 0f, oz * size));
                    DrawList(_tilesA, _tileMatA, offset);
                    DrawList(_tilesB, _tileMatB, offset);
                }
            }

            int visible = 0;
            Vector3? localPos = null;
            if (world != null && world.IsCreated)
            {
                if (_clientQueryWorld != world)
                {
                    _clientQueryWorld = world;
                    _clientQuery = PlayerQuery(world);
                }

                _others.Clear();
                using var transforms = _clientQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var owners = _clientQuery.ToComponentDataArray<GhostOwner>(Allocator.Temp);
                visible = transforms.Length;
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (owners[i].NetworkId == myId) localPos = transforms[i].Position;
                }
                for (int i = 0; i < transforms.Length; i++)
                {
                    Vector3 p = transforms[i].Position;
                    // Show each other player at whichever wrapped copy is nearest to us.
                    if (localPos.HasValue) p = localPos.Value + WrappedDelta(localPos.Value, p, size);
                    var m = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * 1.5f);
                    if (owners[i].NetworkId == myId) Graphics.RenderMesh(new RenderParams(_localMat), _cube, 0, m);
                    else _others.Add(m);
                }
                DrawList(_others, _otherMat, Matrix4x4.identity);
            }

            if (localPos.HasValue && viewCamera != null)
            {
                viewCamera.transform.position = localPos.Value + cameraOffset;
                viewCamera.transform.LookAt(localPos.Value);
            }

            string zone = localPos.HasValue
                ? NetTestSettings.CreateGrid().ZoneAt(localPos.Value.x, localPos.Value.z).ToString()
                : "-";
            _text.Append($"Player view   {_fps:0} fps   (M: lobby map)\n");
            _text.Append($"You are the yellow cube: player {myId}, lobby {myLobby}, zone {zone}\n");
            _text.Append($"Blue cubes are the other players in your lobby. You see {visible} (incl. you).\n");
            _text.Append($"Server: {LobbyStats.Players} players, {LobbyStats.Lobbies} lobbies\n");
            _text.Append("WASD to move");
        }

        // ---------- Helpers ----------

        /// <summary>
        /// Keeps each lobby's color while it exists and gives new lobbies the least-used color,
        /// so neighbors rarely match. After a merge the surviving lobby keeps its color.
        /// </summary>
        private void AssignColors()
        {
            _staleLobbies.Clear();
            foreach (var id in _colorOf.Keys)
            {
                if (!_members.ContainsKey(id)) _staleLobbies.Add(id);
            }
            foreach (var id in _staleLobbies) _colorOf.Remove(id);

            System.Array.Clear(_colorUse, 0, PaletteSize);
            foreach (var c in _colorOf.Values) _colorUse[c]++;
            foreach (var id in _members.Keys)
            {
                if (_colorOf.ContainsKey(id)) continue;
                int best = 0;
                for (int c = 1; c < PaletteSize; c++)
                {
                    if (_colorUse[c] < _colorUse[best]) best = c;
                }
                _colorOf[id] = best;
                _colorUse[best]++;
            }
        }

        private void DrawList(List<Matrix4x4> matrices, Material mat, Matrix4x4 offset)
        {
            if (matrices.Count == 0) return;
            if (offset == Matrix4x4.identity)
            {
                Graphics.RenderMeshInstanced(new RenderParams(mat), _cube, 0, matrices);
                return;
            }
            _scratch.Clear();
            foreach (var m in matrices) _scratch.Add(offset * m);
            Graphics.RenderMeshInstanced(new RenderParams(mat), _cube, 0, _scratch);
        }

        /// <summary>Offset from <paramref name="from"/> to <paramref name="to"/>, the short way around the wrapping world.</summary>
        private static Vector3 WrappedDelta(Vector3 from, Vector3 to, float size)
        {
            float dx = Mathf.Repeat(to.x - from.x + size * 0.5f, size) - size * 0.5f;
            float dz = Mathf.Repeat(to.z - from.z + size * 0.5f, size) - size * 0.5f;
            return new Vector3(dx, 0f, dz);
        }
    }
}
