using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Lithium.Modules.PlantGrowth.Behaviours;
using Object = UnityEngine.Object;

namespace Lithium.Modules.PlantGrowth.Patches
{
    [HarmonyPatch(typeof(PlantHarvestable), nameof(PlantHarvestable.Harvest))]
    public class PlantHarvestablePatch
    {
        private static readonly Dictionary<object, bool> SkipFlags = [];
        private static readonly Dictionary<object, bool> GenerateFlags = [];

        // Set while a player hand-harvest is running so the botanist-side Plant.GetHarvestedProduct
        // patch knows not to also roll a quality bonus (the player path already does its own here).
        internal static bool PlayerHarvestInProgress;

        [HarmonyPrefix]
        public static bool Prefix(PlantHarvestable __instance)
        {
            ModPlantsConfiguration configuration = Core.Get<ModPlants>().Configuration;
            if (!configuration.Enabled)
                return true;

            PlayerHarvestInProgress = true;

            Plant componentInParent = __instance.GetComponentInParent<Plant>();
            // Clean base recomputed from scratch (vanilla 0.5 + additive QualityChange); ignores the
            // unreliable stored QualityLevel. Same calculation the botanist path uses.
            float baseQuality = HarvestQuality.ComputeBaseQuality(componentInParent);

            if (!componentInParent.TryGetComponent(out PlantBaseQuality comp))
            {
                comp = componentInParent.gameObject.AddComponent<PlantBaseQuality>();
                // Restore to the clean base after harvest — this also heals any plant whose stored
                // QualityLevel was corrupted by earlier builds.
                comp.Quality = baseQuality;
                comp.NeedsNotification = true;
            }

            if (!GenerateFlags.ContainsKey(__instance))
            {
                // Per-bud roll around the clean base.
                float offset = configuration.RandomYieldQualityPicker.Evaluate(UnityEngine.Random.value);
                componentInParent.QualityLevel = UnityEngine.Mathf.Clamp01(baseQuality + offset);
                __instance.ProductQuantity = (int)configuration.RandomYieldPerBudPicker.Pick();
                GenerateFlags[__instance] = true;
            }

            //if player cannot fit item ... skip harvest
            QualityItemInstance item = new QualityItemInstance(__instance.Product, __instance.ProductQuantity, ItemQuality.GetQuality(componentInParent.QualityLevel));
            if (!PlayerSingleton<PlayerInventory>.Instance.CanItemFitInInventory(item))
            {
                SkipFlags[__instance] = true;
                return false;
            }
            else
            {
                SkipFlags.Remove(__instance);
                return true;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(PlantHarvestable __instance)
        {
            PlayerHarvestInProgress = false;

            ModPlantsConfiguration configuration = Core.Get<ModPlants>().Configuration;
            if (!configuration.Enabled)
                return;

            if (SkipFlags.ContainsKey(__instance))
                return;

            if (!GenerateFlags.Remove(__instance))
                return;

            Plant componentInParent = __instance.GetComponentInParent<Plant>();
            if (componentInParent.TryGetComponent(out PlantBaseQuality comp))
            {
                // One notification per plant, showing the first bud's rolled quality.
                if (comp.NeedsNotification)
                {
                    EQuality quality = ItemQuality.GetQuality(componentInParent.QualityLevel);

                    NotificationsManager.Instance.SendNotification($"{__instance.ProductQuantity}x {componentInParent.SeedDefinition.Name}",
                        $"{quality:G} quality", componentInParent.SeedDefinition.Icon, 2f, false);
                    comp.NeedsNotification = false;
                }

                // Restore the plant's base quality after EVERY bud — not only the notifying one.
                // A whole-plant harvest fires Harvest once per bud, all in the same frame, and
                // Object.Destroy is deferred to end-of-frame, so every bud after the first reuses
                // this same component. Gating the restore on NeedsNotification (true only for the
                // first bud) left the per-bud quality offset stacking across buds, so quality
                // random-walked into Trash/Heavenly. Restoring unconditionally keeps each bud's
                // roll independent around the true base quality.
                componentInParent.QualityLevel = comp.Quality;
                Object.Destroy(comp);
            }
        }
    }
}
