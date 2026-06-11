using HarmonyLib;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI.Phone.ProductManagerApp;

namespace Lithium.Modules.Products.Patches
{
    // Builds the search + effects filter bar onto the Products app and keeps it applied as the list
    // opens or gains newly-discovered entries.
    [HarmonyPatch(typeof(ProductManagerApp))]
    internal static class ProductManagerAppFilterPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ProductManagerApp.Start))]
        private static void StartPostfix(ProductManagerApp __instance)
        {
            ProductListFilter.EnsureBuilt(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ProductManagerApp.SetOpen))]
        private static void SetOpenPostfix(ProductManagerApp __instance, bool open)
        {
            // Hide the bar whenever the app closes so it can't linger over the home screen / other apps.
            if (!open)
            {
                ProductListFilter.SetVisible(false);
                return;
            }
            ProductListFilter.EnsureBuilt(__instance);
            ProductListFilter.ApplyFilter();
            ProductListFilter.SetVisible(true);
        }

        // Late discoveries create a new entry after the bar is built — re-apply so it respects the
        // active filter. No-op while the bar isn't built yet (e.g. the initial Start-time entry storm).
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ProductManagerApp.CreateEntry), new[] { typeof(ProductDefinition) })]
        private static void CreateEntryPostfix()
        {
            ProductListFilter.ApplyFilter();
        }
    }
}
