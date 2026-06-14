using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Effects;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI.Phone.ProductManagerApp;
using Lithium.Helper;
using UnityEngine;
using UnityEngine.UI;

namespace Lithium.Modules.Products
{
    // Drives the search + effects filter overlaid on the phone's Products app. Single instance of the app
    // exists at a time (PlayerSingleton), so state is kept statically and rebuilt whenever a new app shows
    // up (e.g. after loading a different save).
    internal static class ProductListFilter
    {
        private const float BarHeight = 46f;
        private const float RowHeight = 28f;

        private static readonly Color BarColor = new(0.10f, 0.10f, 0.12f, 0.95f);
        private static readonly Color PanelColor = new(0.08f, 0.08f, 0.10f, 0.98f);
        private static readonly Color ButtonColor = new(0.20f, 0.20f, 0.24f, 1f);
        private static readonly Color RowColor = new(0.16f, 0.16f, 0.20f, 1f);
        private static readonly Color RowSelectedColor = new(0.20f, 0.45f, 0.30f, 1f);
        private static readonly Color TextColor = Color.white;

        private sealed class EffectOption
        {
            public string Id;
            public string Name;
            public Image Background;
            public Text Label;
        }

        private static ProductManagerApp _app;
        private static int _appId;
        private static bool _built;

        private static GameObject _bar;
        private static InputField _searchInput;
        private static Text _effectsButtonLabel;
        private static GameObject _effectsPanel;

        private static string _nameFilter = string.Empty;
        private static readonly HashSet<string> _selectedEffectIds = new();
        private static readonly List<EffectOption> _effectOptions = new();

        // Drop all references so the next EnsureBuilt rebuilds against a fresh app instance.
        public static void Reset()
        {
            _app = null;
            _appId = 0;
            _built = false;
            _bar = null;
            _searchInput = null;
            _effectsButtonLabel = null;
            _effectsPanel = null;
            _nameFilter = string.Empty;
            _selectedEffectIds.Clear();
            _effectOptions.Clear();
        }

        public static bool Enabled
        {
            get
            {
                ModProducts mod = Core.Get<ModProducts>();
                return mod != null && mod.Configuration.Enabled && mod.Configuration.EnableListFilter;
            }
        }

        // Builds the filter bar once for the given app. Safe to call repeatedly.
        public static void EnsureBuilt(ProductManagerApp app)
        {
            if (!Enabled || app == null)
                return;

            int id = app.GetInstanceID();
            if (_built && _appId == id)
                return;

            // A different app instance (new save) — start clean.
            if (_appId != id)
                Reset();

            try
            {
                Build(app);
                _app = app;
                _appId = id;
                _built = true;
                ApplyFilter();
                // The bar lives under the app container, but the container's active-state doesn't reliably
                // hide it across the phone's app-switch/rebuild dance — gate it explicitly on isOpen so it
                // never lingers on the home screen or other phone apps.
                SetVisible(app.isOpen);
            }
            catch (Exception e)
            {
                Log.Error($"[Lithium] Products filter build failed: {e}");
            }
        }

        // Per-frame visibility gate (driven from Core.OnUpdate via ModProducts.DriveUpdate).
        //
        // The SetOpen postfix alone can't be trusted to hide the bar: in IL2CPP the SetOpen(false) call on
        // the app-close path (closeApps delegate -> App.Close -> this.SetOpen) is devirtualized and inlined
        // into Close, so our postfix never fires there and the bar lingers over other apps. But the bar's
        // own ProductManagerApp.isOpen field is set inside the *vanilla* SetOpen body, which always runs —
        // so polling it each frame reliably tracks the real open/close state regardless of patch inlining.
        public static void DriveVisibility()
        {
            if (!_built || _bar == null || _app == null)
                return;

            if (!Enabled)
            {
                SetVisible(false);
                return;
            }

            SetVisible(_app.isOpen);
        }

        // Shows/hides the whole filter bar (search field + effects button) and collapses the effects panel.
        // Driven from the Products app's open/close so the overlay is only ever visible on that app.
        public static void SetVisible(bool visible)
        {
            if (_bar != null && _bar.activeSelf != visible)
                _bar.SetActive(visible);
            if (!visible && _effectsPanel != null && _effectsPanel.activeSelf)
                _effectsPanel.SetActive(false);
        }

