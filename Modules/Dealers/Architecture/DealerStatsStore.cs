using System.Collections.Generic;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Dealers.Architecture
{
    internal sealed class DealerWeeklyStats
    {
        public int WeekIndex;
        public Dictionary<string, int> CurrentWeek = new();
        public Dictionary<string, int> LastWeek = new();
    }

    internal static class DealerStatsStore
    {
        private static readonly SaveSlotStore<DealerWeeklyStats> Store = new("DealerStats", "dealer sales stats");

        public static void Unload() => Store.Unload();

        public static void Record(string dealerId, string productName, int quantity)
        {
            if (string.IsNullOrEmpty(dealerId) || string.IsNullOrEmpty(productName) || quantity <= 0)
                return;

            DealerWeeklyStats stats = Get(dealerId);
            stats.CurrentWeek[productName] = (stats.CurrentWeek.TryGetValue(productName, out int q) ? q : 0) + quantity;
            Store.Set(dealerId, stats);
        }

        public static Dictionary<string, int> RollWeek(string dealerId, int newWeekIndex)
        {
            DealerWeeklyStats stats = Get(dealerId);
            Dictionary<string, int> sold = new(stats.CurrentWeek);
            stats.LastWeek = sold;
            stats.CurrentWeek = new();
            stats.WeekIndex = newWeekIndex;
            Store.Set(dealerId, stats);
            return sold;
        }

        private static DealerWeeklyStats Get(string dealerId) =>
            Store.TryGet(dealerId, out DealerWeeklyStats s) ? s : new DealerWeeklyStats();
    }
}
