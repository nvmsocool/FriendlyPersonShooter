using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FriendlyPersonShooter.Arena
{
    /// <summary>Builds the menu's and session overlay's uGUI from code (no prefabs), in one consistent style.</summary>
    internal static class SessionUi
    {
        public static readonly Color Panel = new Color(0.07f, 0.06f, 0.16f, 0.82f);
        public static readonly Color Primary = new Color(1f, 0.33f, 0.6f);
        public static readonly Color Secondary = new Color(0.2f, 0.75f, 0.85f);
        public static readonly Color Quiet = new Color(0.3f, 0.28f, 0.45f);
        public static readonly Color Error = new Color(1f, 0.55f, 0.45f);
        public static readonly Color Muted = new Color(0.78f, 0.76f, 0.9f);

        private static Font _font;
        public static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Canvas Canvas(Transform parent, string name, int order)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static void EventSystem(Transform parent)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(parent, false);
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Box(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var image = Rect(parent, name, anchor, pos, size).gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text Label(Transform parent, string text, int size, Vector2 anchor, Vector2 pos, Vector2 box,
            TextAnchor align = TextAnchor.MiddleCenter, Color? color = null, FontStyle style = FontStyle.Normal)
        {
            var t = Rect(parent, "Label", anchor, pos, box).gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = color ?? Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick, int fontSize = 30)
        {
            var image = Box(parent, text, anchor, pos, size, color);
            var button = image.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            colors.colorMultiplier = 1.2f;
            button.colors = colors;
            button.onClick.AddListener(onClick);
            var label = Label(image.transform, text, fontSize, new Vector2(0.5f, 0.5f), Vector2.zero, size, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.35f);
            return button;
        }

        public static InputField Input(Transform parent, string placeholder, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize = 34)
        {
            var image = Box(parent, "Input", anchor, pos, size, new Color(1f, 1f, 1f, 0.95f));
            var field = image.gameObject.AddComponent<InputField>();
            var text = Label(image.transform, "", fontSize, new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(30, 0), TextAnchor.MiddleCenter, new Color(0.1f, 0.08f, 0.2f), FontStyle.Bold);
            text.supportRichText = false;
            var hint = Label(image.transform, placeholder, fontSize - 6, new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(30, 0), TextAnchor.MiddleCenter, new Color(0.1f, 0.08f, 0.2f, 0.4f), FontStyle.Italic);
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = 12;
            field.characterValidation = InputField.CharacterValidation.Alphanumeric;
            field.caretColor = new Color(0.1f, 0.08f, 0.2f);
            field.caretWidth = 3;
            return field;
        }

        /// <summary>A full-screen vertical gradient behind everything.</summary>
        public static void Backdrop(Transform parent, Color top, Color bottom)
        {
            const int steps = 32;
            var tex = new Texture2D(1, steps, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[steps];
            for (int i = 0; i < steps; i++) pixels[i] = Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, i / (steps - 1f)));
            tex.SetPixels(pixels);
            tex.Apply();
            var raw = Rect(parent, "Backdrop", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            var rt = raw.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            raw.texture = tex;
            raw.raycastTarget = false;
        }
    }
}
