using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.EndOfDayFreeze.Patches
{
    // Pot.OnMinPass is where plant growth and water drain advance (and where ModPlants applies its
    // grow-speed modifier). Returning false during the freeze skips the whole tick — including the
    // ModPlants prefix (Priority.First guarantees we run first) — so plants don't grow at 4 AM.
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
