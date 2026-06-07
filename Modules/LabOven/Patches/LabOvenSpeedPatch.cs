using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;

namespace Lithium.Modules.LabOven.Patches
{
    /// <summary>
    /// Scales lab-oven cook speed by feeding a multiplied minute count into the cook each tick.
    ///
    /// Patches <see cref="Il2CppScheduleOne.ObjectScripts.LabOven.OnTimePass"/> rather than the previous
    /// target <c>OvenCookOperation.GetCookDuration</c>: that getter is a tiny method whose result is
    /// compared inline at the completion sites, so the IL2CPP build inlines it and the old postfix
    /// (which divided the duration) silently never affected when a cook finished. <c>OnTimePass</c> is
    /// the un-inlinable chokepoint — delegate-bound to <c>onTimeSkip</c> and reached per-minute via
    /// <c>OnUncappedMinPass → OnTimePass(1)</c> — and it calls <c>CurrentOperation.UpdateCookProgress(minutes)</c>,
    /// so multiplying <c>minutes</c> here reaches the (unchanged) cook duration sooner on both the awake
    /// and sleep-skip paths. A per-oven fractional carry preserves sub-minute progress across ticks.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppScheduleOne.ObjectScripts.LabOven), nameof(Il2CppScheduleOne.ObjectScripts.LabOven.OnTimePass))]
    public class LabOvenSpeedPatch
    {
        private static readonly Dictionary<IntPtr, float> Carry = new();

        [HarmonyPrefix]
        public static void Prefix(Il2CppScheduleOne.ObjectScripts.LabOven __instance, ref int minutes, bool __runOriginal)
        {
            // A higher-priority freeze prefix (ElectricBill power-cut) returned false: don't accrue carry.
            if (!__runOriginal)
                return;

            ModLabOvenConfiguration config = Core.Get<ModLabOven>().Configuration;
            if (!config.Enabled)
                return;

            float speed = Mathf.Max(0f, config.Speed);
            if (Mathf.Approximately(speed, 1f))
                return;

            IntPtr key = __instance.Pointer;
            Carry.TryGetValue(key, out float carry);

            float scaled = minutes * speed + carry;
            int applied = Mathf.FloorToInt(scaled);
            Carry[key] = scaled - applied;

            minutes = applied;
        }
    }
}
