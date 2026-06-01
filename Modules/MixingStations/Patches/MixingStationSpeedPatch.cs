using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;

namespace Lithium.Modules.MixingStations.Patches
{
    // Previously this reimplemented the whole MixingStation.OnMinPass (advancing CurrentMixTime by
    // MixStepsPerSecond and duplicating the completion/clock/light logic) which is fragile across
    // updates. Vanilla advances CurrentMixTime by 1 per game-minute and completes once it reaches
    // GetMixTimeForCurrentOperation(), so simply shrinking that target by the configured speed makes
    // the operation finish proportionally faster while leaving all vanilla logic intact. Mirrors the
    // LabOven cook-duration patch.
    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.GetMixTimeForCurrentOperation))]
    internal class MixingStationSpeedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MixingStation __instance, ref int __result)
        {
            ModMixingStationsConfiguration config = Core.Get<ModMixingStations>().Configuration;
            if (!config.Enabled)
                return;

            // Mk2 inherits this method from MixingStation, so the postfix fires for both tiers;
            // pick the speed factor matching the instance's actual type.
            bool isMk2 = __instance.TryCast<MixingStationMk2>() != null;
            int speed = Mathf.Max(1, isMk2 ? config.Mk2MixStepsPerSecond : config.MixStepsPerSecond);
            __result = Mathf.Max(1, Mathf.CeilToInt(__result / (float)speed));
        }
    }
}
