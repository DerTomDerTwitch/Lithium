using Il2CppScheduleOne.GameTime;
using Lithium.Helper;
using Lithium.Util;

namespace Lithium.Modules.Customers.Architecture
{
    public enum OrderPatternArchetype
    {
        // Ordered from the most frequent cadence to the least. There is deliberately no daily cadence:
        // every customer orders at most a few times a week, so they place fewer, larger (bulk) orders.
        EveryThreeDays,
        TwiceWeekly,
        Weekly
    }

    /// <summary>
    /// A customer's weekly ordering pattern, derived deterministically from their name. Both the
    /// GetOrderDays patch (frequency) and the contract-generation patch (quantity) build this
    /// independently from the same inputs and must agree, so <see cref="Create"/> is deterministic: it
    /// consumes the seeded RNG in a fixed order and, beyond its arguments, only reads the configured
    /// archetype weights — which are stable within a session, so both call sites still produce the same
    /// pattern for a given name.
    /// </summary>
    public class OrderPatternProfile
    {
        public OrderPatternArchetype Archetype { get; private set; }

        /// <summary>Days of the Mon–Sun week this customer places an order. Always non-empty.</summary>
        public List<EDay> OrderDays { get; private set; }

        /// <summary>
        /// Per-order quantity scale that conserves weekly volume: the game's intended weekly order
        /// count spread across this pattern's order days. Fewer days ⇒ bigger orders (bulk),
        /// more days ⇒ smaller orders.
        /// </summary>
        public float QuantityMultiplier { get; private set; }

        /// <summary>
        /// Whole days from <paramref name="today"/> until this customer's next order day (1–7). If
        /// today is their only order day, returns 7 (a week from now) rather than 0 — they've just
        /// ordered, so the next occurrence is the following week.
        /// </summary>
        public int DaysUntilNextOrder(EDay today)
        {
            int from = (int)today;
            int best = 7;
            foreach (EDay day in OrderDays)
            {
                int delta = (((int)day - from) % 7 + 7) % 7;
                if (delta == 0)
                    delta = 7;
                if (delta < best)
                    best = delta;
            }
            return best;
        }

        /// <summary>
        /// Fraction (0..1) of the customer's current inter-order interval that has elapsed at the given
        /// continuous week position (<c>(int)day + fractionOfDay</c>, in [0,7)). 0 means they've effectively
        /// just placed an order (start of the wait for the next one); approaching 1 means their next
        /// scheduled order is imminent. Used to gate off-schedule in-person offers — a customer that just
        /// took a bulk order shouldn't keep buying extra product the same day.
        /// </summary>
        public float IntervalFractionElapsed(float weekPosition)
        {
            // Project every order day across the previous, current and next week so the interval bracketing
            // weekPosition is found regardless of where the week boundary falls.
            float prev = float.NegativeInfinity;
            float next = float.PositiveInfinity;
            foreach (EDay day in OrderDays)
            {
                foreach (float mark in new[] { (int)day - 7, (int)day, (int)day + 7 })
                {
                    if (mark <= weekPosition && mark > prev)
                        prev = mark;
                    if (mark > weekPosition && mark < next)
                        next = mark;
                }
            }

            float length = next - prev;
            if (length <= 0f || float.IsInfinity(length))
                return 1f;

            float fraction = (weekPosition - prev) / length;
            return fraction < 0f ? 0f : fraction > 1f ? 1f : fraction;
        }

        // A customer's profile is deterministic within a session (it depends only on the name plus config
        // that's loaded once at startup), yet several patches rebuild it per customer action. Cache by name
        // so the work happens once and every call site sees the identical instance. Cleared on save unload
        // via ModCustomers, since the cache spans the process while the patches' inputs are per-save.
        private static readonly Dictionary<string, OrderPatternProfile> _cache = new();

        public static void ClearCache() => _cache.Clear();

        public static OrderPatternProfile Create(string customerName, int minOrdersPerWeek, int maxOrdersPerWeek)
        {
            string cacheKey = customerName ?? string.Empty;
            if (_cache.TryGetValue(cacheKey, out OrderPatternProfile cached))
                return cached;

            int seed = StableHash.Compute(customerName);
            Random rng = new Random(seed);

            int referenceOrdersPerWeek = Math.Clamp((int)Math.Round((minOrdersPerWeek + maxOrdersPerWeek) / 2.0), 1, 7);

            OrderPatternArchetype archetype = PickArchetype(rng);
            List<EDay> orderDays = BuildDays(archetype, rng);

            if (orderDays.Count == 0)
                orderDays.Add((EDay)(((seed % 7) + 7) % 7));

            // Player-balancing scale on bulk size. Reads config (stable within a session), so every call
            // site that rebuilds this profile for a given name still agrees on the multiplier. Clamped at
            // 0 so a negative factor can't invert order sizes.
            float sizeFactor = Math.Max(0f, Core.Get<ModCustomers>().Configuration.OrderPatterns.BulkOrderSizeFactor);

            OrderPatternProfile profile = new OrderPatternProfile
            {
                Archetype = archetype,
                OrderDays = orderDays,
                QuantityMultiplier = referenceOrdersPerWeek / (float)orderDays.Count * sizeFactor
            };
            _cache[cacheKey] = profile;
            return profile;
        }

        private static OrderPatternArchetype PickArchetype(Random rng)
        {
            OrderPatternWeights weights = Core.Get<ModCustomers>().Configuration.OrderPatterns.ArchetypeWeights;

            // Fixed add order so the pick stays deterministic for a given seed (both the GetOrderDays and
            // contract-generation call sites must agree). Weights come from config but are stable within a
            // session, and Pick() consumes exactly one RNG draw regardless of them, so the two sites stay
            // in sync. Negative weights are clamped to 0.
            WeightedPicker<OrderPatternArchetype> picker = new WeightedPicker<OrderPatternArchetype>(rng);
            picker.Add(OrderPatternArchetype.Weekly, Math.Max(0f, weights.Weekly));
            picker.Add(OrderPatternArchetype.TwiceWeekly, Math.Max(0f, weights.TwiceWeekly));
            picker.Add(OrderPatternArchetype.EveryThreeDays, Math.Max(0f, weights.EveryThreeDays));
            return picker.Pick();
        }

        private static List<EDay> BuildDays(OrderPatternArchetype archetype, Random rng)
        {
            switch (archetype)
            {
                case OrderPatternArchetype.Weekly:
                    // One order day per week — the largest interval and biggest single (bulk) order.
                    return [(EDay)rng.Next(0, 7)];

                case OrderPatternArchetype.TwiceWeekly:
                {
                    // Two well-separated days, ~3–4 days apart.
                    int d0 = rng.Next(0, 7);
                    int d1 = (d0 + 3 + rng.Next(0, 2)) % 7;
                    return Distinct(d0, d1);
                }

                case OrderPatternArchetype.EveryThreeDays:
                default:
                {
                    // Roughly every third day within the week: 2–3 evenly spaced days. Starting in
                    // 0..2 guarantees at least two order days (start 0 → Mon/Thu/Sun, 1 → Tue/Fri,
                    // 2 → Wed/Sat), so this never collapses into a weekly pattern.
                    int start = rng.Next(0, 3);
                    List<EDay> days = [];
                    for (int d = start; d < 7; d += 3)
                        days.Add((EDay)d);
                    return days;
                }
            }
        }

        private static List<EDay> Distinct(int a, int b)
        {
            List<int> set = a == b ? [a] : [a, b];
            set.Sort();
            return [.. set.Select(d => (EDay)d)];
        }
    }
}
