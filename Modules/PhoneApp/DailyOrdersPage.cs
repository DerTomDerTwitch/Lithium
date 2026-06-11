using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI.Phone.Messages;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;
using UnityEngine.UI;

namespace Lithium.Modules.PhoneApp
{
    // The "Daily" tab of the Lithium phone app: lists every unlocked customer whose order days include
    // the current day. Each row shows the customer's mugshot, name and 1-3 stars merging two facts:
    // star COUNT is the expected per-order size (frequent orderers buy small, weekly orderers buy big —
    // per-order quantity scales inversely with order days per week), star COLOUR is the desired quality
    // (the game's customer-standards star + ItemQuality palette). Order days come from
    // CustomerData.GetOrderDays, which routes through CustomerGetOrderDaysPatch — so order-pattern
    // profiles and next-day retries are reflected automatically when the Customers module is active.
    internal sealed class DailyOrdersPage
    {
        private const float Pad = 14f;
        private const float RowHeight = 60f;
        private const float RowStep = 66f;
        private const float MugshotSize = 48f;
        private const float StarSize = 28f;
        private const float StarStep = 32f;

        private static readonly Color RowColor = new(0.13f, 0.13f, 0.16f, 0.85f);
        private static readonly Color HeaderColor = new(0.70f, 0.80f, 1f);
        private static readonly Color Gray = new(0.72f, 0.72f, 0.74f);

        // Completed rows are rendered faded; shifted (catch-up) rows get a small inline tag.
        private const float DoneAlpha = 0.42f;
        private const string DoneCheckHex = "#82E07A";   // green tick before a completed name
        private const string ShiftedTagHex = "#9FB7E0";  // muted blue "(shifted)" suffix

        private sealed class Row
        {
            public string Name;
            public Sprite Mugshot;
            public EQuality Quality;
            public int QuantityLevel; // 1 = low (orders often), 2 = mid, 3 = high (weekly bulk)
            public int OrderTime;     // 24h time, for chronological sorting
            public bool Done;         // already completed their order today
            public bool Shifted;      // pulled into today by the sleep catch-up (not a normal order day)
        }

        private GameObject _content;
        private RectTransform _contentRect;
        private Font _font;
        private Sprite _starSprite;
        private EDay _builtDay;
        private bool _built;
        private float _y;

        // True once the in-game day has rolled past the day the list was built for.
        public bool NeedsRefresh
        {
            get
            {
                if (!_built || TimeManager.Instance == null)
                    return false;
                return TimeManager.Instance.CurrentDay != _builtDay;
            }
        }

        // Creates the scroll list under the given page root; the caller positions the returned root rect.
        public GameObject Build(Transform parent, Font font)
        {
            _font = font;

            GameObject scroll = new("DailyList");
            RectTransform srt = scroll.AddComponent<RectTransform>();
            srt.SetParent(parent, false);
            scroll.transform.localScale = Vector3.one;

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
            _contentRect = content.AddComponent<RectTransform>();
            _contentRect.SetParent(viewport.transform, false);
            content.transform.localScale = Vector3.one;
            _contentRect.anchorMin = new Vector2(0f, 1f);
            _contentRect.anchorMax = new Vector2(1f, 1f);
            _contentRect.pivot = new Vector2(0.5f, 1f);
            _contentRect.sizeDelta = Vector2.zero;
            _content = content;

            ScrollRect sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 24f;
            sr.viewport = vrt;
            sr.content = _contentRect;
            return scroll;
        }

        public void Rebuild()
        {
            if (_content == null)
                return;

            for (int i = _content.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.transform.GetChild(i).gameObject);
            _y = -6f;

            try
            {
                EDay today = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : default;
                _builtDay = today;
                _built = true;

                List<Row> rows = GatherToday(today);

                int doneCount = rows.Count(r => r.Done);
                string doneSuffix = doneCount > 0 ? $" · {doneCount} done" : string.Empty;
                AddLine($"<b>Ordering today ({rows.Count}) — {today}{doneSuffix}</b>", 30, HeaderColor, 40f);
                AddLine("Stars: count = order size, colour = desired quality", 20, Gray, 28f);
                _y -= 6f;

                if (rows.Count == 0)
                    AddLine("No customers will order today.", 26, Gray, 36f);
                else
                    foreach (Row row in rows)
                        AddRow(row);
            }
            catch (Exception e)
            {
                Log.Error($"[PhoneApp] Daily list rebuild failed: {e}");
                AddLine("Could not read customer data.", 26, Gray, 36f);
            }

            _contentRect.sizeDelta = new Vector2(_contentRect.sizeDelta.x, -_y + Pad);
        }

