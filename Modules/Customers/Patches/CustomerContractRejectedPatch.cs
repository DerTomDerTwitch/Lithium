using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    // The player refused a contract offer (declined it in the Messages app). Flag the customer so they
    // re-attempt an order the next day instead of waiting for their next scheduled order day.
    [HarmonyPatch(typeof(Customer), nameof(Customer.ContractRejected))]
    public class CustomerContractRejectedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Customer __instance)
        {
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
