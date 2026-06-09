namespace Lithium.Modules.Customers.Architecture
{
    internal static class OfferDeadlineTracker
    {
        private static readonly SaveSlotStore<int> Store = new("OfferDeadlines", "offer deadlines");

        public static void Set(string customerName, int deadlineMinSum)
        {
            if (string.IsNullOrEmpty(customerName))
                return;

            Store.Set(customerName, deadlineMinSum);
        }

        public static bool TryGet(string customerName, out int deadlineMinSum)
        {
            deadlineMinSum = 0;
            return !string.IsNullOrEmpty(customerName) && Store.TryGet(customerName, out deadlineMinSum);
        }

        public static bool Clear(string customerName)
        {
            if (string.IsNullOrEmpty(customerName))
                return false;

            return Store.Remove(customerName);
        }

        public static void Unload() => Store.Unload();
    }
}
