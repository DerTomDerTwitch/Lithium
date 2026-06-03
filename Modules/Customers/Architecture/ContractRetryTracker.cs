using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.Customers.Architecture
{
    /// <summary>
    /// Remembers customers whose contract offer the player refused, or that expired unanswered, so they
    /// re-attempt an order the next day instead of waiting for their next scheduled order day.
    ///
    /// State is persisted per savegame slot to <c>UserData/Lithium/ContractRetries/&lt;save&gt;.json</c>
    /// (see <see cref="SaveSlotStore{TValue}"/>) so outstanding retries survive quitting and reloading.
    /// </summary>
    public static class ContractRetryTracker
    {
        // customerName -> the weekday the customer should re-attempt on (the day after the refusal/expiry).
        // A weekday (rather than an absolute date) is stored because the order schedule the game consults,
        // GetOrderDays, is itself weekday-based; the day is captured once at refusal time so it stays put.
        private static readonly SaveSlotStore<EDay> Store = new("ContractRetries", "contract retries");

        public static void FlagForRetry(string customerName)
        {
            if (string.IsNullOrEmpty(customerName))
                return;

            int next = ((int)TimeManager.Instance.CurrentDay + 1) % 7;
            Store.Set(customerName, (EDay)next);
        }

        /// <summary>True if this customer owes a retry and today is the day to make it.</summary>
        public static bool IsRetryDay(string customerName) =>
            Store.TryGet(customerName, out EDay day) && day == TimeManager.Instance.CurrentDay;

        public static bool HasPendingRetry(string customerName, out EDay retryDay) =>
            Store.TryGet(customerName, out retryDay);

        public static void Clear(string customerName) => Store.Remove(customerName);

        /// <summary>Drops in-memory state on save load; see <see cref="SaveSlotStore{TValue}.Unload"/>.</summary>
        public static void Unload() => Store.Unload();
    }
}
