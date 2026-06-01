using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using UnityEngine;

namespace Lithium.Util
{
    public static class SuccessChanceCalculator
    {
        public static float CalculateSuccess(EDrugType drugType, EQuality quality, float qualityLevelModifier,
            ECustomerStandard standard, string[] desires, string[] effects, CustomerAffinityData affinities,
            bool includeDrugPreference, float baseAcceptance, bool requireEffectMatch, int maxQualityOverDeliveryLevels,
            float drugAffinitySharpness)
        {
            float acceptance;

            if (desires.Length > 0)
            {
                int coveredEffects = 0;
                foreach (string desire in desires.Where(d => !string.IsNullOrEmpty(d)))
                {
                    coveredEffects += effects.Contains(desire) ? 1 : 0;
                }

                if (requireEffectMatch && coveredEffects == 0)
                {
                    Core.Logger.Msg("Sample offering: covers no desired effects - rejected (effect match required).");
                    return 0f;
                }

                acceptance = (float)coveredEffects / desires.Length;
                Core.Logger.Msg($"Sample offering: Covered {coveredEffects} desires. Base acceptance {acceptance*100:F1}%");
            }
            else
            {
                Core.Logger.Msg($"Sample offering: No desired. Base acceptance 100%");
                acceptance = 1f;
            }

            int qualityDiff = (int)quality - (int)standard;
            // Over-delivery is capped so a Heavenly sample to a Trash-standard NPC adds only a little
            // (and can't rescue a poorly-covered sample); under-delivery keeps its full penalty.
            int effectiveDiff = qualityDiff > 0 ? Math.Min(qualityDiff, maxQualityOverDeliveryLevels) : qualityDiff;
            Core.Logger.Msg($"Sample offering: Quality difference {qualityDiff} levels (effective {effectiveDiff})");
            acceptance += qualityLevelModifier * effectiveDiff;
            Core.Logger.Msg($"Adjusted acceptance: {acceptance*100:F1}%");

            if (includeDrugPreference)
            {
                foreach (ProductTypeAffinity productAffinity in affinities.ProductAffinities)
                {
                    if (productAffinity.DrugType == drugType)
                    {
                        // Affinity is signed (-1..1). Disliked/neutral (<= 0) rejects the sample;
                        // positive affinity is curved so acceptance climbs quickly (sharpness < 1).
                        float aff = productAffinity.Affinity;
                        float factor = aff <= 0f ? 0f : Mathf.Pow(Mathf.Clamp01(aff), drugAffinitySharpness);
                        Core.Logger.Msg($"Sample offering: Drug affinity {aff:0.##} -> factor {factor * 100:F1}%");
                        acceptance *= factor;
                        break;
                    }
                }
            }

            acceptance += baseAcceptance;
            Core.Logger.Msg($"Sample offering: Final acceptance is {Mathf.Clamp01(acceptance):F1}%");
            return Mathf.Clamp01(acceptance);
        }
    }
}
