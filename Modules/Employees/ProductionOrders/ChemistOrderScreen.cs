using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Storage;
using Lithium.Modules.PhoneApp;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Lithium.Modules.Employees.ProductionOrders
{
    // The production-order screen: a self-contained screen-space overlay (own Canvas) for choosing a product
    // (searchable, drug-type filter, listed-only filter, icons), a quantity, and which non-empty shelves feed
    // the order (searchable). Past valid orders can be reloaded with one click. Order creation routes through
    // ChemistOrderService.TrySetOrder on the host, or ChemOrderNet to the host from a client. Built from code
    // with PhoneAppBuilder helpers; frees the mouse and locks player input while open so the fields are typeable.
    internal sealed class ChemistOrderScreen
    {
        private const string UiElementName = "LithiumChemistOrderScreen";

        private static readonly Color Bg = new(0.10f, 0.11f, 0.13f, 0.985f);
        private static readonly Color Header = new(0.85f, 0.95f, 1f);
        private static readonly Color RowColor = new(0.17f, 0.19f, 0.22f);
        private static readonly Color RowAlt = new(0.20f, 0.22f, 0.26f);
        private static readonly Color SelColor = new(0.20f, 0.45f, 0.30f);
        private static readonly Color Btn = new(0.22f, 0.25f, 0.30f);
        private static readonly Color BtnOn = new(0.20f, 0.42f, 0.55f);

        private static ChemistOrderScreen _instance;

        private GameObject _root;
        private Chemist _chemist;
        private Font _font;

        private List<ChemistOrderService.ProductOption> _products = new();
        private List<ChemistOrderService.ShelfOption> _shelves = new();
        private List<OrderHistoryEntry> _history = new();

        private string _selectedProductId = "";
        private int _quantity = 10;
        private readonly HashSet<string> _selected = new();

        private string _productSearch = "";
        private string _shelfSearch = "";
        private int _drugFilter; // 0 All, 1 Weed, 2 Meth, 3 Coke, 4 Shroom, 5 Other
        private bool _listedOnly;

        // live widgets
        private RectTransform _productContent;
        private ScrollRect _productScroll;
        private RectTransform _shelfContent;
        private ScrollRect _shelfScroll;
        private readonly Dictionary<string, Image> _productRowBg = new();
        private readonly Dictionary<string, Image> _shelfRowBg = new();
        private readonly List<(Image bg, int index)> _drugButtons = new();
        private Image _listedBtnBg;
        private Image _selIcon;
        private Text _selName;
        private Text _quantityLabel;
        private Text _chainLabel;
        private Text _statusLabel;

        // progress view (shown when the chemist already has an active order, host only)
        private bool _progressMode;
        private object _progressLoop;
        private Text _progStarted;
        private Text _progRemaining;
        private RectTransform _progBarFillRt;
        private Text _progStatus;
        private GameObject _stopButtonGo;

        public static void Request(Chemist chemist)
        {
            if (chemist == null)
                return;
            MelonCoroutines.Start(OpenDeferred(chemist));
        }

        private static IEnumerator OpenDeferred(Chemist chemist)
        {
            yield return null;
            yield return null;
            try { (_instance ??= new ChemistOrderScreen()).Open(chemist); }
            catch (Exception e) { Log.Warning($"[ChemistOrders] Failed to open order screen: {e.Message}"); }
        }

        private void Open(Chemist chemist)
        {
            Teardown();

            _chemist = chemist;
            _font = ResolveFont();
            _progressMode = false;
            _products = ChemistOrderService.GetOrderableProductInfos();

            // If this chemist already has a running order, show its progress with a Stop/Close instead of the
            // creation form. Only the host owns the order store, so only the host can show progress; a client
            // always gets the creation form (its Cancel button routes a stop request to the host).
            ChemistOrderState existing = ChemOrderNet.IsHost ? ChemistOrderService.GetOrder(chemist) : null;
            if (existing != null && existing.Active)
            {
                BuildProgress(existing);
                LockInput();
                return;
            }

            _shelves = ChemistOrderService.GetNonEmptyShelves(chemist);
            _history = ChemistOrderService.GetHistory(chemist);
            _selected.Clear();
            _selectedProductId = "";
            _quantity = 10;
            _productSearch = "";
            _shelfSearch = "";
            _drugFilter = 0;
            _listedOnly = false;

            BuildForm();
            LockInput();
            RebuildProducts();
            RebuildShelves();
            Refresh();
        }

        // -------------------------------------------------------------------------------------------------
        //  Layout
        // -------------------------------------------------------------------------------------------------

        private void BuildForm()
        {
            _root = new GameObject(UiElementName);
            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            _root.AddComponent<GraphicRaycaster>();

            GameObject dim = Child(_root.transform, "Dim");
            RectTransform dimRt = dim.AddComponent<RectTransform>();
            Fill(dimRt);
            dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            GameObject panel = Child(_root.transform, "Panel");
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(980f, 720f);
            prt.anchoredPosition = Vector2.zero;
            panel.AddComponent<Image>().color = Bg;
            Transform p = panel.transform;

            Label(p, $"Production Order — {SafeName(_chemist)}", 24, new Vector2(20f, -14f), 760f, 34f,
                TextAnchor.MiddleLeft, FontStyle.Bold, Header);
            Button(p, "✕  Close", new Vector2(820f, -14f), 140f, 34f, () => Close(), new Color(0.45f, 0.25f, 0.22f));

            // ---- Left column: products ----
            const float lx = 20f;
            const float lw = 460f;
            Label(p, "PRODUCT", 15, new Vector2(lx, -58f), lw, 20f, TextAnchor.MiddleLeft, FontStyle.Bold, Color.gray);
            MakeInput(p, "Search products…", new Vector2(lx, -80f), new Vector2(lw, 32f), s => { _productSearch = s; RebuildProducts(); });

            string[] drugs = { "All", "Weed", "Meth", "Coke", "Shroom", "Other" };
            _drugButtons.Clear();
            float dx = lx;
            float dw = (lw - 5 * 4f) / 6f;
            for (int i = 0; i < drugs.Length; i++)
            {
                int idx = i;
                Button b = Button(p, drugs[i], new Vector2(dx, -120f), dw, 28f, () => { _drugFilter = idx; RebuildProducts(); RestyleFilters(); }, Btn);
                _drugButtons.Add((b.GetComponent<Image>(), idx));
                dx += dw + 4f;
            }

            Button lb = Button(p, "Listed for sale only", new Vector2(lx, -154f), lw, 28f,
                () => { _listedOnly = !_listedOnly; RebuildProducts(); RestyleFilters(); }, Btn);
            _listedBtnBg = lb.GetComponent<Image>();

            (_productScroll, _productContent) = MakeScroll(p, new Vector2(lx, -190f), new Vector2(lw, 360f));

            // ---- Right column: shelves ----
            const float rx = 500f;
            const float rw = 460f;
            Label(p, "INGREDIENT SHELVES", 15, new Vector2(rx, -58f), rw, 20f, TextAnchor.MiddleLeft, FontStyle.Bold, Color.gray);
            MakeInput(p, "Search shelves…", new Vector2(rx, -80f), new Vector2(rw, 32f), s => { _shelfSearch = s; RebuildShelves(); });
            Label(p, "Only shelves containing items are listed. Click to toggle.", 13, new Vector2(rx, -116f), rw, 20f,
                TextAnchor.MiddleLeft, FontStyle.Italic, Color.gray);
            (_shelfScroll, _shelfContent) = MakeScroll(p, new Vector2(rx, -142f), new Vector2(rw, 408f));

            // ---- Bottom: selection / quantity / chain / history / actions ----
            float by = -566f;
            _selIcon = MakeIconImage(p, new Vector2(20f, by), 40f);
            _selName = Label(p, "Select a product", 20, new Vector2(68f, by), 520f, 40f, TextAnchor.MiddleLeft,
                FontStyle.Bold, Color.white);

            Label(p, "Qty", 15, new Vector2(600f, by), 40f, 40f, TextAnchor.MiddleLeft, FontStyle.Normal, Color.gray);
            Button(p, "-10", new Vector2(640f, by), 52f, 40f, () => AddQty(-10));
            Button(p, "-1", new Vector2(696f, by), 44f, 40f, () => AddQty(-1));
            _quantityLabel = Label(p, "10", 22, new Vector2(744f, by), 80f, 40f, TextAnchor.MiddleCenter, FontStyle.Bold, Color.white);
            Button(p, "+1", new Vector2(828f, by), 44f, 40f, () => AddQty(1));
            Button(p, "+10", new Vector2(876f, by), 52f, 40f, () => AddQty(10));

            _chainLabel = Label(p, "", 14, new Vector2(20f, -612f), 940f, 48f, TextAnchor.UpperLeft, FontStyle.Normal,
                new Color(0.8f, 0.85f, 0.9f));

            // Recent orders.
            Label(p, "Recent:", 14, new Vector2(20f, -662f), 70f, 26f, TextAnchor.MiddleLeft, FontStyle.Normal, Color.gray);
            float hx = 92f;
            int shown = Math.Min(_history.Count, 4);
            for (int i = 0; i < shown; i++)
            {
                OrderHistoryEntry h = _history[i];
                string label = Trim($"{h.Goal}x {DisplayName(h)}", 22);
                Button(p, label, new Vector2(hx, -662f), 200f, 26f, () => LoadHistory(h), new Color(0.24f, 0.28f, 0.34f));
                hx += 204f;
            }

            _statusLabel = Label(p, "", 14, new Vector2(20f, -692f), 620f, 24f, TextAnchor.MiddleLeft, FontStyle.Normal,
                new Color(1f, 0.85f, 0.4f));
            Button(p, "Assign Order", new Vector2(660f, -690f), 150f, 30f, AssignOrder, new Color(0.20f, 0.45f, 0.25f));
            Button(p, "Cancel Order", new Vector2(818f, -690f), 142f, 30f, CancelOrder, new Color(0.45f, 0.25f, 0.22f));
        }

        // -------------------------------------------------------------------------------------------------
        //  Progress view (active order)
        // -------------------------------------------------------------------------------------------------

        private void BuildProgress(ChemistOrderState order)
        {
            _progressMode = true;

            _root = new GameObject(UiElementName);
            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            _root.AddComponent<GraphicRaycaster>();

            GameObject dim = Child(_root.transform, "Dim");
            RectTransform dimRt = dim.AddComponent<RectTransform>();
            Fill(dimRt);
            dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            GameObject panel = Child(_root.transform, "Panel");
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(640f, 440f);
            prt.anchoredPosition = Vector2.zero;
            panel.AddComponent<Image>().color = Bg;
            Transform p = panel.transform;

            Label(p, $"Production Order — {SafeName(_chemist)}", 22, new Vector2(20f, -14f), 470f, 32f,
                TextAnchor.MiddleLeft, FontStyle.Bold, Header);
            Button(p, "✕  Close", new Vector2(480f, -14f), 140f, 32f, () => Close(), new Color(0.45f, 0.25f, 0.22f));

            // Product icon + name.
            ChemistOrderService.ProductOption sel = FindProduct(order.TargetProductId);
            Image icon = MakeIconImage(p, new Vector2(20f, -62f), 56f);
            if (sel?.Icon != null) { icon.sprite = sel.Icon; icon.enabled = true; }
            string pname = sel != null ? sel.Name
                : (string.IsNullOrEmpty(order.TargetName) ? order.TargetProductId : order.TargetName);
            Label(p, pname, 24, new Vector2(88f, -64f), 530f, 32f, TextAnchor.MiddleLeft, FontStyle.Bold, Color.white);

            // Progress counts + bar.
            _progStarted = Label(p, "", 18, new Vector2(88f, -100f), 300f, 24f, TextAnchor.MiddleLeft,
                FontStyle.Normal, new Color(0.7f, 1f, 0.75f));
            _progRemaining = Label(p, "", 16, new Vector2(360f, -100f), 258f, 24f, TextAnchor.MiddleRight,
                FontStyle.Normal, Color.gray);

            GameObject barBg = Child(p, "BarBg");
            RectTransform barBgRt = barBg.AddComponent<RectTransform>();
            TopLeft(barBgRt, new Vector2(20f, -136f), new Vector2(600f, 16f));
            barBg.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.10f);
            GameObject barFill = Child(barBg.transform, "Fill");
            _progBarFillRt = barFill.AddComponent<RectTransform>();
            _progBarFillRt.anchorMin = new Vector2(0f, 0f);
            _progBarFillRt.anchorMax = new Vector2(0f, 1f);
            _progBarFillRt.pivot = new Vector2(0f, 0.5f);
            _progBarFillRt.offsetMin = Vector2.zero;
            _progBarFillRt.offsetMax = Vector2.zero;
            barFill.AddComponent<Image>().color = new Color(0.30f, 0.65f, 0.40f);

            // Chain + shelves (read-only).
            Label(p, BuildChainText(order), 14, new Vector2(20f, -166f), 600f, 24f, TextAnchor.UpperLeft,
                FontStyle.Normal, new Color(0.8f, 0.85f, 0.9f));
            Label(p, "Ingredient shelves:", 14, new Vector2(20f, -206f), 600f, 20f, TextAnchor.MiddleLeft,
                FontStyle.Bold, Color.gray);
            Label(p, BuildShelvesText(order), 14, new Vector2(20f, -228f), 600f, 72f, TextAnchor.UpperLeft,
                FontStyle.Normal, new Color(0.75f, 0.8f, 0.85f));

            _progStatus = Label(p, "Stopping returns all carried items and station contents to the output, " +
                "and frees the reserved shelf slots.", 13, new Vector2(20f, -312f), 600f, 40f, TextAnchor.UpperLeft,
                FontStyle.Italic, new Color(0.7f, 0.72f, 0.78f));

            // Actions.
            _stopButtonGo = Button(p, "Stop Order & Return Items", new Vector2(20f, -398f), 320f, 32f, StopOrder,
                new Color(0.5f, 0.28f, 0.24f)).gameObject;
            Button(p, "Close", new Vector2(490f, -398f), 130f, 32f, () => Close(), Btn);

            UpdateProgressView(order);
            _progressLoop = MelonCoroutines.Start(ProgressRoutine());
        }

        private void UpdateProgressView(ChemistOrderState order)
        {
            if (order == null)
                return;
            int goal = Math.Max(1, order.Goal);
            int started = Math.Max(0, Math.Min(order.Started, order.Goal));
            int remaining = Math.Max(0, order.Goal - order.Started);

            if (_progStarted != null)
                _progStarted.text = $"Produced {started} / {order.Goal}";
            if (_progRemaining != null)
                _progRemaining.text = remaining > 0 ? $"{remaining} to go" : "complete";
            if (_progBarFillRt != null)
                _progBarFillRt.anchorMax = new Vector2(Mathf.Clamp01((float)order.Started / goal), 1f);
        }

        private IEnumerator ProgressRoutine()
        {
            while (_root != null && _progressMode)
            {
                yield return new WaitForSeconds(0.5f);
                if (_root == null || !_progressMode)
                    yield break;

                ChemistOrderState order = ChemistOrderService.GetOrder(_chemist);
                if (order == null || !order.Active)
                {
                    OnOrderFinished();
                    yield break;
                }
                UpdateProgressView(order);
            }
        }

        private void OnOrderFinished()
        {
            if (_progBarFillRt != null)
                _progBarFillRt.anchorMax = new Vector2(1f, 1f);
            if (_progStarted != null)
                _progStarted.text = "Order complete";
            if (_progRemaining != null)
                _progRemaining.text = "done";
            if (_progStatus != null)
            {
                _progStatus.text = "The chemist has finished this order.";
                _progStatus.color = new Color(0.6f, 1f, 0.65f);
            }
            if (_stopButtonGo != null)
                _stopButtonGo.SetActive(false);
        }

        private void StopOrder()
        {
            if (_chemist == null)
            {
                Close();
                return;
            }
            if (!ChemOrderNet.IsHost)
            {
                ChemOrderNet.SendCancel(_chemist);
                Close();
                return;
            }
            ChemistOrderService.StopOrder(_chemist);
            Close();
        }

        private string BuildChainText(ChemistOrderState order)
        {
            if (order?.Chain == null || order.Chain.Count == 0)
                return "";
            System.Text.StringBuilder path = new();
            path.Append(ItemName(order.Chain[0].InputId));
            foreach (OrderStep step in order.Chain)
                path.Append(" +").Append(ItemName(step.MixerId));
            path.Append(" → ").Append(ItemName(order.Chain[order.Chain.Count - 1].OutputId));
            return $"{order.Chain.Count}-step mix:  {path}";
        }

        private string BuildShelvesText(ChemistOrderState order)
        {
            if (order?.ShelfGuids == null || order.ShelfGuids.Count == 0)
                return "—";
            List<string> names = new();
            foreach (string g in order.ShelfGuids)
            {
                var shelf = ChemistOrderService.ResolveShelf(g);
                string nm = "Shelf";
                try
                {
                    if (shelf != null && shelf.StorageEntity != null && !string.IsNullOrEmpty(shelf.StorageEntity.StorageEntityName))
                        nm = shelf.StorageEntity.StorageEntityName;
                }
                catch { /* default */ }
                names.Add(nm);
            }
            return string.Join(", ", names);
        }

        // -------------------------------------------------------------------------------------------------
        //  List rebuilds
        // -------------------------------------------------------------------------------------------------

        private void RebuildProducts()
        {
            if (_productContent == null)
                return;
            Clear(_productContent);
            _productRowBg.Clear();

            float rowH = 34f;
            float y = 0f;
            int n = 0;
            string q = _productSearch != null ? _productSearch.Trim().ToLowerInvariant() : "";

            foreach (ChemistOrderService.ProductOption opt in _products)
            {
                if (q.Length > 0 && (opt.Name == null || !opt.Name.ToLowerInvariant().Contains(q)))
                    continue;
                if (!MatchesDrug(opt.DrugType))
                    continue;
                if (_listedOnly && !opt.Listed)
                    continue;

                AddProductRow(opt, y, rowH, n);
                y -= rowH + 2f;
                n++;
            }

            _productContent.sizeDelta = new Vector2(0f, Math.Max(0f, -y));
            if (_productScroll != null)
                _productScroll.verticalNormalizedPosition = 1f;

            if (n == 0)
                Label(_productContent, "No products match.", 14, new Vector2(8f, -8f), 400f, 22f,
                    TextAnchor.MiddleLeft, FontStyle.Italic, Color.gray);
        }

        private void AddProductRow(ChemistOrderService.ProductOption opt, float y, float rowH, int index)
        {
            GameObject go = Child(_productContent, "P");
            RectTransform rt = go.AddComponent<RectTransform>();
            Row(rt, y, rowH);
            Image bg = go.AddComponent<Image>();
            bg.color = opt.Id == _selectedProductId ? SelColor : (index % 2 == 0 ? RowColor : RowAlt);
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            string id = opt.Id;
            btn.onClick.AddListener(UA(() => SelectProduct(id)));
            _productRowBg[id] = bg;

            if (opt.Icon != null)
            {
                Image icon = MakeIconImage(go.transform, new Vector2(4f, -3f), 28f);
                icon.sprite = opt.Icon;
                icon.enabled = true;
            }

            string suffix = opt.Steps > 1 ? $"  <color=#88aacc>· {opt.Steps} steps</color>" : "";
            if (opt.Listed) suffix += "  <color=#88cc88>· listed</color>";
            // Point-anchored label; width comes from sizeDelta in Label().
            Label(go.transform, opt.Name + suffix, 15, new Vector2(38f, 0f), 412f, rowH,
                TextAnchor.MiddleLeft, FontStyle.Normal, Color.white);
        }

        private void RebuildShelves()
        {
            if (_shelfContent == null)
                return;
            Clear(_shelfContent);
            _shelfRowBg.Clear();

            float rowH = 34f;
            float y = 0f;
            int n = 0;
            string q = _shelfSearch != null ? _shelfSearch.Trim().ToLowerInvariant() : "";

            foreach (ChemistOrderService.ShelfOption opt in _shelves)
            {
                if (q.Length > 0 && (opt.Name == null || !opt.Name.ToLowerInvariant().Contains(q)))
                    continue;
                AddShelfRow(opt, y, rowH, n);
                y -= rowH + 2f;
                n++;
            }

            _shelfContent.sizeDelta = new Vector2(0f, Math.Max(0f, -y));
            if (_shelfScroll != null)
                _shelfScroll.verticalNormalizedPosition = 1f;

            if (n == 0)
                Label(_shelfContent, _shelves.Count == 0 ? "No non-empty shelves on this property." : "No shelves match.",
                    14, new Vector2(8f, -8f), 420f, 22f, TextAnchor.MiddleLeft, FontStyle.Italic,
                    new Color(1f, 0.7f, 0.6f));
        }

        private void AddShelfRow(ChemistOrderService.ShelfOption opt, float y, float rowH, int index)
        {
            GameObject go = Child(_shelfContent, "S");
            RectTransform rt = go.AddComponent<RectTransform>();
            Row(rt, y, rowH);
            Image bg = go.AddComponent<Image>();
            bg.color = _selected.Contains(opt.Guid) ? SelColor : (index % 2 == 0 ? RowColor : RowAlt);
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            string g = opt.Guid;
            btn.onClick.AddListener(UA(() => ToggleShelf(g)));
            _shelfRowBg[g] = bg;

            Label(go.transform, $"{opt.Name}  <color=#888888>· {opt.ItemCount} item(s)</color>", 15,
                new Vector2(10f, 0f), 430f, rowH, TextAnchor.MiddleLeft, FontStyle.Normal, Color.white);
        }

        // -------------------------------------------------------------------------------------------------
        //  Interaction
        // -------------------------------------------------------------------------------------------------

        private void SelectProduct(string id)
        {
            _selectedProductId = id;
            foreach (KeyValuePair<string, Image> kv in _productRowBg)
                if (kv.Value != null)
                    kv.Value.color = kv.Key == id ? SelColor : RowColor;
            Refresh();
        }

        private void ToggleShelf(string guid)
        {
            if (!_selected.Remove(guid))
                _selected.Add(guid);
            if (_shelfRowBg.TryGetValue(guid, out Image bg) && bg != null)
                bg.color = _selected.Contains(guid) ? SelColor : RowColor;
            Refresh();
        }

        private void AddQty(int delta)
        {
            _quantity = Math.Max(1, Math.Min(9999, _quantity + delta));
            Refresh();
        }

        private void LoadHistory(OrderHistoryEntry h)
        {
            if (h == null)
                return;
            _selectedProductId = h.TargetProductId;
            _quantity = Math.Max(1, h.Goal);
            _selected.Clear();
            if (h.ShelfGuids != null)
                foreach (string g in h.ShelfGuids)
                    _selected.Add(g);

            // Clear filters so the chosen product/shelves are visible, then rebuild.
            _productSearch = "";
            _shelfSearch = "";
            _drugFilter = 0;
            _listedOnly = false;
            RestyleFilters();
            RebuildProducts();
            RebuildShelves();
            Refresh();
            SetStatus($"Loaded recent order: {h.Goal}x {DisplayName(h)}.", false);
        }

        private void AssignOrder()
        {
            if (string.IsNullOrEmpty(_selectedProductId))
            {
                SetStatus("Pick a product first.", true);
                return;
            }
            List<string> shelves = new(_selected);

            // On a non-host client the host owns the order store and orchestrator, so route the request to the
            // host instead of setting it locally (which would do nothing useful).
            if (!ChemOrderNet.IsHost)
            {
                if (shelves.Count == 0)
                {
                    SetStatus("Assign at least one shelf for ingredients.", true);
                    return;
                }
                ChemOrderNet.SendSet(_chemist, _selectedProductId, _quantity, shelves);
                Close(); // request sent to the host — close the UI
                return;
            }

            if (ChemistOrderService.TrySetOrder(_chemist, _selectedProductId, _quantity, shelves, out string error))
            {
                Close(); // order assigned — close the UI (re-open the chemist to see progress / stop it)
            }
            else
            {
                SetStatus(error, true);
            }
        }

        private void CancelOrder()
        {
            if (!ChemOrderNet.IsHost)
            {
                ChemOrderNet.SendCancel(_chemist);
                SetStatus("Cancel request sent to the host.", false);
                return;
            }
            ChemistOrderService.ClearOrder(_chemist);
            SetStatus("Order cancelled; reserved slots freed.", false);
        }

        private void Close()
        {
            Teardown();
            UnlockInput();
        }

        // -------------------------------------------------------------------------------------------------
        //  Refresh (selection summary + chain preview + filter styling)
        // -------------------------------------------------------------------------------------------------

        private void Refresh()
        {
            if (_quantityLabel != null)
                _quantityLabel.text = _quantity.ToString();

            ChemistOrderService.ProductOption sel = FindProduct(_selectedProductId);
            if (_selName != null)
                _selName.text = sel != null ? sel.Name : "Select a product";
            if (_selIcon != null)
            {
                _selIcon.sprite = sel?.Icon;
                _selIcon.enabled = sel?.Icon != null;
            }
            if (_chainLabel != null)
                _chainLabel.text = BuildPreview();

            RestyleFilters();
        }

        private void RestyleFilters()
        {
            foreach ((Image bg, int index) in _drugButtons)
                if (bg != null)
                    bg.color = index == _drugFilter ? BtnOn : Btn;
            if (_listedBtnBg != null)
                _listedBtnBg.color = _listedOnly ? BtnOn : Btn;
        }

        private string BuildPreview()
        {
            if (string.IsNullOrEmpty(_selectedProductId))
                return "";

            HashSet<string> available = AvailableFromSelected();
            List<OrderStep> chain = RecipeChainResolver.Resolve(_selectedProductId, null);
            if (chain == null || chain.Count == 0)
                return "<color=#ff8866>No known recipe chain for this product.</color>";

            System.Text.StringBuilder path = new();
            path.Append(ItemName(chain[0].InputId));
            foreach (OrderStep step in chain)
                path.Append(" +").Append(ItemName(step.MixerId));
            path.Append(" → ").Append(ItemName(chain[chain.Count - 1].OutputId));

            HashSet<string> needed = new() { chain[0].InputId };
            foreach (OrderStep step in chain)
                needed.Add(step.MixerId);

            System.Text.StringBuilder needs = new("Needs on shelves: ");
            bool first = true;
            foreach (string id in needed)
            {
                if (!first) needs.Append(", ");
                first = false;
                bool have = available.Contains(id);
                needs.Append(have ? "" : "<color=#ff8866>").Append(ItemName(id)).Append(have ? "" : "</color>");
            }

            return $"{chain.Count}-step mix:  {path}\n{needs}";
        }

        private HashSet<string> AvailableFromSelected()
        {
            HashSet<string> ids = new();
            foreach (string guid in _selected)
            {
                var shelf = ChemistOrderService.ResolveShelf(guid);
                StorageEntity storage = shelf != null ? shelf.StorageEntity : null;
                if (storage == null || storage.ItemSlots == null)
                    continue;
                var slots = storage.ItemSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    ItemSlot slot = slots[i];
                    if (slot == null || slot.ItemInstance == null || slot.Quantity <= 0)
                        continue;
                    ItemDefinition def = slot.ItemInstance.Definition;
                    if (def != null && !string.IsNullOrEmpty(def.ID))
                        ids.Add(def.ID);
                }
            }
            return ids;
        }

        private bool MatchesDrug(EDrugType d)
        {
            switch (_drugFilter)
            {
                case 1: return d == EDrugType.Marijuana;
                case 2: return d == EDrugType.Methamphetamine;
                case 3: return d == EDrugType.Cocaine;
                case 4: return d == EDrugType.Shrooms;
                case 5: return d != EDrugType.Marijuana && d != EDrugType.Methamphetamine
                                && d != EDrugType.Cocaine && d != EDrugType.Shrooms;
                default: return true;
            }
        }

        private ChemistOrderService.ProductOption FindProduct(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            foreach (ChemistOrderService.ProductOption p in _products)
                if (p.Id == id)
                    return p;
            return null;
        }

        private void SetStatus(string text, bool isError)
        {
            if (_statusLabel == null)
                return;
            _statusLabel.text = text;
            _statusLabel.color = isError ? new Color(1f, 0.55f, 0.5f) : new Color(0.6f, 1f, 0.65f);
        }

        // -------------------------------------------------------------------------------------------------
        //  Widget primitives
        // -------------------------------------------------------------------------------------------------

        private static GameObject Child(Transform parent, string name)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one;
            return go;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void TopLeft(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        private static void Row(RectTransform rt, float y, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, h);
            rt.anchoredPosition = new Vector2(0f, y);
        }

        private Text Label(Transform parent, string text, int size, Vector2 pos, float w, float h,
            TextAnchor anchor, FontStyle style, Color color)
        {
            Text t = PhoneAppBuilder.MakeText(parent, _font, "L", size, color, anchor, true);
            t.fontStyle = style;
            t.text = text;
            TopLeft(t.rectTransform, pos, new Vector2(w, h));
            return t;
        }

        private Button Button(Transform parent, string label, Vector2 pos, float w, float h, Action onClick)
            => Button(parent, label, pos, w, h, onClick, Btn);

        private Button Button(Transform parent, string label, Vector2 pos, float w, float h, Action onClick, Color color)
        {
            Button btn = PhoneAppBuilder.MakeButton(parent, _font, "B", label, 15, color, Color.white, onClick, out _, out _);
            TopLeft(btn.GetComponent<RectTransform>(), pos, new Vector2(w, h));
            return btn;
        }

        private Image MakeIconImage(Transform parent, Vector2 pos, float size)
        {
            GameObject go = Child(parent, "Icon");
            RectTransform rt = go.AddComponent<RectTransform>();
            TopLeft(rt, pos, new Vector2(size, size));
            Image img = go.AddComponent<Image>();
            img.preserveAspect = true;
            img.enabled = false;
            return img;
        }

        private InputField MakeInput(Transform parent, string placeholder, Vector2 pos, Vector2 size, Action<string> onChanged)
        {
            GameObject go = Child(parent, "Input");
            RectTransform rt = go.AddComponent<RectTransform>();
            TopLeft(rt, pos, size);
            Image bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.12f);

            InputField input = go.AddComponent<InputField>();

            Text textComp = PhoneAppBuilder.MakeText(go.transform, _font, "Text", 16, Color.white, TextAnchor.MiddleLeft, false);
            FillPad(textComp.rectTransform, 8f, 4f);
            textComp.supportRichText = false;

            Text ph = PhoneAppBuilder.MakeText(go.transform, _font, "Placeholder", 16, new Color(1f, 1f, 1f, 0.4f),
                TextAnchor.MiddleLeft, false);
            FillPad(ph.rectTransform, 8f, 4f);
            ph.fontStyle = FontStyle.Italic;
            ph.text = placeholder;

            input.textComponent = textComp;
            input.placeholder = ph;
            input.text = "";
            input.lineType = InputField.LineType.SingleLine;
            input.caretWidth = 2;
            if (onChanged != null)
                input.onValueChanged.AddListener((UnityAction<string>)(Action<string>)onChanged);
            return input;
        }

        private static void FillPad(RectTransform rt, float padX, float padY)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }

        private (ScrollRect, RectTransform) MakeScroll(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject scrollGo = Child(parent, "Scroll");
            RectTransform srt = scrollGo.AddComponent<RectTransform>();
            TopLeft(srt, pos, size);
            scrollGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            ScrollRect sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24f;

            GameObject vp = Child(scrollGo.transform, "Viewport");
            RectTransform vrt = vp.AddComponent<RectTransform>();
            Fill(vrt);
            vrt.pivot = new Vector2(0f, 1f);
            vp.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.012f);
            Mask mask = vp.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject content = Child(vp.transform, "Content");
            RectTransform crt = content.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = Vector2.zero;
            crt.anchoredPosition = Vector2.zero;

            sr.viewport = vrt;
            sr.content = crt;
            return (sr, crt);
        }

        private static void Clear(RectTransform content)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
        }

        private static UnityAction UA(Action a) => (UnityAction)a;

        // -------------------------------------------------------------------------------------------------
        //  Lifecycle / input lock
        // -------------------------------------------------------------------------------------------------

        private void Teardown()
        {
            if (_progressLoop != null)
            {
                try { MelonCoroutines.Stop(_progressLoop); } catch { /* already stopped */ }
                _progressLoop = null;
            }
            _progressMode = false;
            _progStarted = _progRemaining = _progStatus = null;
            _progBarFillRt = null;
            _stopButtonGo = null;

            _productRowBg.Clear();
            _shelfRowBg.Clear();
            _drugButtons.Clear();
            _productContent = _shelfContent = null;
            _productScroll = _shelfScroll = null;
            _selIcon = null;
            _selName = _quantityLabel = _chainLabel = _statusLabel = null;
            _listedBtnBg = null;
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        private static void LockInput()
        {
            try
            {
                PlayerCamera cam = PlayerSingleton<PlayerCamera>.Instance;
                if (cam != null)
                {
                    cam.AddActiveUIElement(UiElementName);
                    cam.SetCanLook(false);
                    cam.FreeMouse();
                }
                PlayerMovement mv = PlayerSingleton<PlayerMovement>.Instance;
                if (mv != null)
                    mv.CanMove = false;
                PlayerInventory inv = PlayerSingleton<PlayerInventory>.Instance;
                if (inv != null)
                    inv.SetInventoryEnabled(false);
            }
            catch (Exception e) { Log.Warning($"[ChemistOrders] LockInput failed: {e.Message}"); }
        }

        private static void UnlockInput()
        {
            try
            {
                PlayerCamera cam = PlayerSingleton<PlayerCamera>.Instance;
                if (cam != null)
                {
                    cam.RemoveActiveUIElement(UiElementName);
                    if (cam.activeUIElementCount == 0)
                    {
                        cam.SetCanLook(true);
                        cam.LockMouse();
                    }
                }
                PlayerMovement mv = PlayerSingleton<PlayerMovement>.Instance;
                if (mv != null)
                    mv.CanMove = true;
                PlayerInventory inv = PlayerSingleton<PlayerInventory>.Instance;
                if (inv != null)
                    inv.SetInventoryEnabled(true);
            }
            catch (Exception e) { Log.Warning($"[ChemistOrders] UnlockInput failed: {e.Message}"); }
        }

        private static Font ResolveFont()
        {
            Font font = PhoneAppFont.Resolve();
            if (font != null)
                return font;
            try
            {
                Il2CppArrayBase<Font> fonts = Resources.FindObjectsOfTypeAll<Font>();
                if (fonts != null && fonts.Count > 0)
                    return fonts[0];
            }
            catch { /* ignore */ }
            return null;
        }

        private static string ItemName(string id)
        {
            try
            {
                ItemDefinition def = Registry.GetItem(id);
                return def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : id;
            }
            catch { return id; }
        }

        private static string DisplayName(OrderHistoryEntry h) =>
            string.IsNullOrEmpty(h.TargetName) ? h.TargetProductId : h.TargetName;

        private static string Trim(string s, int max) =>
            s != null && s.Length > max ? s.Substring(0, max - 1) + "…" : s;

        private static string SafeName(Chemist chemist)
        {
            try { return chemist.fullName; }
            catch { return "Chemist"; }
        }
    }
}
