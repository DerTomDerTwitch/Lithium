namespace Lithium.Modules.Customers.Architecture
{
    /// <summary>
    /// Remembers, per customer, the absolute in-game minute (GameDateTime.GetMinSum) at which a pending
    /// contract offer is actually allowed to expire — i.e. the deadline the customer was told about.
    ///
    /// The native offer-expiry check is invisible in the IL2CPP proxy assemblies, so we don't rely on it
    /// honouring our extended window. Instead <see cref="Lithium.Modules.Customers.Patches.CustomerOfferDeadlinePatch"/>
    /// records the deadline when the offer is made and the ExpireOffer guard keeps the deal alive until it
    /// passes, guaranteeing the cancellation never beats the promised time.
    ///
    /// State is persisted per savegame slot to <c>UserData/Lithium/OfferDeadlines/&lt;save&gt;.json</c>
    /// (see <see cref="SaveSlotStore{TValue}"/>) so the extended window survives quitting and reloading:
    /// the stored deadline is an absolute in-game minute, which keeps the same meaning across reloads of
    /// that save. Without this, a save load would drop the deadline and the restored offer would fall back
    /// to the game's (shorter) native expiry — exactly the "promised Friday, cancelled the same day" bug.
    /// </summary>
    internal static class OfferDeadlineTracker
    {
        // customerName -> absolute in-game minute at which the offer may expire.
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

        public static void Clear(string customerName)
        {
            if (string.IsNullOrEmpty(customerName))
                return;

            Store.Remove(customerName);
        }

        /// <summary>Drops in-memory state on save load; see <see cref="SaveSlotStore{TValue}.Unload"/>.</summary>
        public static void Unload() => Store.Unload();
    }
}
