using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppFishNet;
using HarmonyLib;

namespace Lithium.Modules.PlantGrowth.Patches
{
    [HarmonyPatch(typeof(HarvestPotBehaviour), "GetQuantityToHarvest")]
    public class HarvestPotBehaviourYieldPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref int __result)
        {
            ModPlantsConfiguration configuration = Core.Get<ModPlants>().Configuration;
            if (!configuration.Enabled)
                return;

            if (!InstanceFinder.IsServer)
                return;

            if (__result <= 0)
                return;

            __result = Math.Max(1, (int)(__result * configuration.RandomYieldPerBudPicker.Pick()));
        }
    }
}
