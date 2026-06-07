using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Growing;
using Lithium.Modules.PlantGrowth.Behaviours;
using UnityEngine;

namespace Lithium.Modules.PlantGrowth.Patches
{
    /// <summary>
    /// Applies the one-time overall yield multiplier (and injects the <see cref="PlantModified"/> marker)
    /// the moment a plant finishes growing.
    ///
    /// Patches <see cref="Plant.SetNormalizedGrowthProgress"/> rather than the previous target
    /// <c>Plant.GrowthDone</c>: <c>GrowthDone</c> is a <c>private</c> helper with a single internal caller
    /// (<c>SetNormalizedGrowthProgress</c>), so the IL2CPP build can inline it and the prefix would silently
    /// never run. <c>SetNormalizedGrowthProgress</c> is a <c>public virtual</c> method with several callers
    /// (far less inlinable) and is exactly where vanilla decides to fire <c>GrowthDone</c> — on the frame
    /// growth crosses to full. This prefix detects that same transition and applies the multiplier <b>before</b>
    /// the original runs <c>GrowthDone</c>, so the bumped <c>YieldMultiplier</c> is in place when GrowthDone
    /// sizes the harvestable count from <c>BaseYieldQuantity * YieldMultiplier</c>. The <see cref="PlantModified"/>
    /// guard keeps it idempotent.
    /// </summary>
    [HarmonyPatch(typeof(Plant), nameof(Plant.SetNormalizedGrowthProgress))]
    public class PlantGrowthDonePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Plant __instance, float progress)
        {
            ModPlantsConfiguration configuration = Core.Get<ModPlants>().Configuration;
            if (!configuration.Enabled)
                return;

            if (!InstanceFinder.IsServer)
                return;

            if (__instance == null)
                return;

            // Mirror the vanilla GrowthDone trigger: only when this call pushes growth from below full to
            // full (NormalizedGrowthProgress still holds the pre-call value inside the prefix).
            if (progress < 1f || __instance.NormalizedGrowthProgress >= 1f)
                return;

            if (__instance.GetComponent<PlantModified>() != null)
                return;

            __instance.gameObject.AddComponent<PlantModified>();

            __instance.YieldMultiplier *= configuration.RandomYieldModifierPicker.Evaluate(UnityEngine.Random.value);
        }
    }
}
