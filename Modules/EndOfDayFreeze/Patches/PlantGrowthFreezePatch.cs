using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.EndOfDayFreeze.Patches
{
    [HarmonyPatch(typeof(Pot), nameof(Pot.OnMinPass))]
    public class PlantGrowthFreezePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix()
        {
            return !EndOfDayGate.ShouldFreeze();
        }
    }
}
