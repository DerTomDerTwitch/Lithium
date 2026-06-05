using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.ElectricBill.Patches
{
    // The sprinkler waters via a momentary coroutine started in Water(); there is no per-minute driver to
    // freeze, so we block the activation itself while the property is powered off.
    [HarmonyPatch(typeof(Sprinkler), nameof(Sprinkler.Water))]
    public class SprinklerFreezePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Sprinkler __instance)
        {
            return !ElectricBillGate.IsCut(__instance);
        }
    }
}
