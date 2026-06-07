using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ChemStation = Il2CppScheduleOne.ObjectScripts.ChemistryStation;

namespace Lithium.Modules.ChemistryStation.Patches
{
    /// <summary>
    /// Scales chemistry cook speed by feeding a multiplied minute count into the cook each tick.
    ///
    /// Patches <see cref="ChemistryStation.OnTimePass"/> rather than the previous target
    /// <c>ChemistryCookOperation.Progress</c>: that method is a 3-line public helper whose only caller
    /// is <c>OnTimePass</c>, so the IL2CPP build inlines it and the old prefix silently never ran.
    /// <c>OnTimePass</c> is the un-inlinable chokepoint — delegate-bound to <c>onTimeSkip</c> and reached
    /// per-minute via <c>OnMinPass → OnTimePass(1)</c> — and it calls <c>CurrentCookOperation.Progress(minutes)</c>,
    /// so multiplying <c>minutes</c> here advances the cook faster for both the awake and sleep-skip paths.
    /// A per-station fractional carry preserves sub-minute progress across ticks.
    /// </summary>
    [HarmonyPatch(typeof(ChemStation), nameof(ChemStation.OnTimePass))]
    public class ChemistryStationSpeedPatch
    {
        private static readonly Dictionary<IntPtr, float> Carry = new();

        [HarmonyPrefix]
        public static void Prefix(ChemStation __instance, ref int minutes, bool __runOriginal)
        {
            // A higher-priority freeze prefix (EndOfDay / ElectricBill power-cut) returned false: the
            // cook won't run this tick, so don't accrue carry against minutes that won't be applied.
            if (!__runOriginal)
                return;

            ModChemistryStation module = Core.Get<ModChemistryStation>();
            if (module == null || !module.Configuration.Enabled)
                return;

            float speed = Mathf.Max(0f, module.Configuration.Speed);
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
