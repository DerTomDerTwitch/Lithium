using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Lithium.Helper;

namespace Lithium.Modules.Customers.Patches
{
    // Direct (in-person) sales go through Customer.GetOfferSuccessChance. Enforce the same effect
    // requirement contracts use: if the customer wants effects and none of the offered products carry
    // any of them, the offer is rejected outright (0% chance) — no quality/price fallback.
    [HarmonyPatch(typeof(Customer), nameof(Customer.GetOfferSuccessChance))]
    public class CustomerOfferSuccessPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Customer __instance, Il2CppSystem.Collections.Generic.List<ItemInstance> items, float askingPrice, ref float __result)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.DirectSales.RequireEffectMatch)
                return true;

            List<string> desires = __instance.CustomerData.PreferredProperties
                .ToList()
                .Select(p => p.Name)
                .ToList();
            if (desires.Count == 0)
                return true;

            bool anyMatch = false;
            foreach (ItemInstance item in items)
            {
                ProductDefinition product = item.Definition.TryCast<ProductDefinition>();
                if (product != null && ProductHelper.ProductMatchesDesires(product, desires))
                {
                    anyMatch = true;
                    break;
                }
            }

            if (!anyMatch)
            {
                __result = 0f;
                return false;
            }

            return true;
        }
    }
}
