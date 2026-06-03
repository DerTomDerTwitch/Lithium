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

            // Multiplayer: plant growth is server-authoritative (Pot syncs progress via RPC). The
            // yield roll uses UnityEngine.Random, so it must only happen on the server — otherwise each
            // peer would roll a different YieldMultiplier and desync the harvested amount.
            if (!InstanceFinder.IsServer)
                return;

            if (__instance == null)
                return;
            if (__instance.GetComponent<PlantModified>() != null) 
                return;

            // Marker component: its presence (checked above) stops GrowthDone re-rolling the yield.
            __instance.gameObject.AddComponent<PlantModified>();

            __instance.YieldMultiplier *= configuration.RandomYieldModifierPicker.Evaluate(UnityEngine.Random.value);
        }
    }
}
