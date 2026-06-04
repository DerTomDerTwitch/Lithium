using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.StationFramework;
using Il2CppScheduleOne.UI.Items;
using Il2CppTMPro;
using Lithium.Helper;
using UnityEngine;
using UnityEngine.UI;

namespace Lithium.Modules.ProductTooltips.Patches
{
    [HarmonyPatch(typeof(ItemInfoPanel), nameof(ItemInfoPanel.Open), new[] { typeof(ItemInstance), typeof(RectTransform) })]
    internal static class ProductInfoPanelMixesPatch
    {
        private const string RootName = "LithiumMixes";
        private const float TopMargin = 4f;
        private const float HeaderHeight = 16f;
        private const float LeftPad = 6f;

        private static readonly Dictionary<int, string> _lastProduct = new();

        [HarmonyPostfix]
        private static void Postfix(ItemInfoPanel __instance, ItemInstance item)
        {
            ModProductTooltipsConfiguration config = Core.Get<ModProductTooltips>().Configuration;
            if (!config.Enabled)
                return;

            try
            {
                ItemInfoContent content = __instance.content;
                if (content == null)
                    return;

                ProductItemInfoContent productContent = content.TryCast<ProductItemInfoContent>();
                if (productContent == null || productContent.DescriptionLabel == null)
                    return;

                int contentId = content.GetInstanceID();
                Transform existingRoot = content.transform.Find(RootName);

                ItemDefinition definition = item != null ? item.Definition : null;
                ProductDefinition product = definition != null ? definition.TryCast<ProductDefinition>() : null;

                List<MixRow> rows = product != null ? BuildForwardRecipes(product, config) : new List<MixRow>();
                string productId = product != null ? product.ID : "";

                float effectsBottom = MeasureEffectsBottom(content, productContent);

                if (rows.Count == 0)
                {
                    if (existingRoot != null)
                        existingRoot.gameObject.SetActive(false);
                    _lastProduct[contentId] = "";
                    ResizePanel(__instance, content, content.Height);
                    return;
                }

                float rowHeight = config.RowHeight > 0 ? config.RowHeight : 28f;
                float iconSize = config.IconSize > 0 ? config.IconSize : 24f;
                float blockHeight = HeaderHeight + rows.Count * rowHeight + TopMargin;
                float newHeight = effectsBottom + TopMargin + blockHeight;

                bool needRebuild = existingRoot == null || _lastProduct.GetValueOrDefault(contentId) != productId;
                RectTransform root;
                if (needRebuild)
                {
                    if (existingRoot != null)
                        UnityEngine.Object.DestroyImmediate(existingRoot.gameObject);
                    root = BuildRows(content, productContent.DescriptionLabel, rows, effectsBottom, blockHeight, rowHeight, iconSize, config);
                    _lastProduct[contentId] = productId;
                }
                else
                {
                    root = existingRoot.TryCast<RectTransform>() ?? existingRoot.GetComponent<RectTransform>();
                    root.gameObject.SetActive(true);
                }

                ResizePanel(__instance, content, newHeight);
            }
            catch (Exception e)
            {
                Log.Error($"[Lithium] ProductTooltips failed for {(item != null && item.Definition != null ? item.Definition.ID : "?")}: {e}");
            }
        }

        private static RectTransform BuildRows(ItemInfoContent content, TextMeshProUGUI template, List<MixRow> rows,
            float topOffset, float blockHeight, float rowHeight, float iconSize, ModProductTooltipsConfiguration config)
        {
            GameObject rootGo = new(RootName);
            RectTransform root = rootGo.AddComponent<RectTransform>();
            root.SetParent(content.transform, false);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = new Vector2(0f, blockHeight);
            root.anchoredPosition = new Vector2(0f, -(topOffset + TopMargin));
            rootGo.transform.localScale = Vector3.one;

            float fontSize = config.FontSize > 0 ? config.FontSize : template.fontSize;

            MakeText(root, template, $"<b>{config.MixesHeader}</b>", LeftPad, 0f, 160f, HeaderHeight, fontSize, TextAlignmentOptions.TopLeft);

            float arrowW = 12f;
            float gap = 2f;
            float mixerX = LeftPad;
            float arrowX = mixerX + iconSize + gap;
            float resultX = arrowX + arrowW + gap;
            float nameX = resultX + iconSize + gap;

            for (int i = 0; i < rows.Count; i++)
            {
                MixRow r = rows[i];
                float yTop = -(HeaderHeight + i * rowHeight);
                float iconY = yTop - (rowHeight - iconSize) * 0.5f;

                MakeIcon(root, r.MixerIcon, mixerX, iconY, iconSize);
                MakeText(root, template, config.Arrow, arrowX, yTop, arrowW, rowHeight, fontSize, TextAlignmentOptions.Midline);
                MakeIcon(root, r.ResultIcon, resultX, iconY, iconSize);
                MakeText(root, template, r.ResultName, nameX, yTop, 200f, rowHeight, fontSize, TextAlignmentOptions.MidlineLeft);
            }

            return root;
        }

