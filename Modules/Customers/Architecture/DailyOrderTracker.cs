using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.Customers.Architecture
{
    /// <summary>
    /// Host-side, per-save record of two per-day facts about each customer, consumed by the Lithium phone
    /// app's Daily tab:
    /// <list type="number">
    /// <item>which customers had an order shifted into "today" by the sleep catch-up
    /// (<c>CustomerMissedOrderCatchupPatch</c>), so they list even though today is not one of their
    /// pattern days;</item>
    /// <item>which customers have completed (handed over) their order today, so the tab can mark them done.</item>
    /// </list>
    /// Days are stored as absolute <see cref="TimeManager.ElapsedDays"/>, so a stale entry from a previous
    /// day simply fails the "== today" test — no explicit cleanup needed. Written only on the host (both the
    /// catch-up and the handover run server-side); a client tab degrades to live order-day info only.
    /// </summary>
    public static class DailyOrderTracker
    {
        private static readonly SaveSlotStore<int> CompletedDay = new("DailyOrderCompleted", "daily order completions");
        private static readonly SaveSlotStore<int> CaughtUpDay = new("DailyOrderCatchups", "daily order catch-ups");

        private static int Today => TimeManager.Instance != null ? TimeManager.Instance.ElapsedDays : int.MinValue;

        public static void RecordCompletion(string customerName)
        {
            if (!string.IsNullOrEmpty(customerName))
                CompletedDay.Set(customerName, Today);
        }

        public static void RecordCatchUp(string customerName)
        {
            if (!string.IsNullOrEmpty(customerName))
                CaughtUpDay.Set(customerName, Today);
        }

        public static bool CompletedToday(string customerName) =>
            !string.IsNullOrEmpty(customerName) && CompletedDay.TryGet(customerName, out int day) && day == Today;

        public static bool CaughtUpToday(string customerName) =>
            !string.IsNullOrEmpty(customerName) && CaughtUpDay.TryGet(customerName, out int day) && day == Today;

        public static void Unload()
        {
            CompletedDay.Unload();
            CaughtUpDay.Unload();
        }
    }
}
