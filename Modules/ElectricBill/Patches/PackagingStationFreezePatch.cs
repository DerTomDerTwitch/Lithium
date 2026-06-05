using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Packaging;

namespace Lithium.Modules.ElectricBill.Patches
{
    // Blocks packaging (player- or employee-driven) while the property is powered off, for both the
    // standard packaging station and the Mk II. StartTask blocks a new packaging job; PackSingleInstance
    // blocks each unit commit of an in-flight job.
    [HarmonyPatch]
    public class PackagingStationFreezePatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase start = AccessTools.DeclaredMethod(typeof(PackagingStation), nameof(PackagingStation.StartTask));
            if (start != null)
                yield return start;

            MethodBase pack = AccessTools.DeclaredMethod(typeof(PackagingStation), nameof(PackagingStation.PackSingleInstance));
            if (pack != null)
                yield return pack;

            MethodBase startMk2 = AccessTools.DeclaredMethod(typeof(PackagingStationMk2), nameof(PackagingStation.StartTask));
            if (startMk2 != null)
                yield return startMk2;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PackagingStation __instance)
        {
            return !ElectricBillGate.IsCut(__instance);
        }
    }
}
