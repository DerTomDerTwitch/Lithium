using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;

namespace Lithium.Modules.MixingStations.Patches
{
    /// <summary>
    /// Scales mixing-station speed by feeding a multiplied minute count into the mix each tick.
    ///
    /// Patches <see cref="MixingStation.OnTimePass"/> rather than the previous target
    /// <c>GetMixTimeForCurrentOperation</c>: that getter's result is compared inline at the completion
    /// sites (<c>IsReady</c>, <c>OnTimePass</c>, <c>IsCurrentMixingOperationComplete</c>), so the IL2CPP
    /// build inlines it and the old postfix (which divided the mix time) silently never affected when a
    /// mix completed. <c>OnTimePass</c> is the un-inlinable chokepoint — delegate-bound to <c>onTimeSkip</c>
    /// and reached per-minute via <c>OnMinPass → OnTimePass(1)</c> — and it advances
    /// <c>CurrentMixTime += minutes</c>, so multiplying <c>minutes</c> here reaches the (unchanged)
    /// threshold sooner on both the awake and sleep-skip paths.
    ///
    /// <c>MixingStationMk2</c> overrides <c>OnTimePass</c> only to wrap <c>base.OnTimePass(minutes)</c>, so
    /// patching the base method covers both; the Mk2 vs. base config is selected by casting the live
    /// instance, which avoids the double-scaling that patching both levels would cause.
    /// </summary>
    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.OnTimePass))]
    internal class MixingStationSpeedPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MixingStation __instance, ref int minutes, bool __runOriginal)
        {
            // A higher-priority freeze prefix (EndOfDay / ElectricBill power-cut) returned false.
            if (!__runOriginal)
                return;

            ModMixingStationsConfiguration config = Core.Get<ModMixingStations>().Configuration;
            if (!config.Enabled)
                return;

            bool isMk2 = __instance.TryCast<MixingStationMk2>() != null;
            int speed = Mathf.Max(1, isMk2 ? config.Mk2MixStepsPerSecond : config.MixStepsPerSecond);
            if (speed == 1)
                return;

            minutes = Mathf.Max(minutes, minutes * speed);
        }
    }
}
