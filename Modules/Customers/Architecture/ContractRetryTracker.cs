using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.Customers.Architecture
{
    public static class ContractRetryTracker
    {
        private static readonly SaveSlotStore<EDay> Store = new("ContractRetries", "contract retries");

        public static void FlagForRetry(string customerName)
        {
            if (string.IsNullOrEmpty(customerName))
                return;

            int next = ((int)TimeManager.Instance.CurrentDay + 1) % 7;
            Store.Set(customerName, (EDay)next);
        }

        public static bool IsRetryDay(string customerName) =>
            Store.TryGet(customerName, out EDay day) && day == TimeManager.Instance.CurrentDay;

        public static bool HasPendingRetry(string customerName, out EDay retryDay) =>
            Store.TryGet(customerName, out retryDay);

        public static void Clear(string customerName) => Store.Remove(customerName);

        public static void Unload() => Store.Unload();
    }
}
