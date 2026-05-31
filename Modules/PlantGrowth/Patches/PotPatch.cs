using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using Lithium.Modules.PlantGrowth.Behaviours;
using UnityEngine;

namespace Lithium.Modules.PlantGrowth.Patches
{
    [HarmonyPatch(typeof(Pot), nameof(Pot.Start))]
    public class PotStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pot __instance)
        {
            if (!Core.Get<ModPlants>().Configuration.Enabled)
                return;
            __instance.gameObject.AddComponent<PotBaseValues>().Init(__instance);
        }
    }

    [HarmonyPatch(typeof(Pot), nameof(Pot.OnMinPass))]
    public class PotMinPassPatch
    {
        // Pot.GrowSpeedMultiplier is a field-backed accessor that Il2CppInterop cannot hook (callers
        // read the field directly, so a getter postfix never takes effect). OnMinPass reads that field
        // when it advances growth, so instead of patching the getter we write the modified value onto
        // the field right before OnMinPass runs — using the base captured at Pot.Start so the modifier
        // never compounds. Water drain is handled the same way.
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
