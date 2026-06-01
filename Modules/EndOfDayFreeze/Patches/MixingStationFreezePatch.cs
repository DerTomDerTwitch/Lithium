using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.EndOfDayFreeze.Patches
{
    // Freezes both the Mk1 (MixingStation) and Mk2 (MixingStationMk2) mixers, whose OnMinPass advances
    // CurrentMixTime. Mk2 may inherit OnMinPass from MixingStation rather than declaring its own;
    // DeclaredMethod returns null in that case, so we patch the base method once (which covers Mk2 via
    // inheritance) and only add a second target when Mk2 actually declares an override. This avoids
    // patching the same MethodInfo twice while still covering both stations.
    [HarmonyPatch]
    public class MixingStationFreezePatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase mk1 = AccessTools.DeclaredMethod(typeof(MixingStation), nameof(MixingStation.OnMinPass));
            if (mk1 != null)
                yield return mk1;

            MethodBase mk2 = AccessTools.DeclaredMethod(typeof(MixingStationMk2), nameof(MixingStation.OnMinPass));
            if (mk2 != null)
                yield return mk2;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix()
        {
            return !EndOfDayGate.ShouldFreeze();
        }
    }
}
