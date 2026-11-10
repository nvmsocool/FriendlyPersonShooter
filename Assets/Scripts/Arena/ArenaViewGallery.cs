using System.Collections.Generic;
using FriendlyPersonShooter.Core.Arsenal;
using FriendlyPersonShooter.Core.Expedition;
using FriendlyPersonShooter.Core.Gallery;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>
    /// The shooting gallery half of the view. While the player's gallery is open the world turns grey and dark and
    /// the targets are drawn in colour on top, at their angles around where the player was looking (worked out from
    /// the gallery's seed, like the host does). Also: the loot preview (aim at a box or reward chest to see its three
    /// items and their target colours) and the player's reward chest.
    /// </summary>
    public partial class ArenaView
    {
        private const float PreviewRange = 40f;
        private const float PreviewHold = 0.3f;
        private const float GreyFade = 0.25f;
        private const float ResultSeconds = 1.3f;
        private const int MaxTargets = 12;

        /// <summary>Item card target colours: each of a box's three items gets one, shown in the preview and the gallery.</summary>
        public static readonly Color[] CardColors =
        {
            new Color(0.25f, 0.9f, 1f),
            new Color(1f, 0.35f, 0.85f),
            new Color(1f, 0.8f, 0.2f),
        };
        private static readonly Color TargetColor = new Color(1f, 0.45f, 0.25f);

        private RectTransform _galleryRoot;
        private CanvasGroup _galleryGroup;
        private Text _galleryTitle, _galleryHint, _galleryResult;
        private Image _galleryBarBack, _galleryBar;
        private readonly Image[] _targetRings = new Image[MaxTargets];
        private readonly Image[] _targetDots = new Image[MaxTargets];
        private readonly Text[] _targetLabels = new Text[MaxTargets];
        private GameObject _previewBox;
        private Text _previewTitle;
        private readonly Image[] _previewSwatches = new Image[3];
        private readonly Text[] _previewLines = new Text[3];
        private Volume _greyVolume;
        private float _grey;

        private EntityQuery _netTime;
        private World _netTimeWorld;
        private GalleryRound _round;
        private byte _roundSerial;
        private bool _roundKnown;
        private byte _resultSerial;
        private float _resultUntil;
        private string _resultText;
        private int _lastHitCount;
        private float _hitFlashUntil;

        private int _previewKey = int.MinValue;
        private float _previewSince;
        private readonly List<Matrix4x4> _chest = new List<Matrix4x4>(), _chestGlow = new List<Matrix4x4>();

        /// <summary>The local player's gallery is open (their shots go at its targets, not the world).</summary>
        public static bool GalleryOpen { get; private set; }

        private static Sprite _ringSprite, _dotSprite;

        private void BuildGalleryUi()
        {
            var canvas = SessionUi.Canvas(transform, "Gallery", 20).transform;
            _galleryRoot = (RectTransform)canvas;
            _galleryGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            _galleryGroup.blocksRaycasts = false;
            _galleryGroup.interactable = false;
            var c = new Vector2(0.5f, 0.5f);
            var top = new Vector2(0.5f, 1f);
            _ringSprite = _ringSprite != null ? _ringSprite : CircleSprite(true);
            _dotSprite = _dotSprite != null ? _dotSprite : CircleSprite(false);
            for (int i = 0; i < MaxTargets; i++)
            {
                _targetRings[i] = SessionUi.Box(canvas, "Target", c, Vector2.zero, new Vector2(60, 60), Color.white);
                _targetRings[i].sprite = _ringSprite;
                _targetRings[i].raycastTarget = false;
                _targetDots[i] = SessionUi.Box(_targetRings[i].transform, "Dot", c, Vector2.zero, new Vector2(20, 20), Color.white);
                _targetDots[i].sprite = _dotSprite;
                _targetDots[i].raycastTarget = false;
                _targetLabels[i] = SessionUi.Label(_targetRings[i].transform, "", 20, new Vector2(0.5f, 0f), new Vector2(0, -12), new Vector2(300, 60),
                    TextAnchor.UpperCenter, Color.white, FontStyle.Bold);
                _targetLabels[i].rectTransform.pivot = new Vector2(0.5f, 1f);
                _targetLabels[i].supportRichText = true;
                _targetLabels[i].gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -2);
                _targetRings[i].gameObject.SetActive(false);
            }
            _galleryTitle = SessionUi.Label(canvas, "", 40, top, new Vector2(0, -150), new Vector2(1400, 56), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _galleryTitle.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);
            _galleryHint = SessionUi.Label(canvas, "", 22, top, new Vector2(0, -196), new Vector2(1400, 34), TextAnchor.MiddleCenter, SessionUi.Muted);
            _galleryHint.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -2);
            _galleryBarBack = SessionUi.Box(canvas, "TimeBack", top, new Vector2(0, -230), new Vector2(604, 16), new Color(0f, 0f, 0f, 0.6f));
            _galleryBar = SessionUi.Box(_galleryBarBack.transform, "Time", new Vector2(0f, 0.5f), new Vector2(2, 0), new Vector2(600, 12), Color.white);
            _galleryResult = SessionUi.Label(canvas, "", 56, c, new Vector2(0, 140), new Vector2(1400, 80), TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            _galleryResult.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);

            // The loot preview: beside the crosshair while you aim at a box or chest.
            var preview = SessionUi.Box(canvas, "Preview", c, new Vector2(330, -10), new Vector2(520, 196), new Color(0.05f, 0.04f, 0.12f, 0.8f));
            _previewBox = preview.gameObject;
            _previewTitle = SessionUi.Label(preview.transform, "", 20, new Vector2(0f, 1f), new Vector2(16, -8), new Vector2(490, 28), TextAnchor.UpperLeft, SessionUi.Muted);
            _previewTitle.rectTransform.pivot = new Vector2(0f, 1f);
            for (int i = 0; i < 3; i++)
            {
                _previewSwatches[i] = SessionUi.Box(preview.transform, "Swatch", new Vector2(0f, 1f), new Vector2(26, -56 - i * 50), new Vector2(22, 22), Color.white);
                _previewSwatches[i].sprite = _dotSprite;
                _previewLines[i] = SessionUi.Label(preview.transform, "", 19, new Vector2(0f, 1f), new Vector2(48, -40 - i * 50), new Vector2(460, 48), TextAnchor.UpperLeft);
                _previewLines[i].rectTransform.pivot = new Vector2(0f, 1f);
                _previewLines[i].supportRichText = true;
            }
            _previewBox.SetActive(false);

            // The grey-out: a global post-processing volume above the zones' own, faded in and out.
            var go = new GameObject("GalleryGrey");
            go.transform.SetParent(transform, false);
            _greyVolume = go.AddComponent<Volume>();
            _greyVolume.isGlobal = true;
            _greyVolume.priority = 1000f;
            _greyVolume.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(-100f);
            ca.postExposure.Override(-0.7f);
            ca.contrast.Override(-10f);
            _greyVolume.sharedProfile = profile;
        }

        private static Sprite CircleSprite(bool ring)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float r = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - size * 0.5f) * (x + 0.5f - size * 0.5f) + (y + 0.5f - size * 0.5f) * (y + 0.5f - size * 0.5f));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    if (ring) a *= Mathf.Clamp01(d - (r - 9f) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>The client's interpolation tick (what it sees), with its fraction.</summary>
        private bool TryViewTick(World world, out float tick)
        {
            tick = 0f;
            if (_netTimeWorld != world)
            {
                _netTimeWorld = world;
                _netTime = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkTime>());
            }
            if (_netTime.CalculateEntityCount() != 1) return false;
            var nt = _netTime.GetSingleton<NetworkTime>();
            if (!nt.InterpolationTick.IsValid) return false;
            tick = nt.InterpolationTick.TickIndexForValidTick + nt.InterpolationTickFraction - 1f;
            return true;
        }

        /// <summary>The gallery screen, the loot preview and the reward chest. Runs after the camera is placed.</summary>
        private void DrawGallery(World world, Entity local)
        {
            if (_galleryRoot == null) BuildGalleryUi();
            var em = world.EntityManager;
            bool has = local != Entity.Null && em.HasComponent<ArenaGallery>(local);
            var g = has ? em.GetComponentData<ArenaGallery>(local) : default;
            float now = Time.unscaledTime;
            bool open = false;

            if (has && g.Kind != 0 && TryViewTick(world, out float viewTick))
            {
                if (!_roundKnown || g.Serial != _roundSerial)
                {
                    _roundKnown = true;
                    _roundSerial = g.Serial;
                    _round = new GalleryRound(GalleryDifficulty.For((GalleryKind)g.Kind, g.Layer, g.Targets), g.Seed);
                    _lastHitCount = 0;
                }
                float t = math.max(0f, GalleryClock.Seconds(1) * (viewTick - g.StartTick));
                _round.Restore(g.Hits, g.HitCount, g.Next, GalleryClock.Seconds(g.NextTick - g.StartTick), (GalleryOutcome)g.Outcome, g.Chosen,
                    GalleryClock.Seconds(g.EndTick - g.StartTick));
                open = g.Outcome == (byte)GalleryOutcome.Running;
                if (g.HitCount > _lastHitCount)
                {
                    _hitFlashUntil = now + 0.15f;
                    ArenaAudio.Ping();
                }
                _lastHitCount = g.HitCount;
                if (!open && _resultSerial != g.Serial)
                {
                    _resultSerial = g.Serial;
                    // Only a result we watched (not an old gallery's, seen on joining).
                    if (_galleryWasOpen) ShowResult(g);
                }
                if (open) DrawTargets(g, t, now);
            }
            // Nothing else to do while a gallery is up.
            if (open) _prompt = null;
            if (!open) HideTargets();
            _galleryWasOpen = open;
            GalleryOpen = open;

            _grey = Mathf.MoveTowards(_grey, open ? 1f : 0f, Time.unscaledDeltaTime / GreyFade);
            _greyVolume.weight = _grey;

            _galleryResult.gameObject.SetActive(now < _resultUntil);
            if (now < _resultUntil)
            {
                _galleryResult.text = _resultText;
                var col = _galleryResult.color;
                col.a = Mathf.Clamp01((_resultUntil - now) / 0.4f);
                _galleryResult.color = col;
            }

            DrawChest(has ? g : default, local);
            UpdatePreview(world, local, has ? g : default, open, now);
        }

        private bool _galleryWasOpen;

        private void ShowResult(ArenaGallery g)
        {
            bool won = g.Outcome == (byte)GalleryOutcome.Won;
            var kind = (GalleryKind)g.Kind;
            _resultUntil = Time.unscaledTime + ResultSeconds;
            if (won)
            {
                var item = ChoiceOf(g, g.Chosen);
                _resultText = item.IsNone ? "<color=#7CFF8A>DONE</color>" : $"<color=#7CFF8A>GOT IT:</color> {ItemLabel(item)}";
                ArenaAudio.LevelUp();
            }
            else
            {
                _resultText = kind == GalleryKind.Box || kind == GalleryKind.Reward
                    ? "<color=#B0B0B0>MISSED: the items are gone</color>"
                    : "<color=#B0B0B0>MISSED</color>";
            }
            _galleryResult.color = Color.white;
        }

        private static LootItem ChoiceOf(ArenaGallery g, int i) =>
            i == 0 ? LootItem.Unpack(g.Choice0) : i == 1 ? LootItem.Unpack(g.Choice1) : i == 2 ? LootItem.Unpack(g.Choice2) : default;

        private void DrawTargets(ArenaGallery g, float t, float now)
        {
            var kind = (GalleryKind)g.Kind;
            var spec = _round.Spec;
            var cam = viewCamera;
            float rootH = _galleryRoot.rect.height;
            float pxPerDeg = rootH / Mathf.Max(1f, cam.fieldOfView);
            int[] colors = (kind == GalleryKind.Box || kind == GalleryKind.Reward) ? GalleryRound.ColorOrder(g.ColorSeed) : null;
            Vector3 eye = cam.transform.position;
            bool flash = now < _hitFlashUntil;

            for (int i = 0; i < MaxTargets; i++)
            {
                var ring = _targetRings[i];
                if (i >= _round.Count || !_round.Visible(i, t))
                {
                    ring.gameObject.SetActive(false);
                    continue;
                }
                var o = _round.Offset(i, t);
                var dir = (Vector3)ArenaSettings.Forward(g.Yaw0 + o.X, g.Pitch0 + o.Y);
                var sp = cam.WorldToScreenPoint(eye + dir * 10f);
                if (sp.z <= 0f)
                {
                    ring.gameObject.SetActive(false);
                    continue;
                }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_galleryRoot, sp, null, out var lp);
                ring.gameObject.SetActive(true);
                ring.rectTransform.anchoredPosition = lp;
                float size = Mathf.Max(18f, spec.Radius * 2f * pxPerDeg);
                ring.rectTransform.sizeDelta = new Vector2(size, size);
                _targetDots[i].rectTransform.sizeDelta = new Vector2(size * 0.32f, size * 0.32f);

                Color color = TargetColor;
                string label = "";
                switch (kind)
                {
                    case GalleryKind.Starter:
                    {
                        var gun = ChoiceOf(g, i).Gun;
                        color = ArenaHud.PayloadColor(gun.Payload);
                        label = $"{GunFrames.Name(gun.Frame).ToUpperInvariant()}\n<size=17><color=#{Hex(color)}>{gun.Payload.ToString().ToUpperInvariant()}</color>  " +
                                $"{SessionHud.FrameBlurb(gun.Frame)}</size>";
                        break;
                    }
                    case GalleryKind.Box:
                    case GalleryKind.Reward:
                        color = CardColors[colors[i]];
                        label = $"<size=19>{ItemLabel(ChoiceOf(g, i))}</size>";
                        break;
                    case GalleryKind.Gate:
                        label = (i + 1).ToString();
                        if (i != _round.Next) color = Color.Lerp(color, Color.grey, 0.6f);
                        break;
                    case GalleryKind.Bank:
                        if (g.Subject >= 0 && g.Subject < EssenceColors.Count)
                            ColorUtility.TryParseHtmlString(EssencePack.Hex((EssenceColor)g.Subject), out color);
                        break;
                    case GalleryKind.Travel:
                        color = new Color(0.45f, 0.8f, 1f);
                        break;
                }
                if (flash) color = Color.Lerp(color, Color.white, 0.5f);
                ring.color = color;
                _targetDots[i].color = color;
                _targetLabels[i].text = label;
            }

            _galleryTitle.text = GalleryTitle(kind);
            _galleryHint.text = GalleryHint(kind, g);
            float left, full;
            if (spec.Rule == GalleryRule.Sequence)
            {
                left = _round.TargetTimeLeft(t);
                full = spec.PerTarget;
            }
            else
            {
                left = _round.TimeLeft(t);
                full = spec.Duration;
            }
            bool timed = full > 0f && !float.IsInfinity(left);
            _galleryBarBack.gameObject.SetActive(timed);
            if (timed)
            {
                float k = Mathf.Clamp01(left / full);
                _galleryBar.rectTransform.sizeDelta = new Vector2(600f * k, 12f);
                _galleryBar.color = Color.Lerp(new Color(1f, 0.3f, 0.25f), Color.white, k * 1.5f);
            }
            _galleryTitle.gameObject.SetActive(true);
            _galleryHint.gameObject.SetActive(true);
        }

        private void HideTargets()
        {
            for (int i = 0; i < MaxTargets; i++) _targetRings[i].gameObject.SetActive(false);
            _galleryTitle.gameObject.SetActive(false);
            _galleryHint.gameObject.SetActive(false);
            _galleryBarBack.gameObject.SetActive(false);
        }

        private static string GalleryTitle(GalleryKind kind)
        {
            switch (kind)
            {
                case GalleryKind.Starter: return "SHOOT YOUR STARTING GUN";
                case GalleryKind.Box: return "LOOT BOX: SHOOT ONE";
                case GalleryKind.Reward: return "BOSS REWARD: SHOOT ONE";
                case GalleryKind.Cache: return "CACHE: HIT THEM ALL";
                case GalleryKind.Bank: return "BANK: HIT EACH ONE";
                case GalleryKind.Gate: return "UNLOCK THE GATE: IN ORDER";
                case GalleryKind.Travel: return "TRAVEL: HIT IT";
                case GalleryKind.RaidStart: return "START THE RAID: HIT IT";
                default: return "";
            }
        }

        private static string GalleryHint(GalleryKind kind, ArenaGallery g)
        {
            switch (kind)
            {
                case GalleryKind.Starter: return "Each comes with a payload: shoot allies to buff them, enemies to weaken them. No hurry.";
                case GalleryKind.Box:
                case GalleryKind.Reward: return "One shot. Miss, or run out of time, and the items are gone.";
                case GalleryKind.Cache: return "Hit every target before time runs out.";
                case GalleryKind.Bank:
                {
                    string color = g.Subject >= 0 && g.Subject < EssenceColors.Count ? EssencePack.Named((EssenceColor)g.Subject) : "essence";
                    return $"Each target holds up to {GalleryDifficulty.BankChunk} {color}. Hit it to bank that; miss it and it's lost.";
                }
                case GalleryKind.Gate: return "Hit them in order: the bright one is next.";
                case GalleryKind.Travel: return g.Subject == 1 ? "Hit it to head up a layer." : "Hit it to head through the gate.";
                case GalleryKind.RaidStart: return "Hit it to start the countdown. Everyone gathers at the door.";
                default: return "";
            }
        }

        /// <summary>The player's reward chest, where it landed: shoot it to open its gallery.</summary>
        private void DrawChest(ArenaGallery g, Entity local)
        {
            _chest.Clear();
            _chestGlow.Clear();
            if (local != Entity.Null && g.ChestOut != 0)
            {
                Vector3 p = Near(g.ChestPos);
                var rot = Quaternion.Euler(0f, Time.time * 20f, 0f);
                _chest.Add(Matrix4x4.TRS(p + Vector3.up * 0.4f, rot, new Vector3(1.2f, 0.8f, 0.85f)));
                _chestGlow.Add(Matrix4x4.TRS(p + Vector3.up * 0.86f, rot, new Vector3(1.26f, 0.14f, 0.9f)));
                _chestGlow.Add(Matrix4x4.TRS(p + Vector3.up * 4f, Quaternion.identity, new Vector3(0.08f, 6f, 0.08f)));
            }
            DrawList(_chest, _boxMat);
            DrawList(_chestGlow, _tierMats[2]);
        }

        /// <summary>
        /// Aiming at an unopened box (or your reward chest) for a moment shows its three items, each with the colour its
        /// target will have in the gallery. The roll is the host's: the same seed, box and player.
        /// </summary>
        private void UpdatePreview(World world, Entity local, ArenaGallery g, bool open, float now)
        {
            int key = int.MinValue;
            LootItem[] items = null;
            uint colorSeed = 0;
            bool chest = false;
            if (!open && local != Entity.Null && viewCamera != null && g.LootSeed != 0)
            {
                var em = world.EntityManager;
                int myId = em.GetComponentData<GhostOwner>(local).NetworkId;
                Vector3 eye = viewCamera.transform.position;
                var dir = (float3)viewCamera.transform.forward;
                float reach = math.min(PreviewRange, ArenaLayout.RaycastCover(eye, dir, PreviewRange));
                float best = reach;
                if (_wrap && _boxes != null && _boxY != null)
                {
                    for (int i = 0; i < _boxes.Count; i++)
                    {
                        if (float.IsNaN(_boxY[i])) continue;
                        var b = _boxes[i];
                        if (ArenaFx.OpenedBoxes.Contains(b.Id)) continue;
                        Vector3 c = Near(new float3(b.World.X, _boxY[i], b.World.Y)) + Vector3.up * 0.4f;
                        if (!RayHits(eye, dir, c, 0.8f, best, out float at)) continue;
                        best = at;
                        key = b.Id;
                        colorSeed = LootBoxLedger.RollSeed(g.LootSeed, b.Id, myId);
                        items = LootRolls.BoxChoices(colorSeed);
                        chest = false;
                    }
                }
                if (g.ChestOut != 0)
                {
                    Vector3 c = Near(g.ChestPos) + Vector3.up * 0.5f;
                    if (RayHits(eye, dir, c, 0.9f, best, out float at))
                    {
                        key = -2 - (int)(g.ChestSeed & 0xFFFF);
                        colorSeed = g.ChestSeed;
                        items = new[] { LootItem.Unpack(g.Chest0), LootItem.Unpack(g.Chest1), LootItem.Unpack(g.Chest2) };
                        chest = true;
                    }
                }
            }
            if (key != _previewKey)
            {
                _previewKey = key;
                _previewSince = now;
            }
            bool show = items != null && now - _previewSince >= PreviewHold;
            _previewBox.SetActive(show);
            if (items != null) _prompt = chest ? "Shoot the chest: one shot at the item you want" : "Shoot the box: one shot at the item you want";
            if (!show) return;
            _previewTitle.text = chest ? "BOSS REWARD (yours)" : "LOOT BOX";
            var colors = GalleryRound.ColorOrder(colorSeed);
            for (int i = 0; i < 3; i++)
            {
                var item = i < items.Length ? items[i] : default;
                _previewSwatches[i].color = CardColors[colors[i]];
                _previewLines[i].text = item.IsNone ? "" : ItemLabel(item) + "\n<size=15><color=#B8B0D8>" + PreviewDetail(item) + "</color></size>";
            }
        }

        private static string PreviewDetail(LootItem item)
        {
            if (!item.IsGun) return GunMods.Describe(item.Mod);
            return SessionHud.FrameBlurb(item.Gun.Frame);
        }

        private static bool RayHits(Vector3 origin, float3 dir, Vector3 center, float radius, float maxRange, out float at)
        {
            at = 0f;
            var oc = (float3)(center - origin);
            float along = math.dot(oc, dir);
            if (along < 0f) return false;
            float miss = math.lengthsq(oc) - along * along;
            if (miss > radius * radius) return false;
            at = along - math.sqrt(radius * radius - miss);
            return at < maxRange;
        }
    }
}
