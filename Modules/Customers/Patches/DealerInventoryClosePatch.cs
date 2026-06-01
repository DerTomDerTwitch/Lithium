using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;

namespace Lithium.Modules.Customers.Patches
{
    // When the player closes a dealer's in-person inventory, text (via the Lithium contact) which of that
    // dealer's assigned customers / desired effects their stock fails to cover. TradeItemsDone is the
    // dealer's "finished trading" callback, so the inventory is already updated by the time it runs.
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
