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
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            if (!InstanceFinder.IsServer)
                return;

            string name = __instance.CustomerData?.name;
            if (string.IsNullOrEmpty(name))
                return;

            OfferDeadlineTracker.Clear(name);

            if (config.Contracts.RetryNextDayOnRefusal)
                ContractRetryTracker.FlagForRetry(name);
        }
    }
}
