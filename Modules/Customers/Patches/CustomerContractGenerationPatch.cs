using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using Lithium.Modules.Customers.Behaviours;
using Lithium.Util;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.TryGenerateContract))]
    public class CustomerContractGenerationPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref ContractInfo __result, Customer __instance, Dealer dealer)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            if (__result == null)
                return;

            if (LevelManager.Instance.TotalXP < config.Contracts.XPRequired)
            {
                return;
            }

            OrderPatternProfile pattern = null;
            if (config.OrderPatterns.Enabled)
            {
                pattern = OrderPatternProfile.Create(
                    __instance.CustomerData.name,
                    __instance.CustomerData.MinOrdersPerWeek,
                    __instance.CustomerData.MaxOrdersPerWeek);

                // Safety net in case the game caches the GetOrderDays schedule: suppress orders on
                // days that aren't part of this customer's pattern.
                if (!pattern.OrderDays.Contains(TimeManager.Instance.CurrentDay))
                {
                    __result = null;
                    return;
                }
            }

            List<string> desires = __instance.CustomerData.PreferredProperties
                .ToList()
                .Select(p => p.Name)
                .ToList();

            ProductList.Entry orderedProduct = __result.Products.entries.ToList()[0];
            EQuality quality = orderedProduct.Quality;

            if (desires.Count == 0)
                return;

            float qtyMultiplier = pattern?.QuantityMultiplier ?? 1f;

            if (__instance.AssignedDealer != null)
            {
                // dealerItems = everything the dealer currently holds, regardless of effect match.
                __instance.DealerHasSuitableProduct(out List<ItemInstance> dealerItems);
                if (dealerItems.Count == 0)
                {
                    // Dealer has nothing to sell at all — no order (daily nudge handled elsewhere).
                    __result = null;
                    return;
                }

                List<ProductDefinition> dealerProducts = dealerItems
                    .Select(i => ProductManager.DiscoveredProducts.ToList().FirstOrDefault(p => p.ID.Equals(i.ID)))
                    .Where(p => p != null)
                    .Distinct()
                    .ToList();

                List<ProductDefinition> matching = dealerProducts
                    .Where(p => ProductHelper.ProductMatchesDesires(p, desires))
                    .ToList();

                bool reduced = matching.Count == 0;
                ProductDefinition chosen = (reduced ? dealerProducts : matching)
                    .OrderBy(x => UnityEngine.Random.value)
                    .First();

                int available = dealerItems.Where(i => i.ID == chosen.ID).Sum(i => i.Quantity);
                RewireOrderedProduct(__result, chosen.ID, quality, Mathf.Max(1, available), qtyMultiplier);

                if (reduced)
                {
                    // Nothing in the dealer's stock matches the wanted effects: still sell, but below
                    // the product's default value, and tell the player it was a reduced substitute.
                    ApplyReducedPayment(__result, chosen);
                    CustomerNotifier.NotifyDealerReducedDeal(__instance);
                }
            }
            else
            {
                List<ProductDefinition> listed = ProductManager.ListedProducts.ToList();
                if (listed.Count == 0)
                {
                    // Nothing listed at all — no order (daily nudge handled elsewhere).
                    __result = null;
                    return;
                }

                List<ProductDefinition> matching = listed
                    .Where(p => ProductHelper.ProductMatchesDesires(p, desires))
                    .ToList();

                bool reduced = matching.Count == 0;
                // Direct (non-dealer) sales have no stock ceiling, so RewireOrderedProduct uses
                // int.MaxValue — bulk-pattern customers can order above the game's base quantity.
                ProductDefinition chosen = (reduced ? listed : matching)
                    .OrderBy(x => UnityEngine.Random.value)
                    .First();

                RewireOrderedProduct(__result, chosen.ID, quality, int.MaxValue, qtyMultiplier);

                if (reduced)
                {
                    // No listed product matches the wanted effects: still sell at a reduced price.
                    ApplyReducedPayment(__result, chosen);
                    CustomerNotifier.NotifyPlayerReducedDeal(__instance);
                }
            }
        }

        // Pays the customer below the product's DEFAULT market value (not the player's listed price)
        // for a substitute that doesn't cover their desired effects. Bonus handlers still run at
        // handover, but the effect-coverage bonus is naturally zero here.
        private static void ApplyReducedPayment(ContractInfo __result, ProductDefinition product)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            int qty = __result.Products.entries.ToList().Sum(e => e.Quantity);
            __result.Payment = product.MarketValue * qty * config.Contracts.ReducedDealPriceMultiplier;
        }

        private static void RewireOrderedProduct(ContractInfo __result, string id, EQuality quality, int maxAvailableQuantity, float quantityMultiplier = 1f)
        {
            int desiredQuantity = __result.Products.entries.ToList().Sum(e => e.Quantity);
            int scaledQuantity = Mathf.Max(1, Mathf.RoundToInt(desiredQuantity * quantityMultiplier));
            ProductList list = new();
            list.entries.Add(new()
            {
                ProductID = id,
                Quality = quality,
                Quantity = Mathf.Min(maxAvailableQuantity, scaledQuantity)
            });
            __result.Products = list;
        }
    }
}
