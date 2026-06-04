using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Dealer), "TradeItemsDone")]
    public class DealerInventoryClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dealer __instance)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Coverage.Enabled || !config.Coverage.NotifyDealerInventoryOnClose)
                return;
            if (!InstanceFinder.IsServer)
                return;

            DealerCoverageNotifier.ReportForDealer(__instance);
        }
    }
}
