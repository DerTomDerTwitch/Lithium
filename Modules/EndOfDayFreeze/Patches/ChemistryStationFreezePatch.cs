using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.EndOfDayFreeze.Patches
{
    // ChemistryCookOperation.Progress is the per-minute cook advance (also scaled by
    // ModChemistryStation's speed prefix). Returning false during the freeze skips the advance entirely
    // — including the speed prefix (Priority.First) — so the cook makes no progress at 4 AM. The cook's
    // completion is driven from inside Progress, so skipping it cannot complete a cook while frozen.
    [HarmonyPatch(typeof(ChemistryCookOperation), nameof(ChemistryCookOperation.Progress))]
    public class ChemistryStationFreezePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix()
        {
            return !EndOfDayGate.ShouldFreeze();
        }
    }
}
