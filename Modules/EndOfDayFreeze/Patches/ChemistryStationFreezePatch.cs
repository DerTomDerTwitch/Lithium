using HarmonyLib;
using ChemStation = Il2CppScheduleOne.ObjectScripts.ChemistryStation;

namespace Lithium.Modules.EndOfDayFreeze.Patches
{
    // Freezes a chemistry station's cook while the game clock is frozen at the 4 AM end-of-day stall.
    // Targets OnTimePass (not the inlinable ChemistryCookOperation.Progress, which IL2CPP folds into
    // OnTimePass and would bypass this prefix): OnTimePass is the un-inlinable chokepoint for both the
    // per-minute path (OnMinPass -> OnTimePass(1)) and the sleep-skip path (onTimeSkip -> OnTimePass(n)),
    // mirroring the ElectricBill power-cut freeze on the same method.
    [HarmonyPatch(typeof(ChemStation), nameof(ChemStation.OnTimePass))]
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
