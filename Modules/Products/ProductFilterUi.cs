using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Lithium.Modules.Products
{
    // Stateless helpers for assembling the Products-app filter bar from code (no AssetBundle): the search
    // field is cloned from a vanilla phone InputField, while labels/buttons are built directly and styled
    // with the game's UI font.
    internal static class ProductFilterUi
    {
        private static Font _font;

        // --- Managed -> Il2Cpp UnityAction conversions ----------------------------------------------
        public static UnityAction UA(Action a) => (UnityAction)a;
        public static UnityAction<string> UA(Action<string> a) => (UnityAction<string>)a;

        public static Font ResolveFont()
        {
            if (_font != null)
                return _font;

            Il2CppArrayBase<Text> texts = UnityEngine.Object.FindObjectsOfType<Text>(true);
            if (texts != null)
            {
                foreach (Text t in texts)
                {
                    if (t != null && t.font != null)
                    {
                        _font = t.font;
                        break;
                    }
                }
            }

            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            return _font;
        }

        // Anchors a RectTransform with explicit pixel offsets from a single anchor corner.
        public static void SetAnchored(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 pos)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        public static GameObject MakeImage(Transform parent, string name, Color color)
        {
            GameObject go = new(name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.transform.localScale = Vector3.one;
            go.AddComponent<Image>().color = color;
            return go;
        }

        public static Text MakeText(Transform parent, string name, string text, int fontSize, Color color, TextAnchor anchor)
        {
            GameObject go = new(name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.transform.localScale = Vector3.one;

            Text t = go.AddComponent<Text>();
            t.font = ResolveFont();
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = text;
            return t;
        }

        // A simple button: background Image (target graphic) + centered child label. Returns both so the
        // caller can recolor/relabel later.
        public static Button MakeButton(Transform parent, string name, string label, int fontSize,
            Color bgColor, Color textColor, Action onClick, out Image bg, out Text labelText)
        {
            GameObject go = new(name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.transform.localScale = Vector3.one;

            bg = go.AddComponent<Image>();
            bg.color = bgColor;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;

            labelText = MakeText(go.transform, "Label", label, fontSize, textColor, TextAnchor.MiddleCenter);
            RectTransform lrt = labelText.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            if (onClick != null)
                btn.onClick.AddListener(UA(onClick));
            return btn;
        }

        // Clones a vanilla InputField and turns it into a free-text search box.
        public static InputField CloneSearchInput(InputField template, string placeholder)
        {
            if (template == null)
                return null;

            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject);
            InputField input = clone.GetComponent<InputField>();
            input.contentType = InputField.ContentType.Standard;
            input.characterValidation = InputField.CharacterValidation.None;
            input.characterLimit = 0;
            input.lineType = InputField.LineType.SingleLine;
            input.SetTextWithoutNotify(string.Empty);

            if (input.placeholder != null)
            {
                Text ph = input.placeholder.TryCast<Text>();
                if (ph != null)
                {
                    ph.text = placeholder;
                    ph.fontStyle = FontStyle.Italic;
                }
            }
            return input;
        }

        // A vertical, scrollable list. Returns the content RectTransform; rows are positioned manually by
        // the caller (anchored top-left), and the caller sets content height so the ScrollRect can clamp.
        public static RectTransform MakeScrollList(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 size,
            Vector2 pos, Color background)
        {
            GameObject scroll = new("ScrollList");
            RectTransform srt = scroll.AddComponent<RectTransform>();
            srt.SetParent(parent, false);
            scroll.transform.localScale = Vector3.one;
            SetAnchored(srt, anchor, pivot, size, pos);
            scroll.AddComponent<Image>().color = background;

            GameObject viewport = new("Viewport");
            RectTransform vrt = viewport.AddComponent<RectTransform>();
            vrt.SetParent(scroll.transform, false);
            viewport.transform.localScale = Vector3.one;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>().color = Color.white;
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            GameObject content = new("Content");
            RectTransform crt = content.AddComponent<RectTransform>();
            crt.SetParent(viewport.transform, false);
            content.transform.localScale = Vector3.one;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = Vector2.zero;

            ScrollRect sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24f;
            sr.viewport = vrt;
            sr.content = crt;
            return crt;
        }
    }
}
