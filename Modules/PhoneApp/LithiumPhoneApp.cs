using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
using Lithium.Helper;
using Lithium.Modules.ElectricBill;
using Lithium.Modules.Rent;
using UnityEngine;
using UnityEngine.UI;
using PhoneT = Il2CppScheduleOne.UI.Phone.Phone;

namespace Lithium.Modules.PhoneApp
{
    // The "Lithium" phone app, a read-only dashboard with tabbed pages:
    //  - "Property": per rent-enabled property, the current week's rent status (paid/unpaid, amount, next
    //    due, contact + dead drop) and a live breakdown of the electric bill by appliance.
    //  - "Daily": the customers ordering today (see DailyOrdersPage).
    // Built entirely from code (no AssetBundle); the open/close lifecycle is driven manually rather than
    // via the game's generic App<T> base.
    internal sealed class LithiumPhoneApp
    {
        private const string IconObjectName = "LithiumPhoneIcon";
        private const float Pad = 14f;
        private const float RefreshInterval = 0.5f;

        private static readonly Color Background = new Color32(18, 18, 22, 255);
        private static readonly Color HeaderColor = new(0.70f, 0.80f, 1f);
        private static readonly Color Gray = new(0.72f, 0.72f, 0.74f);
        private static readonly Color Red = new(1f, 0.32f, 0.32f);
        private static readonly Color IconColor = new(0.46f, 0.40f, 0.90f);

        private const string Green = "#3CCB5A";
        private const string RedHex = "#FF5151";
        private const string Dim = "#9AA0A6";

        private static readonly Color TabSelected = new(0.34f, 0.30f, 0.62f, 1f);
        private static readonly Color TabUnselected = new(0.15f, 0.15f, 0.19f, 1f);

        private GameObject _appContainer;
        private GameObject _icon;
        private GameObject _bodyContent;
        private RectTransform _bodyContentRect;
        private Dropdown _dropdown;
        private Font _font;

        private readonly List<GameObject> _pages = new();
        private readonly List<Image> _tabBackgrounds = new();
        private readonly List<Text> _tabLabels = new();
        private int _tabIndex;
        private DailyOrdersPage _daily;

        private bool _isOpen;
        private bool _wasPhoneOpen;
        private float _lastRefresh;
        private float _y;

        private bool _rentEnabled;
        private List<ModRent.RentAppView> _locations = new();
        private int _selectedIndex;

        public bool IsAlive => _appContainer != null;

        // --- Lifecycle ------------------------------------------------------------------------------
        public IEnumerator BuildWhenReady()
        {
            float waited = 0f;
            while ((!PlayerSingleton<HomeScreen>.InstanceExists || !PlayerSingleton<AppsCanvas>.InstanceExists) && waited < 30f)
            {
                yield return new WaitForSeconds(0.2f);
                waited += 0.2f;
            }

            if (!PlayerSingleton<HomeScreen>.InstanceExists || !PlayerSingleton<AppsCanvas>.InstanceExists)
            {
                Log.Warning("[PhoneApp] Phone UI not ready after 30s; app not created.");
                yield break;
            }

            try
            {
                Build();
                Log.Info("[PhoneApp] Lithium app created.");
            }
            catch (Exception e)
            {
                Log.Error($"[PhoneApp] Build failed: {e}");
            }
        }

        private void Build()
        {
            _font = PhoneAppFont.Resolve();

            _appContainer = PhoneAppBuilder.MakePanel("LithiumPhoneApp");
            PhoneAppBuilder.MakeBackground(_appContainer, Background);

            Text header = PhoneAppBuilder.MakeText(_appContainer.transform, _font, "Header", 40, HeaderColor,
                TextAnchor.MiddleCenter, false);
            header.fontStyle = FontStyle.Bold;
            header.text = "Lithium";
            SetRect(header.rectTransform, 0.05f, 0.92f, 0.95f, 0.985f);

            // --- Tab bar + pages ---
            GameObject pageProperty = MakePage("PageProperty");
            GameObject pageDaily = MakePage("PageDaily");
            BuildTabBar(("Property", pageProperty), ("Daily", pageDaily));

            _dropdown = PhoneAppBuilder.CloneDropdown();
            if (_dropdown != null)
            {
                _dropdown.transform.SetParent(pageProperty.transform, false);
                _dropdown.transform.localScale = Vector3.one;
                SetRect(_dropdown.GetComponent<RectTransform>(), 0.05f, 0.775f, 0.95f, 0.845f);
                _dropdown.onValueChanged.AddListener(PhoneAppBuilder.UA(OnDropdownChanged));
            }
            else
            {
                Log.Warning("[PhoneApp] Could not clone a phone dropdown; selector unavailable.");
            }

            CreateBody(pageProperty);

            _daily = new DailyOrdersPage();
            GameObject dailyList = _daily.Build(pageDaily.transform, _font);
            SetRect(dailyList.GetComponent<RectTransform>(), 0.04f, 0.03f, 0.96f, 0.845f);

            SelectTab(0, refresh: false);
            _appContainer.SetActive(false);

            _icon = PhoneAppBuilder.MakeIcon(IconObjectName, "Lithium",
                PhoneAppBuilder.LoadIconOrDefault(IconColor), OnIconClicked);
        }

