using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Growing;
using Lithium.Modules.PlantGrowth.Behaviours;
using UnityEngine;

namespace Lithium.Modules.PlantGrowth.Patches
{
    [HarmonyPatch(typeof(Plant), nameof(Plant.GrowthDone))]
    public class PlantGrowthDonePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Plant __instance)
        {
            ModPlantsConfiguration configuration = Core.Get<ModPlants>().Configuration;
            if (!configuration.Enabled)
                return;

            if (!InstanceFinder.IsServer)
                return;

            if (__instance == null)
                return;
            if (__instance.GetComponent<PlantModified>() != null)
                return;

            __instance.gameObject.AddComponent<PlantModified>();

            __instance.YieldMultiplier *= configuration.RandomYieldModifierPicker.Evaluate(UnityEngine.Random.value);
        }
    }
}
