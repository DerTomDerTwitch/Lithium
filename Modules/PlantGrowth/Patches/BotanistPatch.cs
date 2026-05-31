using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppFishNet;
using HarmonyLib;

namespace Lithium.Modules.PlantGrowth.Patches
{
    // The botanist harvest flow was reworked: the old PotActionBehaviour.CompleteAction (with an
    // EActionType.Harvest check) is gone, replaced by HarvestPotBehaviour. GetQuantityToHarvest()
    // returns how many product items the botanist pulls from a pot, so we scale it by the per-bud
    // yield multiplier here. (Private method, patched by name.)
    [HarmonyPatch(typeof(HarvestPotBehaviour), "GetQuantityToHarvest")]
    public class HarvestPotBehaviourYieldPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref int __result)
        {
            ModPlantsConfiguration configuration = Core.Get<ModPlants>().Configuration;
            if (!configuration.Enabled)
                return;

            // Multiplayer: botanists are server-controlled NPCs and the yield roll uses random, so only
            // let the server apply the multiplier. Clients calling this for prediction keep vanilla's
            // value; the authoritative result is networked from the server.
            if (!InstanceFinder.IsServer)
                return;

            if (__result <= 0)
                return;

            __result = Math.Max(1, (int)(__result * configuration.RandomYieldPerBudPicker.Pick()));
        }
    }
}
