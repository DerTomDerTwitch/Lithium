using System.Reflection;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Growing;

namespace Lithium.Modules.PlantGrowth.Patches
{
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
            __state = float.NaN;

            ModPlantsConfiguration configuration = Core.Get<ModPlants>().Configuration;
            if (!configuration.Enabled)
                return;

            if (!InstanceFinder.IsServer)
                return;

            if (PlantHarvestablePatch.PlayerHarvestInProgress)
                return;

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
