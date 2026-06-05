using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.ElectricBill.Patches
{
    // Freezes the standard mixing station and the Mk II while their property is powered off. OnTimePass
    // covers both the per-minute and the sleep-skip mix paths.
    [HarmonyPatch]
    public class MixingStationFreezePatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase mk1 = AccessTools.DeclaredMethod(typeof(MixingStation), nameof(MixingStation.OnTimePass));
            if (mk1 != null)
                yield return mk1;

            MethodBase mk2 = AccessTools.DeclaredMethod(typeof(MixingStationMk2), nameof(MixingStation.OnTimePass));
            if (mk2 != null)
                yield return mk2;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(MixingStation __instance)
        {
            return !ElectricBillGate.IsCut(__instance);
        }
    }
}
