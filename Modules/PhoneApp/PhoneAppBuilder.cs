using System;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI.Phone;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Lithium.Modules.PhoneApp
{
    // Low-level, stateless helpers for assembling the phone app's UI from code. No AssetBundle is used:
    // text/dropdown elements are either created directly or cloned from existing vanilla phone widgets.
    internal static class PhoneAppBuilder
    {
        // --- Managed -> Il2Cpp UI delegate conversions (via the generated implicit operators) ---------
        public static UnityAction UA(Action a) => (UnityAction)a;
        public static UnityAction<int> UA(Action<int> a) => (UnityAction<int>)a;

        // --- Text -----------------------------------------------------------------------------------
        public static Text MakeText(Transform parent, Font font, string name, int fontSize, Color color,
            TextAnchor anchor, bool wrap)
        {
            GameObject go = new(name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            go.transform.localScale = Vector3.one;

            Text t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.supportRichText = true;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // --- Button ---------------------------------------------------------------------------------
        // A simple button: background Image (target graphic) + centered child label. Returns both so the
        // caller can restyle them later (e.g. tab selection states).
        public static Button MakeButton(Transform parent, Font font, string name, string label, int fontSize,
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

            labelText = MakeText(go.transform, font, "Label", fontSize, textColor, TextAnchor.MiddleCenter, false);
            RectTransform lrt = labelText.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            labelText.text = label;

            if (onClick != null)
                btn.onClick.AddListener(UA(onClick));
            return btn;
        }

        // --- App panel frame ------------------------------------------------------------------------
        // Creates the root app GameObject under the apps canvas, sized to match an existing app's rect so
        // it fills the phone screen correctly. Returns the container (inactive by default).
        public static GameObject MakePanel(string name)
        {
            AppsCanvas canvas = PlayerSingleton<AppsCanvas>.Instance;
            GameObject container = new(name);
            RectTransform rt = container.AddComponent<RectTransform>();
            rt.SetParent(canvas.canvas.transform, false);
            container.transform.localScale = Vector3.one;

            RectTransform template = FindAppRect(canvas);
            if (template != null)
            {
                rt.anchorMin = template.anchorMin;
                rt.anchorMax = template.anchorMax;
                rt.anchoredPosition = template.anchoredPosition;
                rt.sizeDelta = template.sizeDelta;
                rt.pivot = template.pivot;
                // The apps canvas carries an orientation rotation/scale; copy it or the panel renders sideways.
                rt.localRotation = template.localRotation;
                rt.localScale = template.localScale;
            }
            else
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            return container;
        }

        private static RectTransform FindAppRect(AppsCanvas canvas)
        {
            foreach (string appName in new[] { "DealerManagement", "Delivery", "ProductManager" })
            {
                Transform t = canvas.canvas.transform.Find(appName);
                if (t != null)
                    return t.GetComponent<RectTransform>();
            }
            return null;
        }

        public static void MakeBackground(GameObject parent, Color color)
        {
            GameObject go = new("Background");
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent.transform, false);
            go.transform.localScale = Vector3.one;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.AddComponent<Image>().color = color;
        }

        // --- Dropdown (cloned from a vanilla phone dropdown) ----------------------------------------
        public static Dropdown CloneDropdown()
        {
            GameObject clone = FindAndClone<Dropdown>("Delivery");
            if (clone == null)
                return null;

            Dropdown dd = clone.GetComponent<Dropdown>();
            dd.ClearOptions();

            // Bind the caption's avatar Image so the closed dropdown updates to the selected contact's
            // mugshot (otherwise it keeps the cloned dropdown's baked-in portrait).
            Image caption = FindCaptionImage(dd);
            if (caption != null)
            {
                dd.captionImage = caption;
                caption.preserveAspect = true;
            }

            if (dd.captionText != null)
            {
                dd.captionText.color = Color.white;
                dd.captionText.resizeTextForBestFit = true;
                dd.captionText.resizeTextMinSize = 16;
                dd.captionText.resizeTextMaxSize = 28;
            }
            if (dd.itemText != null)
            {
                dd.itemText.color = Color.white;
                dd.itemText.fontSize = 26;
            }
            return dd;
        }

        // The visible caption avatar = first Image that isn't the dropdown's own background, the popup
        // template, or the arrow.
        private static Image FindCaptionImage(Dropdown dd)
        {
            RectTransform template = dd.template;
            Il2CppArrayBase<Image> images = dd.GetComponentsInChildren<Image>(true);
            if (images == null)
                return null;
            foreach (Image img in images)
            {
                if (img == null || img.gameObject == dd.gameObject)
                    continue;
                if (template != null && img.transform.IsChildOf(template.transform))
                    continue;
                if (img.name == "Arrow")
                    continue;
                return img;
            }
            return null;
        }

        private static GameObject FindAndClone<T>(string preferredApp) where T : Component
        {
            AppsCanvas canvas = PlayerSingleton<AppsCanvas>.InstanceExists ? PlayerSingleton<AppsCanvas>.Instance : null;
            if (canvas == null)
                return null;

            Transform preferred = canvas.canvas.transform.Find(preferredApp);
            if (preferred != null)
            {
                T comp = preferred.GetComponentInChildren<T>(true);
                if (comp != null)
                    return UnityEngine.Object.Instantiate(comp.gameObject);
            }

            int count = canvas.canvas.transform.childCount;
            for (int i = 0; i < count; i++)
            {
                T comp = canvas.canvas.transform.GetChild(i).GetComponentInChildren<T>(true);
                if (comp != null)
                    return UnityEngine.Object.Instantiate(comp.gameObject);
            }
            return null;
        }

        // --- Home-screen icon -----------------------------------------------------------------------
        public static GameObject MakeIcon(string iconObjectName, string label, Sprite sprite, Action onClick)
        {
            HomeScreen hs = PlayerSingleton<HomeScreen>.Instance;
            GameObject icon = UnityEngine.Object.Instantiate(hs.appIconPrefab, hs.appIconContainer);
            icon.name = iconObjectName;

            Transform labelT = icon.transform.Find("Label");
            if (labelT != null)
            {
                Text labelText = labelT.GetComponent<Text>();
                if (labelText != null)
                    labelText.text = label;
            }

            Transform imageT = icon.transform.Find("Mask/Image");
            if (imageT != null)
            {
                Image img = imageT.GetComponent<Image>();
                if (img != null && sprite != null)
                    img.sprite = sprite;
            }

            Transform notif = icon.transform.Find("Notifications");
            if (notif != null)
                notif.gameObject.SetActive(false);

            Button btn = icon.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(UA(onClick));

            return icon;
        }

        // Loads the app icon from a PNG the user can drop at UserData/Lithium/PhoneAppIcon.png (no
        // AssetBundle needed); falls back to a procedural circle if the file is missing or invalid.
        public static Sprite LoadIconOrDefault(Color fallback)
        {
            try
            {
                string path = Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium", "PhoneAppIcon.png");
                if (File.Exists(path))
                {
                    byte[] data = File.ReadAllBytes(path);
                    Texture2D tex = new(2, 2, TextureFormat.RGBA32, false);
                    if (ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)data) && tex.width > 2)
                    {
                        tex.filterMode = FilterMode.Bilinear;
                        tex.wrapMode = TextureWrapMode.Clamp;
                        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f), 100f);
                    }
                }
            }
            catch
            {
                // fall through to the procedural icon
            }
            return CircleSprite(fallback);
        }

        // A simple filled-circle sprite used as the icon glyph (no embedded art needed).
        public static Sprite CircleSprite(Color fill)
        {
            const int size = 64;
            const float radius = 30f;
            Texture2D tex = new(size, size) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Vector2 center = new(size / 2f, size / 2f);

            Il2CppStructArray<Color> pixels = new(size * size);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = Vector2.Distance(new Vector2(x, y), center) <= radius ? fill : Color.clear;

            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
