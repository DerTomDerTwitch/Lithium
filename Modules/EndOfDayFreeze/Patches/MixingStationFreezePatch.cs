using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.EndOfDayFreeze.Patches
{
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
