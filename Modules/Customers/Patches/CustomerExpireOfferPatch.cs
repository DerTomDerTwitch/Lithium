using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    // A contract offer expired without the player responding ("did not go through"). Treat it like a
    // refusal: flag the customer to re-attempt an order the next day.
    [HarmonyPatch(typeof(Customer), nameof(Customer.ExpireOffer))]
    public class CustomerExpireOfferPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Customer __instance)
        {
            // The guard suppressed this expiry (the acceptance window hasn't elapsed) — the offer is still
            // live, so it must not be treated as a refusal/retry.
            if (CustomerExpireOfferGuardPatch.LastCallBlocked)
                return;

            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled || !config.Contracts.RetryNextDayOnRefusal)
                return;

            // Scheduling is server-authoritative; only the server owns the retry bookkeeping.
            if (!InstanceFinder.IsServer)
                return;

            ContractRetryTracker.FlagForRetry(__instance.CustomerData.name);
        }
    }
}
