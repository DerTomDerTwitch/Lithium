using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;

namespace Lithium.Modules.ChemistryStation.Patches
{
    [HarmonyPatch(typeof(ChemistryCookOperation), nameof(ChemistryCookOperation.Progress))]
    public class ChemistryCookOperationProgressPatch
    {
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