        // --- Data -------------------------------------------------------------------------------------
        private static List<Row> GatherToday(EDay today)
        {
            List<Row> rows = [];
            foreach (Customer customer in Customer.UnlockedCustomers.ToList())
            {
                if (customer == null || customer.CustomerData == null || customer.NPC == null)
                    continue;

                CustomerData data = customer.CustomerData;
                string key = data.name;
                float relation = customer.NPC.RelationData != null
                    ? customer.NPC.RelationData.RelationDelta / 5f
                    : 0f;

                // Same arguments the game uses in Customer.IsDealTime for unlocked customers.
                List<EDay> days = data.GetOrderDays(customer.CurrentAddiction, relation).ToList();

                bool normalToday = days.Contains(today);
                bool caughtUp = DailyOrderTracker.CaughtUpToday(key);   // order shifted in by the sleep catch-up
                bool done = DailyOrderTracker.CompletedToday(key);      // already served today

                // List a customer if they normally order today, were shifted into today, or already
                // completed today's order (so the served row still shows, faded).
                if (!normalToday && !caughtUp && !done)
                    continue;

                rows.Add(new Row
                {
                    Name = customer.NPC.fullName,
                    Mugshot = customer.NPC.MugshotSprite,
                    Quality = CorrespondingQuality(data.Standards),
                    QuantityLevel = days.Count >= 3 ? 1 : days.Count == 2 ? 2 : 3,
                    OrderTime = data.OrderTime,
                    Done = done,
                    Shifted = caughtUp && !normalToday
                });
            }

            // Completed orders sink to the bottom; the rest stay in chronological order-time order.
            rows.Sort((a, b) =>
            {
                if (a.Done != b.Done)
                    return a.Done ? 1 : -1;
                if (a.OrderTime != b.OrderTime)
                    return a.OrderTime.CompareTo(b.OrderTime);
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return rows;
        }

        // Mirrors StandardsMethod.GetCorrespondingQuality (game source).
        private static EQuality CorrespondingQuality(ECustomerStandard standards) => standards switch
        {
            ECustomerStandard.VeryLow => EQuality.Trash,
            ECustomerStandard.Low => EQuality.Poor,
            ECustomerStandard.Moderate => EQuality.Standard,
            ECustomerStandard.High => EQuality.Premium,
            ECustomerStandard.VeryHigh => EQuality.Heavenly,
            _ => EQuality.Standard
        };

        // Mirrors ItemQuality's quality colours (game source).
        private static Color QualityColor(EQuality quality) => quality switch
        {
            EQuality.Trash => new Color32(125, 50, 50, 255),
            EQuality.Poor => new Color32(80, 145, 50, 255),
            EQuality.Standard => new Color32(100, 190, 255, 255),
            EQuality.Premium => new Color32(225, 75, 255, 255),
            EQuality.Heavenly => new Color32(255, 200, 50, 255),
            _ => Color.white
        };

        private static string QualityHex(EQuality quality) =>
            "#" + ColorUtility.ToHtmlStringRGB(QualityColor(quality));

        // The star sprite the game itself uses for customer standards (MessagesApp). Null-safe; rows fall
        // back to a text star when it can't be resolved.
        private Sprite ResolveStarSprite()
        {
            if (_starSprite != null)
                return _starSprite;
            try
            {
                if (PlayerSingleton<MessagesApp>.InstanceExists)
                {
                    Image star = PlayerSingleton<MessagesApp>.Instance.standardsStar;
                    if (star != null)
                        _starSprite = star.sprite;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[PhoneApp] Could not resolve standards star sprite: {e.Message}");
            }
            return _starSprite;
        }

        // --- Rendering ----------------------------------------------------------------------------------
        private void AddRow(Row row)
        {
            GameObject go = new("CustomerRow");
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(_content.transform, false);
            go.transform.localScale = Vector3.one;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-2f * Pad, RowHeight);
            rt.anchoredPosition = new Vector2(0f, _y);

            float alpha = row.Done ? DoneAlpha : 1f;
            go.AddComponent<Image>().color = new Color(RowColor.r, RowColor.g, RowColor.b, RowColor.a * alpha);

            if (row.Mugshot != null)
            {
                GameObject mug = new("Mugshot");
                RectTransform mrt = mug.AddComponent<RectTransform>();
                mrt.SetParent(go.transform, false);
                mug.transform.localScale = Vector3.one;
                mrt.anchorMin = new Vector2(0f, 0.5f);
                mrt.anchorMax = new Vector2(0f, 0.5f);
                mrt.pivot = new Vector2(0f, 0.5f);
                mrt.sizeDelta = new Vector2(MugshotSize, MugshotSize);
                mrt.anchoredPosition = new Vector2(6f, 0f);
                Image img = mug.AddComponent<Image>();
                img.sprite = row.Mugshot;
                img.preserveAspect = true;
                img.color = new Color(1f, 1f, 1f, alpha);
            }

            Text name = PhoneAppBuilder.MakeText(go.transform, _font, "Name", 26, Color.white,
                TextAnchor.MiddleLeft, false);
            RectTransform nrt = name.rectTransform;
            nrt.anchorMin = new Vector2(0f, 0f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.offsetMin = new Vector2(MugshotSize + 16f, 0f);
            nrt.offsetMax = new Vector2(-(3f * StarStep + 10f), 0f);
            name.color = new Color(1f, 1f, 1f, alpha);
            if (row.Done)
                name.text = $"<color={DoneCheckHex}>✓</color> {row.Name}";       // green tick = served
            else if (row.Shifted)
                name.text = $"{row.Name}  <size=18><color={ShiftedTagHex}>(shifted)</color></size>";
            else
                name.text = row.Name;

            Sprite star = ResolveStarSprite();
            if (star != null)
            {
                for (int i = 0; i < row.QuantityLevel; i++)
                {
                    GameObject s = new("Star");
                    RectTransform srt = s.AddComponent<RectTransform>();
                    srt.SetParent(go.transform, false);
                    s.transform.localScale = Vector3.one;
                    srt.anchorMin = new Vector2(1f, 0.5f);
                    srt.anchorMax = new Vector2(1f, 0.5f);
                    srt.pivot = new Vector2(1f, 0.5f);
                    srt.sizeDelta = new Vector2(StarSize, StarSize);
                    srt.anchoredPosition = new Vector2(-6f - i * StarStep, 0f);
                    Image img = s.AddComponent<Image>();
                    img.sprite = star;
                    img.preserveAspect = true;
                    Color qc = QualityColor(row.Quality);
                    img.color = new Color(qc.r, qc.g, qc.b, alpha);
                }
            }
            else
            {
                Text stars = PhoneAppBuilder.MakeText(go.transform, _font, "Stars", 30, Color.white,
                    TextAnchor.MiddleRight, false);
                RectTransform srt = stars.rectTransform;
                srt.anchorMin = new Vector2(0.6f, 0f);
                srt.anchorMax = new Vector2(1f, 1f);
                srt.offsetMin = Vector2.zero;
                srt.offsetMax = new Vector2(-6f, 0f);
                stars.color = new Color(1f, 1f, 1f, alpha);
                stars.text = $"<color={QualityHex(row.Quality)}>{new string('★', row.QuantityLevel)}</color>";
            }

            _y -= RowStep;
        }

        private void AddLine(string text, int fontSize, Color color, float height)
        {
            Text t = PhoneAppBuilder.MakeText(_content.transform, _font, "Line", fontSize, color,
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
    }
}
