using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Growing;

namespace Lithium.Modules.PlantGrowth.Patches
{
    // Botanists don't go through PlantHarvestable.Harvest (that path is player-only — it checks the
    // local PlayerInventory). They harvest whole plants via HarvestPotBehaviour, which creates the
    // product through Plant.GetHarvestedProduct(quantity). That method reads the plant's QualityLevel
    // to set the item's quality, so to give botanists the same randomised quality the players get we
    // bump QualityLevel around the call and restore it afterwards (the created item keeps the rolled
    // quality). The botanist's randomised QUANTITY is handled separately in HarvestPotBehaviourYieldPatch
    // (GetQuantityToHarvest), so we only touch quality here.
    [HarmonyPatch(typeof(Plant), nameof(Plant.GetHarvestedProduct))]
    public class PlantGetHarvestedProductPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Plant __instance, out float __state)
        {
            __state = float.NaN; // sentinel: no change made / nothing to restore

            ModPlantsConfiguration configuration = Core.Get<ModPlants>().Configuration;
            if (!configuration.Enabled)
                return;

            // Quality roll is server-authoritative; the result is networked to clients with the item.
            if (!InstanceFinder.IsServer)
                return;

            // The player hand-harvest path already rolls its own quality in PlantHarvestablePatch –
            // don't double-apply if GetHarvestedProduct happens to be reached from there.
            if (PlantHarvestablePatch.PlayerHarvestInProgress)
                return;

            __state = __instance.QualityLevel;
            __instance.QualityLevel += configuration.RandomYieldQualityPicker.Evaluate(UnityEngine.Random.value);
        }

        [HarmonyPostfix]
        public static void Postfix(Plant __instance, float __state)
        {
            if (!float.IsNaN(__state))
                __instance.QualityLevel = __state;
        }
    }
}
