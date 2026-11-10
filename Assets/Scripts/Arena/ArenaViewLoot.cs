using System.Collections.Generic;
using System.Text;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Combat;
using FriendlyPersonShooter.Core.Expedition;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The loot half of the arena view: loot boxes (glowing until you've opened them), guns and mods
    /// on the ground (glowing in their tier's colour, with a light pillar to find them), launcher
    /// shells and bursts, the E prompt, and the gun panel's text.
    /// </summary>
    public partial class ArenaView
    {
        private const float InteractRange = 2.5f;
        private const float BoxDrawDistance = 160f;
        private const float BlastSeconds = 0.3f;

        private static readonly Color CommonColor = new Color(0.9f, 0.9f, 0.9f);
        private static readonly Color RareColor = new Color(0.37f, 0.66f, 1f);
        private static readonly Color EpicColor = new Color(0.78f, 0.49f, 1f);

        private Material _plainMat, _boxMat, _boxOpenMat, _boxGlowMat;
        private Material[] _tierMats;
        private EntityQuery _itemQuery;
        private IReadOnlyList<LootBox> _boxes;
        private LayerMap _boxesMap;
        private float[] _boxY;
        private readonly List<Matrix4x4> _lootA = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _lootB = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _lootC = new List<Matrix4x4>();
        private readonly List<Matrix4x4>[] _tierLists = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        private readonly List<(Vector3 From, Vector3 To, PayloadKind Payload, float Start, bool Burst)> _shells =
            new List<(Vector3, Vector3, PayloadKind, float, bool)>();
        private readonly Dictionary<PayloadKind, List<Matrix4x4>> _blastLists = new Dictionary<PayloadKind, List<Matrix4x4>>();

        /// <summary>The gun in hand's zoom, its stats, and what E would do (for the HUD).</summary>
        private float _localZoom = 1f;
        private GunStats _localStats;
        private ulong _localKey = ulong.MaxValue;
        private int _localActive = -1;
        private string _prompt;

        private void CreateLootMaterials()
        {
            _plainMat = MakeMat(new Color(0.85f, 0.85f, 0.8f));
            _boxMat = MakeMat(new Color(0.36f, 0.27f, 0.2f));
            _boxOpenMat = MakeMat(new Color(0.18f, 0.16f, 0.15f));
            _boxGlowMat = MakeGlow(new Color(1f, 0.78f, 0.35f), 1.6f);
            _tierMats = new[] { MakeGlow(CommonColor, 1.1f), MakeGlow(RareColor, 1.5f), MakeGlow(EpicColor, 1.8f) };
        }

        // ---------- The gun in hand ----------

        private static Gun InHand(ArenaLoadout lo) => Gun.Unpack(lo.Active == 0 ? lo.GunA : lo.GunB);

        /// <summary>Stats of the gun in hand, rebuilt when it changes.</summary>
        private GunStats LocalGunStats(ArenaLoadout lo, float now)
        {
            ulong key = lo.Active == 0 ? lo.GunA : lo.GunB;
            if (key == _localKey && lo.Active == _localActive) return _localStats;
            _localKey = key;
            _localActive = lo.Active;
            _localStats = GunStats.For(Gun.Unpack(key));
            return _localStats;
        }

        private string GunTitle(ArenaLoadout lo)
        {
            var gun = InHand(lo);
            if (gun.IsEmpty) return "NO GUN";
            string pay = PayloadTag(gun.Payload);
            if (gun.SecondPayload != PayloadKind.None) pay += " + " + PayloadTag(gun.SecondPayload);
            int mag = _localStats != null ? _localStats.Magazine : 0;
            string ammo = lo.Capsule == (byte)CapsuleState.Flying
                ? "<color=#FFD84D>CAPSULE OUT: SHOOT IT!</color>"
                : lo.Capsule == (byte)CapsuleState.Regrowing
                ? $"<color=#9A9A9A>regrowing {lo.Grown * 100f:0}%</color>"
                : lo.Charge == 0 ? $"<color=#FF6B6B>0 / {mag}: next shot flings it</color>" : $"{lo.Charge} / {mag}";
            return $"{GunFrames.Name(gun.Frame).ToUpperInvariant()}  {pay}     <size=22>{ammo}</size>";
        }

        private static string GunInfo(ArenaLoadout lo)
        {
            var gun = InHand(lo);
            var sb = new StringBuilder();
            sb.Append(ModList(gun)).Append('\n');
            sb.Append(ArenaHud.PayloadShort(gun.Payload)).Append('\n');
            sb.Append("Right mouse zoom     Out of payload? Shoot your capsule out of the air");
            return sb.ToString();
        }

        private static string ModList(Gun gun)
        {
            var parts = new List<string>();
            for (int i = 0; i < Gun.ModSlots; i++)
            {
                var m = gun.GetMod(i);
                if (!m.IsEmpty) parts.Add(ModLabel(m));
            }
            return parts.Count == 0 ? "<color=#9A9A9A>no mods</color>" : string.Join("  ·  ", parts);
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        private static Color TierColor(ModTier t) => t == ModTier.Epic ? EpicColor : t == ModTier.Rare ? RareColor : CommonColor;

        private static string PayloadTag(PayloadKind p) =>
            p == PayloadKind.None ? "<color=#9A9A9A>no payload</color>" : $"<color=#{Hex(ArenaHud.PayloadColor(p))}>{p.ToString().ToUpperInvariant()}</color>";

        private static string ModLabel(Mod m) => $"<color=#{Hex(TierColor(m.Tier))}>{m.Tier} {GunMods.Name(m.Kind)}</color>";

        private static string ItemLabel(LootItem item)
        {
            if (!item.IsGun) return ModLabel(item.Mod) + (item.Mod.IsPayload ? " payload" : "") + " mod";
            var g = item.Gun;
            string payload = g.Payload0.IsEmpty ? "" : " with " + ModLabel(g.Payload0);
            return $"<color=#{Hex(TierColor(item.Tier))}>{GunFrames.Name(g.Frame)}</color>{payload}";
        }

        private static string GunDetail(Gun g)
        {
            var s = GunStats.For(g);
            if (s == null) return "";
            float perSecond = s.Mode == FireMode.Burst ? s.BurstCount / s.Interval : 1f / s.Interval;
            return $"{s.Damage:0} damage  ·  {perSecond:0.#} shots/s  ·  {s.Magazine} rounds  ·  range {s.Range:0} m     {ModList(g)}";
        }

        // ---------- Launcher shells ----------

        private void AddShell(Vector3 from, Vector3 to, PayloadKind payload, float start) => _shells.Add((from, to, payload, start, false));

        private void DrawShells(float now)
        {
            var frame = GunFrames.For(FrameKind.Launcher);
            float speed = frame.ProjectileSpeed;
            float radius = frame.SplashRadius;
            foreach (var l in _blastLists.Values) l.Clear();
            for (int i = _shells.Count - 1; i >= 0; i--)
            {
                var s = _shells[i];
                float length = Vector3.Distance(s.From, s.To);
                float arrive = s.Start + length / speed;
                if (now < arrive)
                {
                    float t = (now - s.Start) * speed / Mathf.Max(0.01f, length);
                    var dir = s.To - s.From;
                    if (!_blastLists.TryGetValue(s.Payload, out var shellList)) _blastLists[s.Payload] = shellList = new List<Matrix4x4>();
                    shellList.Add(Matrix4x4.TRS(Vector3.Lerp(s.From, s.To, t), Quaternion.LookRotation(dir), new Vector3(0.3f, 0.3f, 0.7f)));
                    continue;
                }
                float age = now - arrive;
                if (age > BlastSeconds) { _shells.RemoveAt(i); continue; }
                if (!s.Burst)
                {
                    ArenaAudio.Blast(s.To);
                    s.Burst = true;
                    _shells[i] = s;
                }
                // The burst: a quick cube flash the size of the splash, shrinking away.
                float k = age / BlastSeconds;
                float size = radius * 2f * Mathf.Lerp(0.6f, 1f, Mathf.Sqrt(k)) * (1f - k * 0.7f);
                if (!_blastLists.TryGetValue(s.Payload, out var list)) _blastLists[s.Payload] = list = new List<Matrix4x4>();
                list.Add(Matrix4x4.TRS(s.To, Quaternion.Euler(45f * k, 90f * k, 30f), Vector3.one * size * 0.6f));
            }
            foreach (var kv in _blastLists) DrawList(kv.Value, PayloadMat(kv.Key));
        }

        // ---------- Boxes and items ----------

        private void DrawLoot(World world, Entity local)
        {
            _prompt = null;
            var em = world.EntityManager;
            int myId = -1;
            Vector3 feet = Vector3.zero;
            ArenaLoadout lo = default;
            bool alive = false;
            if (local != Entity.Null)
            {
                myId = em.GetComponentData<GhostOwner>(local).NetworkId;
                feet = em.GetComponentData<LocalTransform>(local).Position;
                lo = em.GetComponentData<ArenaLoadout>(local);
                alive = em.GetComponentData<ArenaVitals>(local).Health > 0f;
            }
            Vector3 cam = viewCamera != null ? viewCamera.transform.position : feet;
            float now = Time.time;

            // Loot boxes: the same spots the server uses, from the layer seed.
            int nearBox = -1;
            if (_wrap)
            {
                var map = ExpeditionWorld.Map;
                if (_boxesMap != map)
                {
                    _boxesMap = map;
                    _boxes = LootBoxSites.For(map, ExpeditionWorld.Layer);
                    _boxY = new float[_boxes.Count];
                    for (int i = 0; i < _boxY.Length; i++) _boxY[i] = float.NaN;
                }
                _lootA.Clear(); _lootB.Clear(); _lootC.Clear();
                float best = InteractRange * InteractRange;
                for (int i = 0; i < _boxes.Count; i++)
                {
                    var b = _boxes[i];
                    if (float.IsNaN(_boxY[i]))
                    {
                        // The ground there may not be built yet; try again next frame.
                        float y = ExpeditionTerrain.Active ? ExpeditionTerrain.GroundY(b.World.X, b.World.Y, float.NaN) : 0f;
                        if (float.IsNaN(y)) continue;
                        _boxY[i] = y;
                    }
                    Vector3 p = Near(new float3(b.World.X, _boxY[i], b.World.Y));
                    if ((p - cam).sqrMagnitude > BoxDrawDistance * BoxDrawDistance) continue;
                    bool opened = ArenaFx.OpenedBoxes.Contains(b.Id);
                    var rot = Quaternion.Euler(0f, b.Id * 47f % 360f, 0f);
                    (opened ? _lootB : _lootA).Add(Matrix4x4.TRS(p + Vector3.up * 0.35f, rot, new Vector3(1.1f, 0.7f, 0.75f)));
                    (opened ? _lootB : _lootC).Add(Matrix4x4.TRS(p + Vector3.up * 0.74f, rot, new Vector3(1.16f, 0.1f, 0.81f)));
                    if (opened) continue;
                    // A faint gold column so boxes can be spotted from a distance.
                    _lootC.Add(Matrix4x4.TRS(p + Vector3.up * 3f, Quaternion.identity, new Vector3(0.05f, 4.5f, 0.05f)));
                    var flat = new Vector2(p.x - feet.x, p.z - feet.z);
                    if (local != Entity.Null && flat.sqrMagnitude < best && Mathf.Abs(p.y - feet.y) < 2.5f)
                    {
                        best = flat.sqrMagnitude;
                        nearBox = b.Id;
                    }
                }
                DrawList(_lootA, _boxMat);
                DrawList(_lootB, _boxOpenMat);
                DrawList(_lootC, _boxGlowMat);
            }

            // Items on the ground: yours and anyone's (others' personal rolls aren't drawn).
            foreach (var l in _tierLists) l.Clear();
            bool hasItem = false;
            LootItem nearItem = default;
            int nearOwner = 0;
            float bestItem = InteractRange * InteractRange;
            using (var items = _itemQuery.ToComponentDataArray<ArenaGroundItem>(Allocator.Temp))
            using (var transforms = _itemQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].Owner != 0 && items[i].Owner != myId) continue;
                    var item = LootItem.Unpack(items[i].Item);
                    if (item.IsNone) continue;
                    Vector3 p = Near(transforms[i].Position);
                    var list = _tierLists[(int)item.Tier];
                    float bob = Mathf.Sin(now * 2.2f + i) * 0.08f;
                    float spin = now * 70f + i * 40f;
                    if (item.IsGun) list.Add(Matrix4x4.TRS(p + Vector3.up * bob, Quaternion.Euler(0f, spin, 0f), new Vector3(0.75f, 0.18f, 0.2f)));
                    else list.Add(Matrix4x4.TRS(p + Vector3.up * bob, Quaternion.Euler(45f, spin, 45f), Vector3.one * 0.28f));
                    list.Add(Matrix4x4.TRS(p + Vector3.up * 1.3f, Quaternion.identity, new Vector3(0.04f, 2.2f, 0.04f)));
                    float d = (p - (feet + Vector3.up * 0.8f)).sqrMagnitude;
                    if (local != Entity.Null && d < bestItem)
                    {
                        bestItem = d;
                        hasItem = true;
                        nearItem = item;
                        nearOwner = items[i].Owner;
                    }
                }
            }
            for (int t = 0; t < _tierLists.Length; t++) DrawList(_tierLists[t], _tierMats[t]);

            if (!alive) return;
            if (hasItem) _prompt = ItemPrompt(lo, nearItem, nearOwner);
        }

        /// <summary>A loot box pick card: the item, what it does, and what taking it would swap out.</summary>
        public static string BoxCard(ArenaLoadout lo, LootItem item)
        {
            var preview = Loadout.Unpack(lo.GunA, lo.GunB, lo.Active);
            var r = preview.Equip(item);
            var sb = new StringBuilder();
            sb.Append("<b><size=30>").Append(ItemLabel(item)).Append("</size></b>\n");
            if (item.IsGun) sb.Append("<color=#B8B0D8>gun</color>\n");
            sb.Append("\n<size=20>").Append(item.IsGun ? GunDetail(item.Gun).Replace("     ", "\n") : GunMods.Describe(item.Mod)).Append("</size>\n\n<size=19>");
            if (!r.Equipped) sb.Append($"<color=#FF8C8C>{r.Refusal}</color>");
            else if (!r.Dropped.IsNone)
            {
                sb.Append("<color=#B8B0D8>swaps out your</color> ").Append(ItemLabel(r.Dropped));
                if (!r.Dropped2.IsNone) sb.Append(" <color=#B8B0D8>and</color> ").Append(ItemLabel(r.Dropped2));
            }
            else sb.Append(item.IsGun ? "<color=#B8B0D8>goes in your empty gun slot</color>" : "<color=#B8B0D8>goes in an empty mod slot</color>");
            return sb.Append("</size>").ToString();
        }

        /// <summary>The colour of an item's tier (common, rare, epic).</summary>
        public static Color ItemTierColor(LootItem item) => TierColor(item.Tier);

        /// <summary>What taking this item would do, before you take it: what it is and what it would swap out.</summary>
        private static string ItemPrompt(ArenaLoadout lo, LootItem item, int owner)
        {
            var preview = Loadout.Unpack(lo.GunA, lo.GunB, lo.Active);
            var r = preview.Equip(item);
            var sb = new StringBuilder();
            sb.Append("<color=#FFD84D>Shoot it</color> to take ").Append(ItemLabel(item));
            if (owner != 0) sb.Append("  <size=18><color=#B8B0D8>(yours)</color></size>");
            sb.Append("\n<size=18>");
            sb.Append(item.IsGun ? GunDetail(item.Gun) : GunMods.Describe(item.Mod));
            if (!r.Equipped) sb.Append($"\n<color=#FF8C8C>{r.Refusal}</color>");
            else if (!r.Dropped.IsNone)
            {
                sb.Append("\nswaps out your ").Append(ItemLabel(r.Dropped));
                if (!r.Dropped2.IsNone) sb.Append(" and ").Append(ItemLabel(r.Dropped2));
                sb.Append(" (left on the ground)");
            }
            else if (item.IsGun) sb.Append("\ngoes in your empty gun slot");
            sb.Append("</size>");
            return sb.ToString();
        }
    }
}
