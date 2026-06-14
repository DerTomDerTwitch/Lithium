using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ChemStation = Il2CppScheduleOne.ObjectScripts.ChemistryStation;

namespace Lithium.Modules.ChemistryStation.Patches
{
    /// <summary>
    /// Gives the chemistry station a flat total cook duration
    /// (<see cref="ModChemistryStationConfiguration.CookDurationMinutes"/>) instead of the old speed multiplier.
    ///
    /// Vanilla completes a cook when <c>CurrentTime >= Recipe.CookTime_Mins</c>, advancing
    /// <c>CurrentTime += minutes</c> each tick. To make the whole cook take <c>CookDurationMinutes</c>
    /// regardless of recipe, we feed a scaled minute count into the cook:
    /// <c>speed = Recipe.CookTime_Mins / CookDurationMinutes</c>, so <c>CurrentTime</c> reaches the (unchanged)
    /// threshold in exactly the configured number of in-game minutes.
    ///
    /// Patches <c>OnTimePass</c> — the un-inlinable chokepoint, delegate-bound to <c>onTimeSkip</c> and reached
    /// per-minute via <c>OnMinPass → OnTimePass(1)</c> — which calls <c>CurrentCookOperation.Progress(minutes)</c>,
    /// so scaling <c>minutes</c> here advances the cook on both the awake and sleep-skip paths. A per-station
    /// fractional carry preserves sub-minute progress across ticks (so a slow cook never stalls by flooring to 0).
    /// </summary>
    [HarmonyPatch(typeof(ChemStation), nameof(ChemStation.OnTimePass))]
    public class ChemistryStationDurationPatch
    {
        private static readonly Dictionary<IntPtr, float> Carry = new();

        [HarmonyPrefix]
        public static void Prefix(ChemStation __instance, ref int minutes, bool __runOriginal)
        {
            // A higher-priority freeze prefix (EndOfDay / ElectricBill power-cut) returned false: the cook won't
            // run this tick, so don't accrue carry against minutes that won't be applied.
            if (!__runOriginal)
                return;

            ModChemistryStation module = Core.Get<ModChemistryStation>();
            if (module == null || !module.Configuration.Enabled)
                return;

            var operation = __instance.CurrentCookOperation;
            if (operation == null)
                return;

            var recipe = operation.Recipe;
            if (recipe == null)
                return;

            float target = module.Configuration.CookDurationMinutes;
            if (target <= 0f)
                return;

            float baseDuration = recipe.CookTime_Mins;
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
