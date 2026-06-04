using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.ContractRejected))]
    public class CustomerContractRejectedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Customer __instance)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled || !config.Contracts.RetryNextDayOnRefusal)
                return;

            if (!InstanceFinder.IsServer)
                return;

            ContractRetryTracker.FlagForRetry(__instance.CustomerData.name);
        }
    }
}
