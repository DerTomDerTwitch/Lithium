using Il2CppScheduleOne.GameTime;
using Lithium.Helper;
using Lithium.Util;

namespace Lithium.Modules.Customers.Architecture
{
    public enum OrderPatternArchetype
    {
        DailySmall,
        EveryTwoDays,
        Irregular,
        BiWeekly,
        WeeklyBulk
    }

    /// <summary>
    /// A customer's weekly ordering pattern, derived deterministically from their name. Both the
    /// GetOrderDays patch (frequency) and the contract-generation patch (quantity) build this
    /// independently from the same inputs and must agree, so <see cref="Create"/> is pure: it reads
    /// nothing but its arguments and consumes the seeded RNG in a fixed order.
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

        public static OrderPatternProfile Create(string customerName, int minOrdersPerWeek, int maxOrdersPerWeek)
        {
            int seed = StableHash.Compute(customerName);
            Random rng = new Random(seed);

            int referenceOrdersPerWeek = Math.Clamp((int)Math.Round((minOrdersPerWeek + maxOrdersPerWeek) / 2.0), 1, 7);

            OrderPatternArchetype archetype = PickArchetype(rng);
            List<EDay> orderDays = BuildDays(archetype, rng);

            if (orderDays.Count == 0)
                orderDays.Add((EDay)(((seed % 7) + 7) % 7));

            return new OrderPatternProfile
            {
                Archetype = archetype,
                OrderDays = orderDays,
                QuantityMultiplier = referenceOrdersPerWeek / (float)orderDays.Count
            };
        }

        private static OrderPatternArchetype PickArchetype(Random rng)
        {
            WeightedPicker<OrderPatternArchetype> picker = new WeightedPicker<OrderPatternArchetype>(rng);
            picker.Add(OrderPatternArchetype.DailySmall, 30f);
            picker.Add(OrderPatternArchetype.EveryTwoDays, 25f);
            picker.Add(OrderPatternArchetype.Irregular, 20f);
            picker.Add(OrderPatternArchetype.BiWeekly, 10f);
            picker.Add(OrderPatternArchetype.WeeklyBulk, 15f);
            return picker.Pick();
        }

        private static List<EDay> BuildDays(OrderPatternArchetype archetype, Random rng)
        {
            switch (archetype)
            {
                case OrderPatternArchetype.WeeklyBulk:
                    // One big order day per week, then nothing.
                    return [(EDay)rng.Next(0, 7)];

                case OrderPatternArchetype.BiWeekly:
                {
                    // Two well-separated days.
                    int d0 = rng.Next(0, 7);
                    int d1 = (d0 + 3 + rng.Next(0, 2)) % 7;
                    return Distinct(d0, d1);
                }

                case OrderPatternArchetype.EveryTwoDays:
                {
                    // Roughly every other day (3–4 days/week).
                    int start = rng.Next(0, 2);
                    List<EDay> days = [];
                    for (int d = start; d < 7; d += 2)
                        days.Add((EDay)d);
                    return days;
                }

                case OrderPatternArchetype.Irregular:
                {
                    // 2–4 arbitrary, unevenly spaced fixed days.
                    int count = 2 + rng.Next(0, 3);
                    return SampleDistinct(count, rng);
                }

                case OrderPatternArchetype.DailySmall:
                default:
                {
                    // Most days (6–7), small quantities.
                    int count = 6 + rng.Next(0, 2);
                    if (count >= 7)
                        return [.. Enumerable.Range(0, 7).Select(d => (EDay)d)];
                    return SampleDistinct(count, rng);
                }
            }
        }

        private static List<EDay> Distinct(int a, int b)
        {
            List<int> set = a == b ? [a] : [a, b];
            set.Sort();
            return [.. set.Select(d => (EDay)d)];
        }

        private static List<EDay> SampleDistinct(int count, Random rng)
        {
            // Partial Fisher–Yates over Mon..Sun, then sort for a stable day order.
            List<int> pool = [.. Enumerable.Range(0, 7)];
            count = Math.Clamp(count, 1, 7);
            for (int i = 0; i < count; i++)
            {
                int j = i + rng.Next(0, pool.Count - i);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            List<int> chosen = pool.GetRange(0, count);
            chosen.Sort();
            return [.. chosen.Select(d => (EDay)d)];
        }
    }
}
