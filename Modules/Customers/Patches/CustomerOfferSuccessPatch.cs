using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.GetOfferSuccessChance))]
    public class CustomerOfferSuccessPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Customer __instance, Il2CppSystem.Collections.Generic.List<ItemInstance> items, ref float askingPrice, ref float __result)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled)
                return true;

            if (!OfferTimingGate.AcceptsOfferNow(__instance, config))
            {
                __result = 0f;
                return false;
            }

            ApplyPriceTolerance(__instance, config, ref askingPrice);

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

        private static void ApplyPriceTolerance(Customer customer, ModCustomersConfiguration config, ref float askingPrice)
        {
            float baseMultiplier = config.DirectSales.PriceToleranceMultiplier;
            if (baseMultiplier <= 0f)
                return;

            float effective = baseMultiplier;
            float jitterRange = config.DirectSales.PriceToleranceJitter;
            if (jitterRange > 0f)
                effective = baseMultiplier * (1f + DeterministicJitter(customer, jitterRange));

            if (effective <= 0f || Mathf.Approximately(effective, 1f))
                return;

            askingPrice /= effective;

            if (Log.DebugEnabled)
                Log.Info($"DirectSales: price tolerance {effective:0.###}x -> perceived asking price {askingPrice:0.##}");
        }

        private static float DeterministicJitter(Customer customer, float range)
        {
            int day = TimeManager.Instance != null ? TimeManager.Instance.ElapsedDays : 0;
            string id = customer != null && customer.NPC != null ? customer.NPC.ID : string.Empty;

            uint hash = 2166136261u;
            foreach (char c in id)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            unchecked
            {
                hash ^= (uint)day;
                hash *= 16777619u;
            }

            float unit = (hash & 0xFFFFFFu) / (float)0x1000000;
            return (unit * 2f - 1f) * range;
        }
    }
}
