using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Lithium.Helper;
using UnityEngine;

namespace Lithium.Modules.PlantGrowth
{
    /// <summary>
    /// Shared harvest-quality calculation used by BOTH the player hand-harvest path
    /// (PlantHarvestablePatch) and the botanist whole-plant path (PlantGetHarvestedProductPatch) so
    /// the two always produce quality from identical logic.
    ///
    /// We deliberately do NOT read the plant's stored QualityLevel as the base. It is unreliable
    /// (older builds could leak per-bud bumps into it, and it can drift out of range), so instead we
    /// rebuild a clean base every harvest: the vanilla Standard base (Plant.BaseQualityLevel = 0.5)
    /// plus the QualityChange of every additive applied to the pot. Additives that don't affect
    /// quality (e.g. pure speed-grow) simply contribute 0.
    /// </summary>
    public static class HarvestQuality
    {
        public static float ComputeBaseQuality(Plant plant)
        {
            float quality = Plant.BaseQualityLevel;

            Pot pot = plant != null ? plant.Pot : null;
            if (pot != null && pot.AppliedAdditives != null)
            {
                foreach (AdditiveDefinition additive in pot.AppliedAdditives.ToList())
                {
                    if (additive != null)
                        quality += additive.QualityChange;
                }
            }

            return Mathf.Clamp01(quality);
        }
    }
}
