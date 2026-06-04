using Il2CppScheduleOne.GameTime;
using Lithium.Helper;
using Lithium.Util;

namespace Lithium.Modules.Customers.Architecture
{
    public enum OrderPatternArchetype
    {
        EveryThreeDays,
        TwiceWeekly,
        Weekly
    }

    public class OrderPatternProfile
    {
        public OrderPatternArchetype Archetype { get; private set; }

        public List<EDay> OrderDays { get; private set; }

        public float QuantityMultiplier { get; private set; }

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

        public float IntervalFractionElapsed(float weekPosition)
        {
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
                    return [(EDay)rng.Next(0, 7)];

                case OrderPatternArchetype.TwiceWeekly:
                {
                    int d0 = rng.Next(0, 7);
                    int d1 = (d0 + 3 + rng.Next(0, 2)) % 7;
                    return Distinct(d0, d1);
                }

                case OrderPatternArchetype.EveryThreeDays:
                default:
                {
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
