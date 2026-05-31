using HarmonyLib;
using Il2CppScheduleOne.PlayerScripts;

namespace Lithium.Modules.WateringCans.Patches
{
    [HarmonyPatch(typeof(WaterContainerPourable), nameof(WaterContainerPourable.PourAmount))]
    public static class FunctionalWateringCanPourAmountPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref float amount)
        {
            ModWateringCanConfiguration configuration = Core.Get<ModWateringCan>().Configuration;

            if(!configuration.Enabled)
                return;

            amount *= configuration.DrainModifier;
        }
    }
}
