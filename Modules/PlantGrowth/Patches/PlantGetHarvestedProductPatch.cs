using System.Reflection;
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
    //
    // IMPORTANT: GetHarvestedProduct is virtual and the concrete plants (WeedPlant, CocaPlant) OVERRIDE
    // it. Harmony patches a specific MethodInfo, so a patch on the base Plant.GetHarvestedProduct never
    // fires for those subclasses (virtual dispatch goes straight to the override). We must therefore
    // patch the overrides themselves. We patch only the concrete overrides — not the base — so a
    // subclass that happened to call base.GetHarvestedProduct wouldn't double-apply the bump.
    [HarmonyPatch]
    public class PlantGetHarvestedProductPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WeedPlant), nameof(WeedPlant.GetHarvestedProduct), [typeof(int)]);
            yield return AccessTools.Method(typeof(CocaPlant), nameof(CocaPlant.GetHarvestedProduct), [typeof(int)]);
        }

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

            // Same clean calculation the player path uses: vanilla base + additive QualityChange,
            // plus a random offset. Ignores the unreliable stored QualityLevel and restores to the
            // clean base afterwards.
            float baseQuality = HarvestQuality.ComputeBaseQuality(__instance);
            float offset = configuration.RandomYieldQualityPicker.Evaluate(UnityEngine.Random.value);
            __state = baseQuality;
            __instance.QualityLevel = UnityEngine.Mathf.Clamp01(baseQuality + offset);
        }

        [HarmonyPostfix]
        public static void Postfix(Plant __instance, float __state)
        {
            if (!float.IsNaN(__state))
                __instance.QualityLevel = __state;
        }
    }
}
