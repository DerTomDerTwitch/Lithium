using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;

namespace Lithium.Modules.MixingStations.Patches
{
    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.GetMixTimeForCurrentOperation))]
    internal class MixingStationSpeedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MixingStation __instance, ref int __result)
        {
            ModMixingStationsConfiguration config = Core.Get<ModMixingStations>().Configuration;
            if (!config.Enabled)
                return;

            bool isMk2 = __instance.TryCast<MixingStationMk2>() != null;
            int speed = Mathf.Max(1, isMk2 ? config.Mk2MixStepsPerSecond : config.MixStepsPerSecond);
            __result = Mathf.Max(1, Mathf.CeilToInt(__result / (float)speed));
        }
    }
}
