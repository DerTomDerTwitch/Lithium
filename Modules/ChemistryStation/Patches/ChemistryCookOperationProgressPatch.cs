using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;

namespace Lithium.Modules.ChemistryStation.Patches
{
    // ChemistryCookOperation has no GetCookDuration to scale (unlike OvenCookOperation/MixingStation),
    // so we turn Progress into a real speed factor instead of the old flat "bonus steps per tick".
    // Vanilla advances the integer CurrentTime by `mins` each call and completes once it reaches the
    // recipe duration; we rescale that advance to mins * Speed. Because CurrentTime is an integer, a
    // per-operation fractional carry is accumulated so slow-down (Speed < 1) stays accurate instead of
    // truncating to zero and stalling, while speed-up (Speed > 1) and pause (Speed == 0) also work —
    // all without duplicating any vanilla completion logic.
    [HarmonyPatch(typeof(ChemistryCookOperation), nameof(ChemistryCookOperation.Progress))]
    public class ChemistryCookOperationProgressPatch
    {
        // Keyed by the native object pointer (stable for the operation's lifetime) rather than the
        // managed wrapper, then pruned on completion.
        private static readonly Dictionary<IntPtr, float> Carry = new();

        [HarmonyPrefix]
        public static void Prefix(ChemistryCookOperation __instance, ref int mins)
        {
            ModChemistryStation module = Core.Get<ModChemistryStation>();
            if (module == null || !module.Configuration.Enabled)
                return;

            float speed = Mathf.Max(0f, module.Configuration.Speed);
            if (Mathf.Approximately(speed, 1f))
                return;

            IntPtr key = __instance.Pointer;
            Carry.TryGetValue(key, out float carry);

            float scaled = mins * speed + carry;
            int applied = Mathf.FloorToInt(scaled);
            Carry[key] = scaled - applied;

            mins = applied;
        }

        [HarmonyPostfix]
        public static void Postfix(ChemistryCookOperation __instance)
        {
            if (Carry.Count > 0 && __instance.IsComplete())
                Carry.Remove(__instance.Pointer);
        }
    }
}
