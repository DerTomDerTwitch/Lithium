using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using Lithium.Modules.PlantGrowth.Behaviours;
using UnityEngine;

namespace Lithium.Modules.PlantGrowth.Patches
{
    [HarmonyPatch(typeof(Pot), nameof(Pot.OnMinPass))]
    public class PotMinPassPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Pot __instance)
        {
            ModPlantsConfiguration config = Core.Get<ModPlants>().Configuration;
            if (!config.Enabled)
                return;
            if (__instance == null)
                return;

            PotBaseValues potBaseValues = __instance.gameObject.GetComponent<PotBaseValues>();
            if (potBaseValues == null)
                return;

            __instance._moistureDrainPerHour = potBaseValues.BaseWaterDrainPerHour * config.WaterDrainModifier;
            __instance.GrowSpeedMultiplier = potBaseValues.BaseGrowSpeedMultiplier * Mathf.Max(0.001f, config.GrowthModifier);
        }
    }
}
