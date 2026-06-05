using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.ElectricBill.Patches
{
    // Freezes a cauldron's cook while its property is powered off. OnTimePass covers both the per-minute
    // and the sleep-skip cook paths.
    [HarmonyPatch(typeof(Cauldron), nameof(Cauldron.OnTimePass))]
    public class CauldronFreezePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Cauldron __instance)
        {
            return !ElectricBillGate.IsCut(__instance);
        }
    }
}
