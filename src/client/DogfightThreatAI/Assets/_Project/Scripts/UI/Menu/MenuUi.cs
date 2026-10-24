using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Menu
{
    /// <summary>Shared UI construction in a 1920 × 1080 reference canvas.</summary>
    internal sealed class MenuUi
    {
        internal static readonly Color Ivory = new Color32(239, 237, 222, 255);
        internal static readonly Color Muted = new Color32(176, 181, 169, 255);
        internal static readonly Color Faint = new Color32(126, 143, 139, 255);
        internal readonly Font Body;
        internal readonly Font Display;

        internal MenuUi()
        {
            Body = Resources.Load<Font>("Menu/Fonts/MenuChinese");
            Display = Resources.Load<Font>("Menu/Fonts/BebasNeue");
            if (Body == null || Display == null)
                throw new InvalidOperationException("Menu fonts are missing from Resources/Menu/Fonts.");
        }

        internal static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = LayerMask.NameToLayer("UI");
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        internal static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        internal static Image Image(string name, Transform parent, Color color, float x, float y, float w, float h,
                                    bool raycast = false)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        internal Text Text(string name, Transform parent, string value, int size,
                           float x, float y, float w, float h, Color? color = null, bool display = false)
        {
            var text = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Text>();
            text.font = display ? Display : Body;
            text.fontSize = size;
            text.text = value;
            text.color = color ?? Ivory;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = display ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        internal Button Button(string name, Transform parent, string value, int size,
                               float x, float y, float w, float h, UnityAction click, Action focus = null)
        {
            var background = Image(name, parent, Color.clear, x, y, w, h, true);
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.normalColor = Color.clear;
            colors.highlightedColor = colors.selectedColor = new Color(1, 1, 1, .045f);
            colors.pressedColor = new Color(1, 1, 1, .13f);
            colors.disabledColor = Color.clear;
            colors.fadeDuration = .15f;
            button.colors = colors;
            // Keep the graphic white; the Selectable's tint supplies the alpha.
            background.color = Color.white;
            var mark = Image("Focus", background.transform, Color.clear, 0, h / 2 - 1, 18, 2);
            var label = Text("Label", background.transform, value, size, 34, 0, w - 38, h, Muted);
            background.gameObject.AddComponent<MenuItemFeedback>().Configure(label, mark, focus);
            button.onClick.AddListener(click);
            return button;
        }

        internal Slider Slider(Transform parent, float x, float y, float w, float value, UnityAction<float> change)
        {
            var rect = Rect("MasterVolume", parent, x, y, w, 54);
            var hitArea = rect.gameObject.AddComponent<Image>();
            hitArea.color = Color.clear;
            var track = Image("Track", rect, new Color(.8f, .85f, .8f, .22f), 0, 26, w, 2);
            var fillArea = Rect("FillArea", rect, 0, 26, w, 2);
            var fill = Image("Fill", fillArea, Ivory, 0, 0, w, 2);
            Fill(fill.rectTransform);
            var handleArea = Rect("HandleArea", rect, 5, 0, w - 10, 54);
            var handle = Image("Handle", handleArea, Ivory, 0, 18, 10, 18, true);
            // Slider stretches the handle's Y anchors across its parent. Keep its final height at 18.
            handle.rectTransform.pivot = new Vector2(.5f, .5f);
            handle.rectTransform.sizeDelta = new Vector2(10, -36);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = value;
            slider.onValueChanged.AddListener(change);
            var colors = slider.colors;
            colors.normalColor = Muted;
            colors.selectedColor = colors.highlightedColor = Color.white;
            slider.colors = colors;
            return slider;
        }

        internal static void Navigation(params Selectable[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit };
                navigation.selectOnUp = items[(i + items.Length - 1) % items.Length];
                navigation.selectOnDown = items[(i + 1) % items.Length];
                items[i].navigation = navigation;
            }
        }
    }
}