        private static void MakeIcon(Transform parent, Sprite sprite, float x, float y, float size)
        {
            if (sprite == null)
                return;
            GameObject go = new("Icon");
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(x, y);
            go.transform.localScale = Vector3.one;
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
        }

        private static void MakeText(Transform parent, TextMeshProUGUI template, string text, float x, float y,
            float w, float h, float fontSize, TextAlignmentOptions align)
        {
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, parent);
            go.name = "Text";
            go.transform.localScale = Vector3.one;
            TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
            t.enableWordWrapping = false;
            t.alignment = align;
            if (fontSize > 0f)
                t.fontSize = fontSize;
            t.text = text;
            go.SetActive(true);
        }

        private static float MeasureEffectsBottom(ItemInfoContent content, ProductItemInfoContent productContent)
        {
            RectTransform contentRect = content.GetComponent<RectTransform>();
            if (contentRect == null)
                return content.Height;

            Il2CppStructArray<Vector3> contentCorners = new(4);
            contentRect.GetWorldCorners(contentCorners);
            float topY = contentCorners[1].y;

            float lowestY = topY;
            bool any = false;

            var labels = productContent.PropertyLabels;
            if (labels != null)
            {
                for (int i = 0; i < labels.Count; i++)
                {
                    TextMeshProUGUI label = labels[i];
                    if (label == null || !label.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(label.text))
                        continue;

                    Il2CppStructArray<Vector3> corners = new(4);
                    label.rectTransform.GetWorldCorners(corners);
                    float bottom = Mathf.Min(corners[0].y, corners[3].y);
                    if (!any || bottom < lowestY)
                    {
                        lowestY = bottom;
                        any = true;
                    }
                }
            }

            if (!any)
                return content.Height;

            float worldGap = topY - lowestY;
            float scaleY = content.transform.lossyScale.y;
            return scaleY > 0.0001f ? worldGap / scaleY : worldGap;
        }

        private static void ResizePanel(ItemInfoPanel panel, ItemInfoContent content, float height)
        {
            content.Height = height;
            RectTransform container = panel.Container;
            if (container != null)
                container.sizeDelta = new Vector2(container.sizeDelta.x, height);
        }

        private readonly struct MixRow
        {
            public readonly Sprite MixerIcon;
            public readonly Sprite ResultIcon;
            public readonly string ResultName;

            public MixRow(Sprite mixerIcon, Sprite resultIcon, string resultName)
            {
                MixerIcon = mixerIcon;
                ResultIcon = resultIcon;
                ResultName = resultName;
            }
        }

        private static List<MixRow> BuildForwardRecipes(ProductDefinition product, ModProductTooltipsConfiguration config)
        {
            List<MixRow> rows = new();
            ProductManager manager = NetworkSingleton<ProductManager>.Instance;
            if (manager == null || manager.mixRecipes == null)
                return rows;

            HashSet<string> mixerIds = new();
            if (manager.ValidMixIngredients != null)
            {
                for (int i = 0; i < manager.ValidMixIngredients.Count; i++)
                {
                    PropertyItemDefinition mixer = manager.ValidMixIngredients[i];
                    if (mixer != null)
                        mixerIds.Add(mixer.ID);
                }
            }

            HashSet<string> discoveredIds = new();
            foreach (ProductDefinition discovered in ProductManager.DiscoveredProducts.ToList())
            {
                if (discovered != null)
                    discoveredIds.Add(discovered.ID);
            }

            var recipes = manager.mixRecipes;
            for (int r = 0; r < recipes.Count; r++)
            {
                StationRecipe recipe = recipes[r];
                if (recipe == null || recipe.Ingredients == null || recipe.Product == null || recipe.Product.Item == null)
                    continue;
                if (recipe.Ingredients.Count < 2)
                    continue;

                ItemDefinition mixerItem = null;
                ItemDefinition baseItem = null;
                for (int g = 0; g < recipe.Ingredients.Count; g++)
                {
                    ItemDefinition ingredient = recipe.Ingredients[g].Item;
                    if (ingredient == null)
                        continue;
                    if (mixerItem == null && mixerIds.Contains(ingredient.ID))
                        mixerItem = ingredient;
                    else
                        baseItem = ingredient;
                }

                if (mixerItem == null || baseItem == null || baseItem.ID != product.ID)
                    continue;

                ItemDefinition output = recipe.Product.Item;
                if (output.ID == product.ID)
                    continue;
                if (!discoveredIds.Contains(output.ID))
                    continue;

                rows.Add(new MixRow(mixerItem.Icon, output.Icon, output.Name));

                if (config.MaxLines > 0 && rows.Count >= config.MaxLines)
                    break;
            }

            return rows;
        }
    }
}