        private static void Build(ProductManagerApp app)
        {
            ProductFilterUi.ResolveFont();

            RectTransform container = app.appContainer;
            if (container == null)
            {
                Log.Warning("[Lithium] Products filter: app container missing; skipping.");
                return;
            }

            ModProductsConfiguration config = Core.Get<ModProducts>().Configuration;

            // --- Top bar -----------------------------------------------------------------------------
            GameObject bar = ProductFilterUi.MakeImage(container, "LithiumProductFilterBar", BarColor);
            _bar = bar;
            RectTransform brt = bar.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.sizeDelta = new Vector2(0f, BarHeight);
            brt.anchoredPosition = Vector2.zero;

            // Search field (cloned from the detail panel's price input).
            InputField template = app.DetailPanel != null ? app.DetailPanel.ValueLabel : null;
            _searchInput = ProductFilterUi.CloneSearchInput(template, config.SearchPlaceholder);
            if (_searchInput != null)
            {
                _searchInput.gameObject.SetActive(true);
                _searchInput.transform.SetParent(bar.transform, false);
                _searchInput.transform.localScale = Vector3.one;
                RectTransform irt = _searchInput.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0f, 0f);
                irt.anchorMax = new Vector2(0.62f, 1f);
                irt.offsetMin = new Vector2(6f, 6f);
                irt.offsetMax = new Vector2(-4f, -6f);
                _searchInput.onValueChanged.AddListener(ProductFilterUi.UA(OnNameChanged));
            }
            else
            {
                Log.Warning("[Lithium] Products filter: no InputField template to clone; search unavailable.");
            }

            // Effects multi-select button.
            Button effectsBtn = ProductFilterUi.MakeButton(bar.transform, "EffectsButton",
                config.EffectsButtonLabel, 16, ButtonColor, TextColor, ToggleEffectsPanel, out _, out _effectsButtonLabel);
            RectTransform ert = effectsBtn.GetComponent<RectTransform>();
            ert.anchorMin = new Vector2(0.64f, 0f);
            ert.anchorMax = new Vector2(1f, 1f);
            ert.offsetMin = new Vector2(4f, 6f);
            ert.offsetMax = new Vector2(-6f, -6f);

            BuildEffectsPanel(container, config);
            UpdateEffectsButtonLabel();
        }

        private static void BuildEffectsPanel(RectTransform container, ModProductsConfiguration config)
        {
            _effectOptions.Clear();

            _effectsPanel = ProductFilterUi.MakeImage(container, "LithiumEffectsPanel", PanelColor);
            RectTransform prt = _effectsPanel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.40f, 0.08f);
            prt.anchorMax = new Vector2(0.99f, 0.86f);
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            Text title = ProductFilterUi.MakeText(_effectsPanel.transform, "Title",
                "Effects (must have all)", 15, TextColor, TextAnchor.MiddleLeft);
            RectTransform trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(0.7f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.sizeDelta = new Vector2(0f, 26f);
            trt.anchoredPosition = new Vector2(8f, -4f);

            Button clearBtn = ProductFilterUi.MakeButton(_effectsPanel.transform, "ClearButton",
                "Clear", 14, ButtonColor, TextColor, ClearEffects, out _, out _);
            RectTransform crt = clearBtn.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.72f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(0f, 24f);
            crt.anchoredPosition = new Vector2(-6f, -5f);

            // Scroll list filling the rest of the panel.
            RectTransform listContent = ProductFilterUi.MakeScrollList(_effectsPanel.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-12f, -36f), new Vector2(0f, 6f), PanelColor);
            // Stretch the scroll list horizontally/vertically inside the panel below the title row.
            RectTransform listRoot = listContent.parent.parent.GetComponent<RectTransform>();
            listRoot.anchorMin = new Vector2(0f, 0f);
            listRoot.anchorMax = new Vector2(1f, 1f);
            listRoot.offsetMin = new Vector2(6f, 6f);
            listRoot.offsetMax = new Vector2(-6f, -34f);

            List<EffectOption> options = GatherEffects();
            listContent.sizeDelta = new Vector2(0f, options.Count * RowHeight);

            for (int i = 0; i < options.Count; i++)
            {
                EffectOption opt = options[i];
                Button row = ProductFilterUi.MakeButton(listContent, "Effect_" + opt.Id, opt.Name, 14,
                    RowColor, TextColor, () => ToggleEffect(opt.Id), out Image bg, out Text label);
                label.alignment = TextAnchor.MiddleLeft;
                RectTransform lrt = label.rectTransform;
                lrt.offsetMin = new Vector2(8f, 0f);
                lrt.offsetMax = new Vector2(-4f, 0f);

                RectTransform rrt = row.GetComponent<RectTransform>();
                rrt.anchorMin = new Vector2(0f, 1f);
                rrt.anchorMax = new Vector2(1f, 1f);
                rrt.pivot = new Vector2(0.5f, 1f);
                rrt.sizeDelta = new Vector2(0f, RowHeight - 2f);
                rrt.anchoredPosition = new Vector2(0f, -i * RowHeight);

                opt.Background = bg;
                opt.Label = label;
                _effectOptions.Add(opt);
            }

