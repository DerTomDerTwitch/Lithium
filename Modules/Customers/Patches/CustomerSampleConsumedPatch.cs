using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Lithium.Helper;
using Lithium.Util;
using MelonLoader;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.GetSampleSuccess))]

    public class CustomerSampleSuccessPatch
    {
        [HarmonyPrefix]
        static bool Prefix(Customer __instance, Il2CppSystem.Collections.Generic.List<ItemInstance> items,
            float price, ref float __result)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.SampleOffering.Enabled)
                return true;

            // Only override sample acceptance once the player reaches the configured rank. Below it
            // (e.g. before a Mixing Station can craft matching effects), defer to the game's own
            // GetSampleSuccess so early-game samples aren't penalised by the Lithium calculation.
            if (!config.SampleOffering.RankMet())
            {
                Log.Info("Sample offering: below configured rank - using vanilla acceptance calculation.");
                return true;
            }

            float sum = 0;
            
            foreach (ItemInstance item in items)
            {
                ProductDefinition productDefinition = item.Definition.TryCast<ProductDefinition>();
                ProductItemInstance productItemInstance = item.TryCast<ProductItemInstance>();


                string[] desires = ProductHelper.GetDesireNames(__instance.CustomerData).ToArray();
                string[] productEffects = productDefinition.Properties.ToList().Select(p => p.Name).ToArray();

                sum += SuccessChanceCalculator.CalculateSuccess(
                    productDefinition.DrugType,  
                    productItemInstance.Quality,
                    config.SampleOffering.QualityLevelModifier,
                    __instance.CustomerData.Standards,
                    desires, 
                    productEffects,
                    __instance.CustomerData.DefaultAffinityData,
                    config.SampleOffering.IncludeDrugPreference,
                    config.SampleOffering.BaseAcceptance,
                    config.SampleOffering.RequireEffectMatch,
                    config.SampleOffering.MaxQualityOverDeliveryLevels,
                    config.SampleOffering.DrugAffinitySharpness);
            }

            __result = sum / items.Count;
            return false;
        }
    }
}