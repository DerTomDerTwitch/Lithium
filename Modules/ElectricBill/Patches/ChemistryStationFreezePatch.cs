using HarmonyLib;
using ChemStation = Il2CppScheduleOne.ObjectScripts.ChemistryStation;

namespace Lithium.Modules.ElectricBill.Patches
{
    // Freezes a chemistry station's cook while its property is powered off for an unpaid electric bill.
    // OnTimePass is the chokepoint for both the per-minute path (OnMinPass -> OnTimePass(1)) and the
    // sleep-skip path (onTimeSkip -> OnTimePass(n)), so gating it stops cooking in both cases.
    [HarmonyPatch(typeof(ChemStation), nameof(ChemStation.OnTimePass))]
    public class ChemistryStationFreezePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(ChemStation __instance)
        {
            return !ElectricBillGate.IsCut(__instance);
        }
    }
}
