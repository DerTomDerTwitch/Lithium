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
        // TryGenerateContract's postfix fires more than once on the SAME ContractInfo (the game reprocesses
        // the customer's pending contract), which compounded the bulk multiplier — e.g. 11 -> 66 -> 396,
        // because each re-fire read the already-scaled quantity as its new base. Rather than assume the
        // re-fires land in the same frame (the old per-frame guard's fragile premise), we remember the exact
        // contract shape we last wrote for each ContractInfo pointer. On entry we compare the incoming
        // contract to that fingerprint: an exact match means the game is reprocessing our own output, so we
        // leave it untouched; a mismatch (or unseen pointer) is a genuine fresh roll — or a GC-reused pointer
        // now backing a different contract — and is scaled normally. Frame-independent and reuse-safe.
        private static readonly Dictionary<IntPtr, string> _lastWritten = new();

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

            // A customer whose offer was refused / expired re-attempts today, even if today isn't one
            // of their normal (or pattern) order days. The flag is cleared once we hand them a fresh
            // offer below; if generation fails they keep it and try again the next day.
            bool isRetryDay = config.Contracts.RetryNextDayOnRefusal &&
                              ContractRetryTracker.IsRetryDay(__instance.CustomerData.name);

            OrderPatternProfile pattern = null;
            if (config.OrderPatterns.Enabled)
            {
                pattern = OrderPatternProfile.Create(
                    __instance.CustomerData.name,
                    __instance.CustomerData.MinOrdersPerWeek,
                    __instance.CustomerData.MaxOrdersPerWeek);

                // Safety net in case the game caches the GetOrderDays schedule: suppress orders on
                // days that aren't part of this customer's pattern (retry days excepted).
                if (!isRetryDay && !pattern.OrderDays.Contains(TimeManager.Instance.CurrentDay))
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
            {
                // Game default order is fine — the customer got an offer, so any retry debt is settled.
                ContractRetryTracker.Clear(__instance.CustomerData.name);
                return;
            }

            float qtyMultiplier = pattern?.QuantityMultiplier ?? 1f;

            // Idempotency guard (see field note): if the contract handed to us is exactly what we last wrote
            // for this pointer, the game is just reprocessing our own output — leave it as-is rather than
            // re-scaling it. Captured before any mutation so it reflects the game's fresh roll on a real call.
            IntPtr contractPtr = __result.Pointer;
            string incomingFingerprint = Fingerprint(__result);
            if (_lastWritten.TryGetValue(contractPtr, out string previous) && previous == incomingFingerprint)
            {
                Log.Info($"[Lithium] Skipped reprocessed contract for {__instance.CustomerData.name}.");
                return;
            }

            if (pattern != null && Log.DebugEnabled)
                Log.Info($"[Lithium] OrderGen {__instance.CustomerData.name}: " +
                    $"gameBase={__result.Products.entries.ToList().Sum(e => e.Quantity)}, " +
                    $"ordersPerWeek={__instance.CustomerData.MinOrdersPerWeek}-{__instance.CustomerData.MaxOrdersPerWeek}, " +
                    $"archetype={pattern.Archetype}, days={pattern.OrderDays.Count}, mult={qtyMultiplier:F2}");

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

                if (matching.Count == 0)
                {
                    // Nothing in the dealer's stock matches the wanted effects: still sell, but below
                    // the product's default value, and tell the player it was a reduced substitute.
                    ProductDefinition chosen = dealerProducts.OrderBy(x => UnityEngine.Random.value).First();
                    int available = dealerItems.Where(i => i.ID == chosen.ID).Sum(i => i.Quantity);
                    RewireOrderedProduct(__result, chosen.ID, quality, Mathf.Max(1, available), qtyMultiplier);
                    ApplyReducedPayment(__result, chosen);
                    CustomerNotifier.NotifyDealerReducedDeal(__instance);
                }
                else
                {
                    // Prefer higher-coverage products (and maybe add a second one), capped by the
                    // dealer's stock for each chosen product. Pays the player's set price when enabled.
                    ComposeMatchingOrder(__result, matching, desires, quality, qtyMultiplier,
                        p => dealerItems.Where(i => i.ID == p.ID).Sum(i => i.Quantity),
                        config.Contracts.DealerSellAtListedPrice);
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

                if (matching.Count == 0)
                {
                    // No listed product matches the wanted effects: still sell at a reduced price.
                    ProductDefinition chosen = listed.OrderBy(x => UnityEngine.Random.value).First();
                    RewireOrderedProduct(__result, chosen.ID, quality, int.MaxValue, qtyMultiplier);
                    ApplyReducedPayment(__result, chosen);
                    CustomerNotifier.NotifyPlayerReducedDeal(__instance);
                }
                else
                {
                    // Prefer higher-coverage products (and maybe add a second one). Direct sales have no
                    // stock ceiling, so each product's available quantity is unbounded. Pays the player's
                    // listed price when enabled (otherwise the game's standard per-unit roll).
                    ComposeMatchingOrder(__result, matching, desires, quality, qtyMultiplier,
                        _ => int.MaxValue, config.Contracts.SellAtListedPrice);
                }
            }

            if (__result != null && Log.DebugEnabled)
                Log.Info($"[Lithium] Contract for {__instance.CustomerData.name}: " +
                    string.Join(", ", __result.Products.entries.ToList().Select(e => $"{e.Quantity}x {e.ProductID}")) +
                    $" = ${__result.Payment:F0}");

            // Remember the exact shape we just wrote so the game's reprocess pass recognises it as our own
            // output (above) instead of re-scaling it.
            RememberWritten(contractPtr, __result);

            // A fresh offer reached the player — the retry obligation (if any) is fulfilled. A later
            // refusal/expiry re-arms it for the following day.
            ContractRetryTracker.Clear(__instance.CustomerData.name);
        }

        // A stable signature of a contract's payable shape (payment + each ordered product, quantity and
        // quality). Two invocations producing the same string describe the same order.
        private static string Fingerprint(ContractInfo info)
        {
            if (info == null || info.Products == null)
                return string.Empty;
            return $"{info.Payment:F2}|" + string.Join(",", info.Products.entries.ToList()
                .Select(e => $"{e.ProductID}:{e.Quantity}:{(int)e.Quality}"));
        }

        private static void RememberWritten(IntPtr contractPtr, ContractInfo info)
        {
            if (info == null)
            {
                _lastWritten.Remove(contractPtr);
                return;
            }
            // Contracts are transient and few are in flight at once, but never let a long session grow the
            // map without bound — a periodic flush is harmless since a flushed entry just re-scales once.
            if (_lastWritten.Count > 512)
                _lastWritten.Clear();
            _lastWritten[contractPtr] = Fingerprint(info);
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

        // Builds the ordered product(s) for a matching (effect-covering) deal. Picks a primary product
        // weighted heavily toward effect coverage, optionally adds a different second product that takes
        // a share of the quantity, and prices the deal — at the player's set price when useListedPrice is
        // on, otherwise by scaling the game's per-unit roll. availableOf gives the cap per product (the
        // dealer's stock, or int.MaxValue for direct sales).
        private static void ComposeMatchingOrder(
            ContractInfo __result,
            List<ProductDefinition> matching,
            List<string> desires,
            EQuality quality,
            float quantityMultiplier,
            Func<ProductDefinition, int> availableOf,
            bool useListedPrice)
        {
            ProductSelection selection = Core.Get<ModCustomers>().Configuration.Contracts.ProductSelection;

            // Baseline from the game's roll (its per-unit price already bakes in quality and markup).
            int desiredQuantity = __result.Products.entries.ToList().Sum(e => e.Quantity);
            float originalPayment = __result.Payment;
            int scaled = Mathf.Max(1, Mathf.RoundToInt(desiredQuantity * quantityMultiplier));

            ProductDefinition primary = PickWeightedByCoverage(matching, desires, selection.CoverageBiasExponent);

            // Maybe pick a second, different product — also weighted toward coverage.
            ProductDefinition secondary = null;
            if (selection.EnableSecondProduct && matching.Count > 1 &&
                UnityEngine.Random.value < selection.SecondProductChance)
            {
                List<ProductDefinition> others = matching.Where(p => p.ID != primary.ID).ToList();
                if (others.Count > 0)
                    secondary = PickWeightedByCoverage(others, desires, selection.CoverageBiasExponent);
            }

            int primaryCap = Mathf.Max(1, availableOf(primary));
            int primaryQty;
            int secondaryQty = 0;

            if (secondary != null)
            {
                // The second product takes a share of the order; the first keeps the remainder. Each is
                // still capped by its own available stock.
                int secondaryCap = Mathf.Max(0, availableOf(secondary));
                secondaryQty = Mathf.Clamp(Mathf.RoundToInt(scaled * selection.SecondProductQuantityShare), 0, secondaryCap);
                primaryQty = Mathf.Min(scaled - secondaryQty, primaryCap);

                if (secondaryQty <= 0 || primaryQty <= 0)
                {
                    // Couldn't actually split (e.g. no stock for the second product) — single product.
                    secondary = null;
                    secondaryQty = 0;
                    primaryQty = Mathf.Max(1, Mathf.Min(scaled, primaryCap));
                }
            }
            else
            {
                primaryQty = Mathf.Max(1, Mathf.Min(scaled, primaryCap));
            }

            ProductList list = new();
            list.entries.Add(new() { ProductID = primary.ID, Quality = quality, Quantity = primaryQty });
            if (secondary != null && secondaryQty > 0)
                list.entries.Add(new() { ProductID = secondary.ID, Quality = quality, Quantity = secondaryQty });
            __result.Products = list;

            int total = primaryQty + secondaryQty;

            ProductManager manager = useListedPrice ? NetworkSingleton<ProductManager>.Instance : null;
            if (manager != null)
            {
                // Player's set price per product times its quantity.
                float payment = manager.GetPrice(primary) * primaryQty;
                if (secondary != null && secondaryQty > 0)
                    payment += manager.GetPrice(secondary) * secondaryQty;
                __result.Payment = payment;
            }
            else if (desiredQuantity > 0 && total != desiredQuantity)
            {
                // Scale the game's roll proportionally to the (possibly bulk-scaled / stock-capped) total.
                __result.Payment = originalPayment / desiredQuantity * total;
            }
        }

        // Picks one product from the candidates, weighted by coverage^exponent so products covering more
        // of the customer's desired effects are much more likely. Candidates are assumed to cover at
        // least one effect (coverage >= 1).
        private static ProductDefinition PickWeightedByCoverage(
            List<ProductDefinition> candidates, List<string> desires, float exponent)
        {
            if (candidates.Count == 1)
                return candidates[0];

            float[] weights = new float[candidates.Count];
            float totalWeight = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                int coverage = Mathf.Max(1, ProductHelper.CoveredEffectCount(candidates[i], desires));
                weights[i] = Mathf.Pow(coverage, exponent);
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

        private static void RewireOrderedProduct(ContractInfo __result, string id, EQuality quality, int maxAvailableQuantity, float quantityMultiplier = 1f)
        {
            int desiredQuantity = __result.Products.entries.ToList().Sum(e => e.Quantity);
            int scaledQuantity = Mathf.Max(1, Mathf.RoundToInt(desiredQuantity * quantityMultiplier));
            int finalQuantity = Mathf.Min(maxAvailableQuantity, scaledQuantity);

            // The game sized __result.Payment for the ORIGINAL order quantity. We're changing how many
            // units the customer buys (bulk patterns scale up, dealer stock caps down), so the payment
            // has to move with the quantity — otherwise a 54-unit bulk order still only pays for the
            // handful the game first rolled. Scaling proportionally preserves the game's own per-unit
            // price (quality, market value and customer markup are already baked into Payment).
            if (desiredQuantity > 0 && finalQuantity != desiredQuantity)
                __result.Payment = __result.Payment / desiredQuantity * finalQuantity;

            ProductList list = new();
            list.entries.Add(new()
            {
                ProductID = id,
                Quality = quality,
                Quantity = finalQuantity
            });
            __result.Products = list;
        }
    }
}