        // A full-size page root; SelectTab toggles page visibility.
        private GameObject MakePage(string name)
        {
            GameObject page = new(name);
            RectTransform rt = page.AddComponent<RectTransform>();
            rt.SetParent(_appContainer.transform, false);
            page.transform.localScale = Vector3.one;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _pages.Add(page);
            return page;
        }

        private void BuildTabBar(params (string Label, GameObject Page)[] tabs)
        {
            GameObject bar = new("Tabs");
            RectTransform brt = bar.AddComponent<RectTransform>();
            brt.SetParent(_appContainer.transform, false);
            bar.transform.localScale = Vector3.one;
            SetRect(brt, 0.05f, 0.85f, 0.95f, 0.915f);

            float width = 1f / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                Button btn = PhoneAppBuilder.MakeButton(bar.transform, _font, "Tab" + tabs[i].Label,
                    tabs[i].Label, 26, TabUnselected, Color.white, () => OnTabClicked(index),
                    out Image bg, out Text label);
                RectTransform rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i * width, 0f);
                rt.anchorMax = new Vector2((i + 1) * width, 1f);
                rt.offsetMin = new Vector2(i == 0 ? 0f : 3f, 0f);
                rt.offsetMax = new Vector2(i == tabs.Length - 1 ? 0f : -3f, 0f);
                _tabBackgrounds.Add(bg);
                _tabLabels.Add(label);
            }
        }

        private void OnTabClicked(int index)
        {
            if (_tabIndex != index)
                SelectTab(index, refresh: _isOpen);
        }

        private void SelectTab(int index, bool refresh)
        {
            _tabIndex = index;
            for (int i = 0; i < _pages.Count; i++)
                _pages[i].SetActive(i == index);
            for (int i = 0; i < _tabBackgrounds.Count; i++)
            {
                _tabBackgrounds[i].color = i == index ? TabSelected : TabUnselected;
                _tabLabels[i].color = i == index ? Color.white : Gray;
            }

            if (!refresh)
                return;
            if (index == 0)
                RebuildBody(refreshElectric: true);
            else
                _daily.Rebuild();
        }

        private void CreateBody(GameObject page)
        {
            GameObject scroll = new("Body");
            RectTransform srt = scroll.AddComponent<RectTransform>();
            srt.SetParent(page.transform, false);
            scroll.transform.localScale = Vector3.one;
            SetRect(srt, 0.04f, 0.03f, 0.96f, 0.755f);

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
            _bodyContentRect = content.AddComponent<RectTransform>();
            _bodyContentRect.SetParent(viewport.transform, false);
            content.transform.localScale = Vector3.one;
            _bodyContentRect.anchorMin = new Vector2(0f, 1f);
            _bodyContentRect.anchorMax = new Vector2(1f, 1f);
            _bodyContentRect.pivot = new Vector2(0.5f, 1f);
            _bodyContentRect.sizeDelta = Vector2.zero;
            _bodyContent = content;

            ScrollRect sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24f;
            sr.viewport = vrt;
            sr.content = _bodyContentRect;
        }

        // --- Open / close ---------------------------------------------------------------------------
        private void OnIconClicked()
        {
            if (_isOpen)
                Close();
            else
                Open();
        }

        private void Open()
        {
            if (_isOpen || PhoneT.ActiveApp != null || _appContainer == null)
                return;

            RefreshLocations();

            _isOpen = true;
            _appContainer.SetActive(true);
            PhoneT.ActiveApp = _appContainer;
            PlayerSingleton<HomeScreen>.Instance.SetIsOpen(false);
            PlayerSingleton<AppsCanvas>.Instance.SetIsOpen(true);
            PlayerSingleton<PhoneT>.Instance.SetIsHorizontal(false);
            PlayerSingleton<PhoneT>.Instance.SetLookOffsetMultiplier(1f);

            SelectTab(_tabIndex, refresh: true);
        }

        private void Close()
        {
            if (!_isOpen)
                return;

            _isOpen = false;
            if (_appContainer != null)
                _appContainer.SetActive(false);
            if (PhoneT.ActiveApp == _appContainer)
                PhoneT.ActiveApp = null;

            if (PlayerSingleton<HomeScreen>.InstanceExists)
                PlayerSingleton<HomeScreen>.Instance.SetIsOpen(true);
            if (PlayerSingleton<PhoneT>.InstanceExists)
            {
                PlayerSingleton<PhoneT>.Instance.SetIsHorizontal(false);
                PlayerSingleton<PhoneT>.Instance.SetLookOffsetMultiplier(1f);
            }
        }

        // --- Per-frame update (driven from Core.OnUpdate) -------------------------------------------
        public void Update()
        {
            if (_appContainer == null)
                return;

            bool phoneOpen = PlayerSingleton<PhoneT>.InstanceExists && PlayerSingleton<PhoneT>.Instance.IsOpen;

            // Phone closed (e.g. via Esc) while our app was open: reset our state so re-opening is clean.
            if (_isOpen && !phoneOpen)
            {
                Close();
                _wasPhoneOpen = false;
                return;
            }
            _wasPhoneOpen = phoneOpen;

            if (!_isOpen)
                return;

            // Clicking the phone's home button returns to the home screen.
            if (phoneOpen && Input.GetMouseButtonDown(0) && IsHoveringHomeButton())
            {
                Close();
                return;
            }

            if (Time.time - _lastRefresh >= RefreshInterval)
            {
                _lastRefresh = Time.time;
                // The property dashboard shows live electric draw, so it re-renders continuously; the
                // daily list only changes when the in-game day rolls over.
                if (_tabIndex == 0)
                    RebuildBody(refreshElectric: false);
                else if (_daily.NeedsRefresh)
                    _daily.Rebuild();
            }
        }

        private static bool IsHoveringHomeButton()
        {
            GameplayMenu gm = Singleton<GameplayMenu>.InstanceExists ? Singleton<GameplayMenu>.Instance : null;
            if (gm == null || gm.OverlayCamera == null)
                return false;

            RaycastHit hit;
            if (Physics.Raycast(gm.OverlayCamera.ScreenPointToRay(Input.mousePosition), out hit, 2f,
                    1 << LayerMask.NameToLayer("Overlay")))
                return hit.collider != null && hit.collider.gameObject.name == "Button";
            return false;
        }

        // --- Data + dropdown ------------------------------------------------------------------------
        private void RefreshLocations()
        {
            ModRent rent = Core.Get<ModRent>();
            _rentEnabled = rent != null && rent.Configuration.Enabled;
            _locations = rent != null ? rent.GetAppViews() : new List<ModRent.RentAppView>();

            string previous = (_selectedIndex >= 0 && _selectedIndex < _locations.Count)
                ? _locations[_selectedIndex].PropertyName
                : null;

            _selectedIndex = 0;
            if (previous != null)
                for (int i = 0; i < _locations.Count; i++)
                    if (_locations[i].PropertyName == previous)
                    {
                        _selectedIndex = i;
                        break;
                    }

            if (_dropdown == null)
                return;

            Il2CppSystem.Collections.Generic.List<Dropdown.OptionData> options = new();
            foreach (ModRent.RentAppView v in _locations)
                options.Add(new Dropdown.OptionData { text = v.PropertyName, image = ResolveMugshot(v.ContactName) });

            _dropdown.ClearOptions();
            _dropdown.AddOptions(options);
            if (_locations.Count > 0)
            {
                _dropdown.value = _selectedIndex;
                _dropdown.RefreshShownValue();
            }
        }

        // Mugshot of the rent contact NPC for a location, matched by full name. Null if not found.
        private static Sprite ResolveMugshot(string contactName)
        {
            if (string.IsNullOrEmpty(contactName) || contactName == "—")
                return null;
            try
            {
                Il2CppSystem.Collections.Generic.List<NPC> registry = NPCManager.NPCRegistry;
                if (registry == null)
                    return null;
                foreach (NPC npc in registry)
                {
                    if (npc != null && string.Equals(npc.fullName, contactName, StringComparison.OrdinalIgnoreCase))
                        return npc.MugshotSprite;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[PhoneApp] Mugshot lookup failed for '{contactName}': {e.Message}");
            }
            return null;
        }

        private void OnDropdownChanged(int index)
        {
            _selectedIndex = index;
            RebuildBody(refreshElectric: true);
        }

        // --- Rendering ------------------------------------------------------------------------------
        private void RebuildBody(bool refreshElectric)
        {
            if (_bodyContent == null)
                return;

            ClearChildren(_bodyContent.transform);
            _y = -6f;

            ModRent.RentAppView rent = (_selectedIndex >= 0 && _selectedIndex < _locations.Count)
                ? _locations[_selectedIndex]
                : null;

            if (rent == null)
            {
                AddLine(_rentEnabled ? "No rent-enabled properties." : "Rent module is disabled.", 28, Gray, 40f);
                FinishBody();
                return;
            }

            if (rent.LockedOut)
            {
                AddLine($"<b><color={RedHex}>⚠ LOCKED OUT — rent unpaid. Pay to restore access.</color></b>",
                    28, Red, 44f);
                _y -= 4f;
            }

            // --- Rent section ---
            AddLine($"<b>{rent.PropertyName}</b>", 34, Color.white, 46f);

            string firstWeek = rent.FirstWeek ? $"  <color={Dim}>· first week</color>" : "";
            string status = rent.Paid ? $"<color={Green}>PAID</color>" : $"<color={RedHex}>UNPAID</color>";
            AddLine($"Rent: {status}   <color={Dim}>(${rent.WeeklyRent:N0}/wk)</color>{firstWeek}", 28, Color.white, 38f);

            if (!rent.Paid)
            {
                AddLine($"Outstanding: <color={RedHex}>${rent.Owed:N0}</color>", 28, Color.white, 36f);
                if (rent.HasOwedDue)
                {
                    string overdue = rent.DaysOverdue > 0
                        ? $"<color={RedHex}>({rent.DaysOverdue} day{(rent.DaysOverdue == 1 ? "" : "s")} overdue)</color>"
                        : $"<color={Dim}>(today)</color>";
                    AddLine($"Due: {rent.OwedDueWeekday} {overdue}", 28, Color.white, 36f);
                }
            }
            else if (rent.HasNextDue)
            {
                string dayWord = rent.DaysUntilDue == 1 ? "day" : "days";
                AddLine($"Next due: {rent.NextDueWeekday} <color={Dim}>(in {rent.DaysUntilDue} {dayWord})</color>",
                    28, Color.white, 36f);
            }

            AddLine($"<color={Dim}>Contact:</color> {rent.ContactName}", 26, Gray, 34f);
            AddLine($"<color={Dim}>Dead drop:</color> {rent.DeadDropName}", 26, Gray, 34f);

            _y -= 12f;

            // --- Electricity (actual cost so far this week) ---
            AddLine("<b>Electricity — this week</b>", 30, HeaderColor, 42f);

            ModElectricBill bill = Core.Get<ModElectricBill>();
            ModElectricBill.ElectricAppView ev = bill != null
                ? bill.GetAppView(rent.PropertyCode, refreshElectric)
                : null;

            if (ev == null || !ev.ModuleEnabled)
            {
                AddLine("Electric bill module is disabled.", 26, Gray, 34f);
                FinishBody();
                return;
            }

            if (ev.PoweredOff)
                AddLine($"<color={RedHex}>POWER CUT — ${ev.OutstandingBill:N0} unpaid. Pay at the dead drop.</color>", 28, Red, 36f);
            else if (ev.OutstandingBill > 0f)
                AddLine($"Bill due: <color={RedHex}>${ev.OutstandingBill:N0}</color> <color={Dim}>— pay in cash at the dead drop</color>", 28, Color.white, 36f);

            if (ev.Lines.Count == 0)
            {
                AddLine("No metered appliances here.", 24, Gray, 32f);
                FinishBody();
                return;
            }

            // Actual accrued: Source | Qty | Cost (wide cost column so values fit).
            AddRow(true, Cell("Source", 0f, 0.55f, TextAnchor.MiddleLeft),
                Cell("Qty", 0.55f, 0.72f, TextAnchor.MiddleRight),
                Cell("Cost", 0.72f, 1f, TextAnchor.MiddleRight));
            foreach (ModElectricBill.ApplianceLine line in ev.Lines)
                AddRow(false, Cell(line.Name, 0f, 0.55f, TextAnchor.MiddleLeft),
                    Cell(line.Count.ToString(), 0.55f, 0.72f, TextAnchor.MiddleRight),
                    Cell($"${line.AccruedCost:N2}", 0.72f, 1f, TextAnchor.MiddleRight));
            AddRow(true, Cell("Total", 0f, 0.55f, TextAnchor.MiddleLeft),
                Cell("", 0.55f, 0.72f, TextAnchor.MiddleRight),
                Cell($"${ev.TotalAccrued:N2}", 0.72f, 1f, TextAnchor.MiddleRight));

            _y -= 12f;

            // --- Week estimate (projected at the current draw) ---
            AddLine("<b>Week estimate</b>", 28, HeaderColor, 38f);
            AddRow(true, Cell("Source", 0f, 0.50f, TextAnchor.MiddleLeft),
                Cell("Now", 0.50f, 0.74f, TextAnchor.MiddleRight),
                Cell("~$/wk", 0.74f, 1f, TextAnchor.MiddleRight));
            foreach (ModElectricBill.ApplianceLine line in ev.Lines)
                AddRow(false, Cell(line.Name, 0f, 0.50f, TextAnchor.MiddleLeft),
                    Cell($"{line.CurrentWatts:N0}W", 0.50f, 0.74f, TextAnchor.MiddleRight),
                    Cell($"${line.ProjectedCost:N2}", 0.74f, 1f, TextAnchor.MiddleRight));
            AddRow(true, Cell("Total", 0f, 0.50f, TextAnchor.MiddleLeft),
                Cell($"{ev.TotalWatts:N0}W", 0.50f, 0.74f, TextAnchor.MiddleRight),
                Cell($"${ev.TotalProjected:N2}", 0.74f, 1f, TextAnchor.MiddleRight));

            FinishBody();
        }

        private void FinishBody()
        {
            float height = -_y + Pad;
            _bodyContentRect.sizeDelta = new Vector2(_bodyContentRect.sizeDelta.x, height);
        }

        private void AddLine(string text, int fontSize, Color color, float height)
        {
            Text t = PhoneAppBuilder.MakeText(_bodyContent.transform, _font, "Line", fontSize, color,
                TextAnchor.UpperLeft, true);
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-2f * Pad, height);
            rt.anchoredPosition = new Vector2(0f, _y);
            t.text = text;
            _y -= height;
        }

        private readonly struct TableCell
        {
            public readonly string Text;
            public readonly float XMin;
            public readonly float XMax;
            public readonly TextAnchor Anchor;

            public TableCell(string text, float xMin, float xMax, TextAnchor anchor)
            {
                Text = text;
                XMin = xMin;
                XMax = xMax;
                Anchor = anchor;
            }
        }

        private static TableCell Cell(string text, float xMin, float xMax, TextAnchor anchor) =>
            new(text, xMin, xMax, anchor);

        private void AddRow(bool header, params TableCell[] cells)
        {
            const float rowHeight = 32f;
            Color color = header ? HeaderColor : Color.white;
            const int fontSize = 24;

            GameObject row = new("Row");
            RectTransform rrt = row.AddComponent<RectTransform>();
            rrt.SetParent(_bodyContent.transform, false);
            row.transform.localScale = Vector3.one;
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.sizeDelta = new Vector2(-2f * Pad, rowHeight);
            rrt.anchoredPosition = new Vector2(0f, _y);

            foreach (TableCell c in cells)
            {
                float left = c.Anchor == TextAnchor.MiddleLeft ? 6f : 0f;
                float right = c.Anchor == TextAnchor.MiddleRight ? 6f : 0f;
                MakeCell(rrt, c.XMin, c.XMax, c.Text, c.Anchor, fontSize, color, left, right);
            }

            _y -= rowHeight;
        }

        private void MakeCell(Transform row, float xMin, float xMax, string text, TextAnchor anchor,
            int fontSize, Color color, float leftInset, float rightInset)
        {
            Text t = PhoneAppBuilder.MakeText(row, _font, "Cell", fontSize, color, anchor, false);
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.offsetMin = new Vector2(leftInset, 0f);
            rt.offsetMax = new Vector2(-rightInset, 0f);
            t.text = text;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        private static void SetRect(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
