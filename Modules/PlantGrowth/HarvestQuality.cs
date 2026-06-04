using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Lithium.Helper;
using UnityEngine;

namespace Lithium.Modules.PlantGrowth
{
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
