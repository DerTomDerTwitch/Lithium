using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Product;

namespace Lithium.Modules.Customers.Patches
{
    // When the player lists or delists a product, recompute customer coverage and text the player (via
    // the Lithium contact) which customers became covered / uncovered, plus the overall percentage.
    // Prefix snapshots coverage before the change; postfix diffs against the post-change state.
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
