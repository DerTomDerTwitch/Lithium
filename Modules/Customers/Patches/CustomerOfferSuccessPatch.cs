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
    // Direct (in-person) sales go through Customer.GetOfferSuccessChance. Lithium layers three things over
    // the native acceptance maths (and otherwise lets the original method run):
    //   1. Off-schedule timing — a bulk-pattern customer refuses an unsolicited offer until enough of the
    //      wait for their next scheduled order has passed (OfferTimingGate), so the player can't sell them
    //      extra product every day and bypass the bulk cadence.
    //   2. Effect match — if the customer wants effects and none of the offered products carry any of them,
    //      the offer is rejected outright, the same hard requirement contracts use (no quality/price fallback).
    //   3. Price tolerance — the price the customer *perceives* is divided by the configured tolerance
    //      (with optional per-day/per-customer jitter) before the native curve sees it, shifting the stock
    //      ~1.6x-of-value "no drawback" ceiling up or down. The money actually paid is unaffected; only this
    //      success-chance computation reads the adjusted price.
    // The timing/effect gates each return a 0% chance when they fail.
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

            // Shift the perceived price by the configured tolerance before any native acceptance maths.
            // Done before the effect-match gate too — harmless if that gate then rejects the offer.
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

        // Divides the perceived asking price by the effective tolerance so a tolerance > 1 makes the
        // customer judge the offer as cheaper (more forgiving of markup) and < 1 as pricier (stricter).
        private static void ApplyPriceTolerance(Customer customer, ModCustomersConfiguration config, ref float askingPrice)
        {
            float baseMultiplier = config.DirectSales.PriceToleranceMultiplier;
            if (baseMultiplier <= 0f)
                return; // misconfigured — leave the price untouched rather than divide by ~0

            float effective = baseMultiplier;
            float jitterRange = config.DirectSales.PriceToleranceJitter;
            if (jitterRange > 0f)
                effective = baseMultiplier * (1f + DeterministicJitter(customer, jitterRange));

            if (effective <= 0f || Mathf.Approximately(effective, 1f))
                return; // vanilla (or safety) — nothing to shift

            askingPrice /= effective;

            if (Log.DebugEnabled)
                Log.Info($"DirectSales: price tolerance {effective:0.###}x -> perceived asking price {askingPrice:0.##}");
        }

        // A deterministic roll in [-range, +range], stable for a given (customer, in-game day) so the
        // success chance shown to the player matches the one actually rolled, yet differs day to day and
        // customer to customer. FNV-1a over the NPC id + elapsed-day count (reproducible across sessions,
        // unlike a runtime-randomised string GetHashCode).
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

            float unit = (hash & 0xFFFFFFu) / (float)0x1000000; // [0, 1)
            return (unit * 2f - 1f) * range;                    // [-range, +range]
        }
    }
}