            _effectsPanel.SetActive(false);
        }

        // Distinct effects across every product, ordered by name.
        private static List<EffectOption> GatherEffects()
        {
            Dictionary<string, EffectOption> byId = new();
            ProductManager manager = NetworkSingleton<ProductManager>.Instance;
            if (manager != null && manager.AllProducts != null)
            {
                var products = manager.AllProducts;
                for (int i = 0; i < products.Count; i++)
                {
                    ProductDefinition def = products[i];
                    if (def == null || def.Properties == null)
                        continue;
                    for (int j = 0; j < def.Properties.Count; j++)
                    {
                        Effect effect = def.Properties[j];
                        if (effect == null || string.IsNullOrEmpty(effect.ID) || byId.ContainsKey(effect.ID))
                            continue;
                        byId[effect.ID] = new EffectOption
                        {
                            Id = effect.ID,
                            Name = string.IsNullOrEmpty(effect.Name) ? effect.ID : effect.Name
                        };
                    }
                }
            }

            return byId.Values.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // --- Callbacks -------------------------------------------------------------------------------
        private static void OnNameChanged(string value)
        {
            _nameFilter = (value ?? string.Empty).Trim();
            ApplyFilter();
        }

        private static void ToggleEffectsPanel()
        {
            if (_effectsPanel == null)
                return;
            _effectsPanel.SetActive(!_effectsPanel.activeSelf);
        }

        private static void ToggleEffect(string id)
        {
            if (!_selectedEffectIds.Remove(id))
                _selectedEffectIds.Add(id);
            RefreshEffectVisuals();
            UpdateEffectsButtonLabel();
            ApplyFilter();
        }

        private static void ClearEffects()
        {
            if (_selectedEffectIds.Count == 0)
                return;
            _selectedEffectIds.Clear();
            RefreshEffectVisuals();
            UpdateEffectsButtonLabel();
            ApplyFilter();
        }

        private static void RefreshEffectVisuals()
        {
            foreach (EffectOption opt in _effectOptions)
            {
                if (opt.Background != null)
                    opt.Background.color = _selectedEffectIds.Contains(opt.Id) ? RowSelectedColor : RowColor;
                if (opt.Label != null)
                    opt.Label.text = (_selectedEffectIds.Contains(opt.Id) ? "✓ " : "") + opt.Name;
            }
        }

        private static void UpdateEffectsButtonLabel()
        {
            if (_effectsButtonLabel == null)
                return;
            string baseLabel = Core.Get<ModProducts>().Configuration.EffectsButtonLabel;
            _effectsButtonLabel.text = _selectedEffectIds.Count > 0
                ? $"{baseLabel} ({_selectedEffectIds.Count})"
                : baseLabel;
        }

        // --- Filtering -------------------------------------------------------------------------------
        public static void ApplyFilter()
        {
            if (!_built || _app == null)
                return;

            try
            {
                if (_app.FavouritesContainer != null)
                    FilterContainer(_app.FavouritesContainer.Container);

                var typeContainers = _app.ProductTypeContainers;
                if (typeContainers != null)
                {
                    for (int i = 0; i < typeContainers.Count; i++)
                    {
                        var tc = typeContainers[i];
                        if (tc != null)
                            FilterContainer(tc.Container);
                    }
                }

                RebuildLayout();
            }
            catch (Exception e)
            {
                Log.Error($"[Lithium] Products filter apply failed: {e}");
            }
        }

        // A single ForceRebuildLayoutImmediate is not enough here: the section blocks (drug-type header +
        // entry grid) are sized by nested ContentSizeFitters/VerticalLayoutGroups that only recompute via
        // the game's own rebuild dance (immediate rebuild, end-of-frame rebuild, fitter/group toggling —
        // see ProductManagerApp.DelayedRebuildLayout/SetOpen). Without it, hiding entries leaves stale
        // section heights and the remaining icons draw over the headers below.
        private static void RebuildLayout()
        {
            // While the app is closed its GameObject is inactive (no coroutine host), and SetOpen runs the
            // full rebuild dance itself before our SetOpen postfix re-applies the filter — skip safely.
            if (!_app.isOpen)
                return;

            _app.DelayedRebuildLayout();

            var groups = _app.GetComponentsInChildren<VerticalLayoutGroup>(true);
            if (groups != null)
            {
                foreach (VerticalLayoutGroup group in groups)
                {
                    if (group == null)
                        continue;
                    group.enabled = false;
                    group.enabled = true;
                }
            }
        }

        private static void FilterContainer(RectTransform container)
        {
            if (container == null)
                return;

            int count = container.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = container.GetChild(i);
                if (child == null)
                    continue;
                ProductEntry entry = child.GetComponent<ProductEntry>();
                if (entry == null || entry.Definition == null)
                    continue;

                bool visible = Matches(entry.Definition);
                if (child.gameObject.activeSelf != visible)
                    child.gameObject.SetActive(visible);
            }
        }

        private static bool Matches(ProductDefinition def)
        {
            if (_nameFilter.Length > 0)
            {
                string name = def.Name;
                if (string.IsNullOrEmpty(name) ||
                    name.IndexOf(_nameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            if (_selectedEffectIds.Count > 0)
            {
                if (def.Properties == null)
                    return false;
                foreach (string required in _selectedEffectIds)
                {
                    if (!HasEffect(def, required))
                        return false;
                }
            }

            return true;
        }

        private static bool HasEffect(ProductDefinition def, string effectId)
        {
            var props = def.Properties;
            for (int i = 0; i < props.Count; i++)
            {
                Effect e = props[i];
                if (e != null && e.ID == effectId)
                    return true;
            }
            return false;
        }
    }
}
