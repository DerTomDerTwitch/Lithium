using HarmonyLib;
using Il2CppScheduleOne.Property;

namespace Lithium.Modules.ElectricBill.Patches
{
    // Laundering advances in Business.MinPass (which only steps the business's LaunderingOperations).
    // Business IS-A Property, so __instance gives the property code for the power-cut gate. Freezing it
    // pauses laundering at a powered-off property's laundering station.
    [HarmonyPatch(typeof(Business), nameof(Business.MinPass))]
    public class LaunderingFreezePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Business __instance)
        {
            return !ElectricBillGate.IsCut(__instance);
        }
    }
}
