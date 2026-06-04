using System;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using Lithium.Modules.Dealers.Architecture;

namespace Lithium.Modules.Dealers.Patches
{
    [HarmonyPatch(typeof(Dealer), nameof(Dealer.RemoveContractItems))]
    public class DealerSaleTrackingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Dealer __instance, Contract contract)
        {
            ModDealers mod = Core.Get<ModDealers>();
            if (mod == null || !mod.Configuration.Enabled || !mod.Configuration.WeeklyReport.Enabled)
                return;
            if (__instance == null || contract == null || contract.ProductList == null)
                return;

            try
            {
                foreach (ProductList.Entry entry in contract.ProductList.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.ProductID))
                        continue;
                    DealerStatsStore.Record(__instance.ID, DealerShortageCalculator.ResolveName(entry.ProductID), entry.Quantity);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[Dealers] Sale tracking failed: {e.Message}");
            }
        }
    }
}
