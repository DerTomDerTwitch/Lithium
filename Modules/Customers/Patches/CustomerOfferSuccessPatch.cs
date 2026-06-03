using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    // Direct (in-person) sales go through Customer.GetOfferSuccessChance. Two gates:
    //   1. Off-schedule timing — a bulk-pattern customer refuses an unsolicited offer until enough of the
    //      wait for their next scheduled order has passed (OfferTimingGate), so the player can't sell them
    //      extra product every day and bypass the bulk cadence.
    //   2. Effect match — if the customer wants effects and none of the offered products carry any of them,
    //      the offer is rejected outright, the same hard requirement contracts use (no quality/price fallback).
    // Either gate failing returns a 0% chance.
    [HarmonyPatch(typeof(Customer), nameof(Customer.GetOfferSuccessChance))]
    public class CustomerOfferSuccessPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Customer __instance, Il2CppSystem.Collections.Generic.List<ItemInstance> items, float askingPrice, ref float __result)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled)
                return true;

            if (!OfferTimingGate.AcceptsOfferNow(__instance, config))
            {
                __result = 0f;
                return false;
            }

            if (!config.DirectSales.RequireEffectMatch)
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
