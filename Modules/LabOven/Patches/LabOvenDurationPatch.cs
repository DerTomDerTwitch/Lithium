using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;

namespace Lithium.Modules.LabOven.Patches
{
    /// <summary>
    /// Gives the lab oven a flat total cook duration (<see cref="ModLabOvenConfiguration.CookDurationMinutes"/>)
    /// instead of the old speed multiplier.
    ///
    /// Vanilla completes a cook when <c>CookProgress >= GetCookDuration()</c> (the ingredient's
    /// <c>CookableModule.CookTime</c>), advancing one minute of progress per tick. To make the whole cook
    /// take <c>CookDurationMinutes</c> regardless of ingredient, we feed a scaled minute count into the cook:
    /// <c>speed = vanillaDuration / CookDurationMinutes</c>, so progress reaches the (unchanged) threshold in
    /// exactly the configured number of in-game minutes.
    ///
    /// Patches <c>OnTimePass</c> rather than <c>GetCookDuration</c>: that getter is inlined at the completion
    /// sites in the IL2CPP build, so a patch on it silently never affects when a cook finishes. <c>OnTimePass</c>
    /// is the un-inlinable chokepoint — delegate-bound to <c>onTimeSkip</c> and reached per-minute via
    /// <c>OnUncappedMinPass → OnTimePass(1)</c> — and it calls <c>CurrentOperation.UpdateCookProgress(minutes)</c>,
    /// so scaling <c>minutes</c> here works on both the awake and sleep-skip paths. A per-oven fractional carry
    /// preserves sub-minute progress across ticks (so a slow cook never stalls by repeatedly flooring to 0).
    /// </summary>
    [HarmonyPatch(typeof(Il2CppScheduleOne.ObjectScripts.LabOven), nameof(Il2CppScheduleOne.ObjectScripts.LabOven.OnTimePass))]
    public class LabOvenDurationPatch
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

            OvenCookOperation operation = __instance.CurrentOperation;
            if (operation == null)
                return;

            float target = config.CookDurationMinutes;
            if (target <= 0f)
                return;

            float baseDuration = operation.GetCookDuration();
            if (baseDuration <= 0f)
                return;

            float speed = baseDuration / target;
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
