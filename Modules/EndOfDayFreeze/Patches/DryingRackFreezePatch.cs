using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.EndOfDayFreeze.Patches
{
    // DryingRack.OnMinPass is also patched by ModDryingRacks, whose prefix reimplements drying and
    // returns false. Running first (Priority.First) and returning false during the freeze skips that
    // reimplementation too, so DryingOperation.Time never advances while the clock is frozen at 4 AM.
    [HarmonyPatch(typeof(DryingRack), nameof(DryingRack.OnMinPass))]
    public class DryingRackFreezePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix()
        {
            return !EndOfDayGate.ShouldFreeze();
        }
    }
}
