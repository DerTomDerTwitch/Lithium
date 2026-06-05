using HarmonyLib;
using Oven = Il2CppScheduleOne.ObjectScripts.LabOven;

namespace Lithium.Modules.ElectricBill.Patches
{
    // The lab oven has no OnMinPass; its cook advances through OnTimePass(int) (which drives
    // CurrentOperation.UpdateCookProgress). OnTimePass is declared on LabOven itself, so __instance gives
    // us the property for the per-property power-cut gate.
    [HarmonyPatch(typeof(Oven), nameof(Oven.OnTimePass))]
    public class LabOvenFreezePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Oven __instance)
        {
            return !ElectricBillGate.IsCut(__instance);
        }
    }
}
