using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Product;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(ProductManager), nameof(ProductManager.SetProductListed), new[] { typeof(string), typeof(bool) })]
    public class ProductListingPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (!ShouldRun())
                return;

            ProductCoverageNotifier.EnsureBaseline();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!ShouldRun())
                return;

            ProductCoverageNotifier.ReportChange();

            if (Core.Get<ModCustomers>().Configuration.Coverage.NotifyNoDealerCustomers)
                DealerCoverageNotifier.ReportNoDealerChange();
        }

        private static bool ShouldRun()
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            return config.Enabled && config.Coverage.Enabled && InstanceFinder.IsServer;
        }
    }
}
