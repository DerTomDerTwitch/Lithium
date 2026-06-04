using System;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
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
        private static readonly Dictionary<string, string> _lastWritten = new();

        private static readonly Dictionary<string, int> _lastOrderDay = new();

        public static void ResetState()
        {
            _lastWritten.Clear();
            _lastOrderDay.Clear();
        }

        private const int MaxOrderQuantity = 9999;

        [HarmonyPostfix]
        public static void Postfix(ref ContractInfo __result, Customer __instance, Dealer dealer)
        {
            try
            {
                Generate(ref __result, __instance, dealer);
            }
            catch (Exception e)
            {
                string who = __instance != null && __instance.CustomerData != null
                    ? __instance.CustomerData.name : "<unknown>";
                Log.Warning($"[Customers] Contract generation failed for {who}; leaving the game's contract untouched. {e}");
            }
        }

        private static void Generate(ref ContractInfo __result, Customer __instance, Dealer dealer)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            if (__result == null)
                return;

            if (dealer == null && __instance.AssignedDealer != null)
            {
                __result = null;
                return;
            }

            if (LevelManager.Instance.TotalXP < config.Contracts.XPRequired)
            {
                return;
            }

            bool isRetryDay = config.Contracts.RetryNextDayOnRefusal &&
                              ContractRetryTracker.IsRetryDay(__instance.CustomerData.name);

            OrderPatternProfile pattern = null;
            if (config.OrderPatterns.Enabled)
            {
                pattern = OrderPatternProfile.Create(
                    __instance.CustomerData.name,
                    __instance.CustomerData.MinOrdersPerWeek,
                    __instance.CustomerData.MaxOrdersPerWeek);

                if (!isRetryDay && !pattern.OrderDays.Contains(TimeManager.Instance.CurrentDay))
                {
                    __result = null;
                    return;
                }
            }

            List<string> desires = ProductHelper.GetDesireNames(__instance.CustomerData);

            ProductList.Entry orderedProduct = __result.Products.entries.ToList()[0];
            EQuality quality = orderedProduct.Quality;

            if (desires.Count == 0)
            {
                ContractRetryTracker.Clear(__instance.CustomerData.name);
                return;
            }

            float perOrderBudget = pattern != null ? ComputeOrderBudget(__instance, pattern.OrderDays.Count) : -1f;

            string customerKey = __instance.CustomerData.name ?? string.Empty;
            string incomingFingerprint = Fingerprint(__result);
            if (_lastWritten.TryGetValue(customerKey, out string previous) && previous == incomingFingerprint)
            {
                Log.Info($"[Lithium] Skipped reprocessed contract for {__instance.CustomerData.name}.");
                return;
            }

            if (pattern != null &&
                _lastOrderDay.TryGetValue(customerKey, out int lastDay) &&
                lastDay == TimeManager.Instance.ElapsedDays)
            {
                __result = null;
                return;
            }

            if (pattern != null && Log.DebugEnabled)
                Log.Info($"[Lithium] OrderGen {__instance.CustomerData.name}: " +
                    $"gameBase={ProductHelper.GetTotalQuantity(__result.Products)}, " +
                    $"ordersPerWeek={__instance.CustomerData.MinOrdersPerWeek}-{__instance.CustomerData.MaxOrdersPerWeek}, " +
                    $"archetype={pattern.Archetype}, days={pattern.OrderDays.Count}, perOrderBudget=${perOrderBudget:F0}");

            if (__instance.AssignedDealer != null)
            {
                __instance.DealerHasSuitableProduct(out List<ItemInstance> dealerItems);
                if (dealerItems.Count == 0)
                {
                    __result = null;
                    return;
                }

                List<ProductDefinition> dealerProducts = dealerItems
                    .Select(i => ProductManager.DiscoveredProducts.ToList().FirstOrDefault(p => p.ID.Equals(i.ID)))
                    .Where(p => p != null)
                    .Distinct()
                    .ToList();

                Func<ProductDefinition, int> availableOf =
                    p => dealerItems.Where(i => i.ID == p.ID).Sum(i => i.Quantity);

                if (!config.Contracts.DealerRequireEffectMatch)
                {
                    ComposeMatchingOrder(__result, dealerProducts, desires, __instance.CustomerData, quality,
                        perOrderBudget, availableOf, config.Contracts.DealerSellAtListedPrice);
                }
                else
                {
                    List<ProductDefinition> matching = dealerProducts
                        .Where(p => ProductHelper.ProductMatchesDesires(p, desires))
                        .ToList();

                    if (matching.Count == 0)
                    {
                        ProductDefinition chosen = dealerProducts.OrderBy(x => UnityEngine.Random.value).First();
                        ReducedDeal deal = ApplyReducedDeal(__result, chosen, quality, Mathf.Max(1, availableOf(chosen)),
                            perOrderBudget, config.Contracts.DealerSellAtListedPrice);
                        CustomerNotifier.NotifyDealerReducedDeal(__instance, deal.ProductName, deal.Quantity, deal.UnitPrice, deal.Total);
                    }
                    else
                    {
                        ComposeMatchingOrder(__result, matching, desires, __instance.CustomerData, quality,
                            perOrderBudget, availableOf, config.Contracts.DealerSellAtListedPrice);
                    }
                }
            }
            else
            {
                List<ProductDefinition> listed = ProductManager.ListedProducts.ToList();
                if (listed.Count == 0)
                {
                    __result = null;
                    return;
                }

                if (!config.Contracts.RequireEffectMatch)
                {
                    ComposeMatchingOrder(__result, listed, desires, __instance.CustomerData, quality,
                        perOrderBudget, _ => int.MaxValue, config.Contracts.SellAtListedPrice);
                }
                else
                {
                    List<ProductDefinition> matching = listed
                        .Where(p => ProductHelper.ProductMatchesDesires(p, desires))
                        .ToList();

                    if (matching.Count == 0)
                    {
                        ProductDefinition chosen = listed.OrderBy(x => UnityEngine.Random.value).First();
                        ReducedDeal deal = ApplyReducedDeal(__result, chosen, quality, int.MaxValue,
                            perOrderBudget, config.Contracts.SellAtListedPrice);
                        CustomerNotifier.NotifyPlayerReducedDeal(__instance, deal.ProductName, deal.Quantity, deal.UnitPrice, deal.Total);
                    }
                    else
                    {
                        ComposeMatchingOrder(__result, matching, desires, __instance.CustomerData, quality,
                            perOrderBudget, _ => int.MaxValue, config.Contracts.SellAtListedPrice);
                    }
                }
            }

            if (__result != null && Log.DebugEnabled)
                Log.Info($"[Lithium] Contract for {__instance.CustomerData.name}: " +
                    string.Join(", ", __result.Products.entries.ToList().Select(e => $"{e.Quantity}x {e.ProductID}")) +
                    $" = ${__result.Payment:F0}");

            RememberWritten(customerKey, __result);

            if (pattern != null && __result != null)
                _lastOrderDay[customerKey] = TimeManager.Instance.ElapsedDays;

            ContractRetryTracker.Clear(__instance.CustomerData.name);
        }

        private static string Fingerprint(ContractInfo info)
        {
            if (info == null || info.Products == null)
                return string.Empty;
            return string.Join(",", info.Products.entries.ToList()
                .Select(e => $"{e.ProductID}:{e.Quantity}:{(int)e.Quality}"));
        }

        private static void RememberWritten(string customerKey, ContractInfo info)
        {
            if (info == null)
            {
                _lastWritten.Remove(customerKey);
                return;
            }
            if (_lastWritten.Count > 4096)
                _lastWritten.Clear();
            _lastWritten[customerKey] = Fingerprint(info);
        }

        private static float ComputeOrderBudget(Customer customer, int orderDayCount)
        {
            if (customer == null || customer.CustomerData == null)
                return -1f;

            float normalizedRelationship = 0f;
            if (customer.NPC != null && customer.NPC.RelationData != null)
                normalizedRelationship = customer.NPC.RelationData.NormalizedRelationDelta;

            float weeklyBudget = customer.CustomerData.GetAdjustedWeeklySpend(normalizedRelationship);
            if (weeklyBudget <= 0f)
                return -1f;

            float sizeFactor = Mathf.Max(0f, Core.Get<ModCustomers>().Configuration.OrderPatterns.BulkOrderSizeFactor);
            int days = Mathf.Max(1, orderDayCount);
            return weeklyBudget * sizeFactor / days;
        }

        private static float UnitPrice(ProductDefinition product, bool useListedPrice, float gameUnitPrice)
        {
            if (useListedPrice)
            {
                ProductManager manager = NetworkSingleton<ProductManager>.Instance;
                if (manager != null)
                    return Mathf.Max(0.01f, manager.GetPrice(product));
            }
            return Mathf.Max(0.01f, gameUnitPrice);
        }

        private static int QuantityFromBudget(float budget, float unitPrice, int cap)
        {
            int qty = Mathf.RoundToInt(budget / Mathf.Max(0.01f, unitPrice));
            int ceiling = Mathf.Max(1, Mathf.Min(cap, MaxOrderQuantity));
            return Mathf.Clamp(qty, 1, ceiling);
        }

        private readonly struct ReducedDeal
        {
            public ReducedDeal(string productName, int quantity, float unitPrice, float total)
            {
                ProductName = productName;
                Quantity = quantity;
                UnitPrice = unitPrice;
                Total = total;
            }

            public string ProductName { get; }
            public int Quantity { get; }
            public float UnitPrice { get; }
            public float Total { get; }
        }

        // Substitute deal: nothing in stock matched the customer's desires, so they buy
        // something else at a reduced price. The reduction factor is applied exactly once,
        // on top of the listed price the player actually sells at (when the relevant
        // SellAtListedPrice flag is set) rather than the raw market value — otherwise the
        // payout, and the dealer's cut of it, would be undervalued.
        private static ReducedDeal ApplyReducedDeal(ContractInfo __result, ProductDefinition product,
            EQuality quality, int maxAvailableQuantity, float perOrderBudget, bool useListedPrice)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;

            float unitPrice = UnitPrice(product, useListedPrice, product.MarketValue);

            int desiredQuantity = ProductHelper.GetTotalQuantity(__result.Products);
            int finalQuantity = perOrderBudget > 0f
                ? QuantityFromBudget(perOrderBudget, unitPrice, maxAvailableQuantity)
                : Mathf.Max(1, Mathf.Min(maxAvailableQuantity, desiredQuantity));

            ProductList list = new();
            list.entries.Add(new()
            {
                ProductID = product.ID,
                Quality = quality,
                Quantity = finalQuantity
            });
            __result.Products = list;

            float reducedUnit = unitPrice * config.Contracts.ReducedDealPriceMultiplier;
            __result.Payment = reducedUnit * finalQuantity;

            return new ReducedDeal(product.Name, finalQuantity, reducedUnit, __result.Payment);
        }

        private static void ComposeMatchingOrder(
            ContractInfo __result,
            List<ProductDefinition> matching,
            List<string> desires,
            CustomerData customerData,
            EQuality quality,
            float perOrderBudget,
            Func<ProductDefinition, int> availableOf,
            bool useListedPrice)
        {
            ProductSelection selection = Core.Get<ModCustomers>().Configuration.Contracts.ProductSelection;

            int desiredQuantity = ProductHelper.GetTotalQuantity(__result.Products);
            float originalPayment = __result.Payment;
            float gameUnitPrice = desiredQuantity > 0 ? originalPayment / desiredQuantity : 0f;
            bool useBudget = perOrderBudget > 0f;

            ProductDefinition primary = PickWeightedByCoverage(matching, desires, customerData, selection);
            ProductDefinition secondary = PickSecondaryProduct(matching, primary, desires, customerData, selection);

            float primaryUnit = UnitPrice(primary, useListedPrice, gameUnitPrice);
            int primaryCap = Mathf.Max(1, availableOf(primary));
            int primaryQty;
            int secondaryQty = 0;

            if (secondary != null)
            {
                float secondaryUnit = UnitPrice(secondary, useListedPrice, gameUnitPrice);
                int secondaryCap = Mathf.Max(0, availableOf(secondary));

                if (useBudget)
                {
                    secondaryQty = QuantityFromBudget(perOrderBudget * selection.SecondProductQuantityShare, secondaryUnit, secondaryCap);
                    primaryQty = QuantityFromBudget(perOrderBudget * (1f - selection.SecondProductQuantityShare), primaryUnit, primaryCap);
                }
                else
                {
                    secondaryQty = Mathf.Clamp(Mathf.RoundToInt(desiredQuantity * selection.SecondProductQuantityShare), 0, secondaryCap);
                    primaryQty = Mathf.Min(Mathf.Max(1, desiredQuantity - secondaryQty), primaryCap);
                }

                if (secondaryQty <= 0 || primaryQty <= 0)
                {
                    secondary = null;
                    secondaryQty = 0;
                    primaryQty = useBudget
                        ? QuantityFromBudget(perOrderBudget, primaryUnit, primaryCap)
                        : Mathf.Max(1, Mathf.Min(desiredQuantity, primaryCap));
                }
            }
            else
            {
                primaryQty = useBudget
                    ? QuantityFromBudget(perOrderBudget, primaryUnit, primaryCap)
                    : Mathf.Max(1, Mathf.Min(desiredQuantity, primaryCap));
            }

            ProductList list = new();
            list.entries.Add(new() { ProductID = primary.ID, Quality = quality, Quantity = primaryQty });
            if (secondary != null && secondaryQty > 0)
                list.entries.Add(new() { ProductID = secondary.ID, Quality = quality, Quantity = secondaryQty });
            __result.Products = list;

            float payment = primaryUnit * primaryQty;
            if (secondary != null && secondaryQty > 0)
                payment += UnitPrice(secondary, useListedPrice, gameUnitPrice) * secondaryQty;
            __result.Payment = payment;
        }

        private static ProductDefinition PickSecondaryProduct(List<ProductDefinition> matching,
            ProductDefinition primary, List<string> desires, CustomerData customerData, ProductSelection selection)
        {
            if (!selection.EnableSecondProduct || matching.Count <= 1 ||
                UnityEngine.Random.value >= selection.SecondProductChance)
                return null;

            List<ProductDefinition> others = matching.Where(p => p.ID != primary.ID).ToList();
            return others.Count > 0
                ? PickWeightedByCoverage(others, desires, customerData, selection)
                : null;
        }

        // Weight = (coveredEffects + 1)^CoverageBiasExponent * drugTypeFactor^DrugTypeBiasExponent.
        // The +1 base keeps a zero-coverage product selectable (needed when the effect-match
        // requirement is off and the candidate pool is the full stock) while still strongly
        // favouring higher coverage. The drug-type factor is the customer's affinity for the
        // product's drug type mapped to [0,1], so products in a disliked drug type are deprioritised
        // but never outright excluded.
        private static ProductDefinition PickWeightedByCoverage(
            List<ProductDefinition> candidates, List<string> desires, CustomerData customerData, ProductSelection selection)
        {
            if (candidates.Count == 1)
                return candidates[0];

            float[] weights = new float[candidates.Count];
            float totalWeight = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                int coverage = ProductHelper.CoveredEffectCount(candidates[i], desires);
                float coverageWeight = Mathf.Pow(coverage + 1, selection.CoverageBiasExponent);
                float drugWeight = DrugTypeWeight(candidates[i], customerData, selection.DrugTypeBiasExponent);
                weights[i] = coverageWeight * drugWeight;
                totalWeight += weights[i];
            }

            if (totalWeight <= 0f)
                return candidates[UnityEngine.Random.Range(0, candidates.Count)];

            float roll = UnityEngine.Random.value * totalWeight;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f)
                    return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        private static float DrugTypeWeight(ProductDefinition product, CustomerData customerData, float exponent)
        {
            if (exponent <= 0f)
                return 1f;
            float affinity = ProductHelper.DrugTypeAffinity(product, customerData);
            float normalized = Mathf.Clamp01((affinity + 1f) / 2f);
            return Mathf.Pow(Mathf.Max(0.0001f, normalized), exponent);
        }
    }
}
