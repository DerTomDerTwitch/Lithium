using Il2CppInterop.Runtime.Injection;
using Lithium.Modules.PlantGrowth.Behaviours;
using Lithium.Util;
using Newtonsoft.Json;

namespace Lithium.Modules.PlantGrowth
{
    public class WeightedFloat
    {
        public float Weight;
        public float Value;

        [JsonConstructor]
        public WeightedFloat(float weight, float value)
        {
            Weight = weight;
            Value = value;
        }
    }

    public class ModPlantsConfiguration : ModuleConfiguration
    {
        public override string Name => "Plants";

        public float GrowthModifier = 1f;
        public float WaterDrainModifier = 1f;

        // Defaults are produced by factory methods (not shared static lists) so that clearing the
        // instance list in OnBeforeConfigurationLoaded can never corrupt the defaults themselves.

        // How many product items a single bud yields. Discrete weighted pick (WeightedPicker):
        // weight is the relative chance, value is the count. Most buds give 1, sometimes 2, rarely 3.
        public static List<WeightedFloat> DefaultResultsPerBud() =>
        [
            new(80f, 1f),
            new(17f, 2f),
            new(3f,  3f),
        ];

        // Overall plant yield multiplier, rolled once when the plant finishes growing. Interpolated
        // distribution (WeightedNormalizer): values must be ascending, the doubled 1.0 entry is the
        // wide "default amount" plateau, the ends are the rare big swings. ~50% stays 1.0, sometimes a
        // bit less/more (0.75 / 1.25), rarely a lot less/more (0.5 / 2.0).
        public static List<WeightedFloat> DefaultYieldModifiers() =>
        [
            new(2f,  0.5f),
            new(10f, 0.75f),
            new(13f, 1.0f),
            new(50f, 1.0f),
            new(15f, 1.25f),
            new(8f,  2.0f),
        ];

        // Quality offset added to the plant's quality level. The game's quality bands are wide
        // (Standard spans 0.40–0.75) so offsets stay modest; these were tightened so the spread isn't
        // "all over the place". Interpolated like the yield modifier: most harvests land near the
        // plant's own quality, sometimes a tier off (±0.15), rarely two (±0.3). Stacks on top of any
        // fertilizer/PGF QualityChange already baked into the plant's quality level.
        public static List<WeightedFloat> DefaultQualityModifiers() =>
        [
            new(2f,  -0.3f),
            new(10f, -0.15f),
            new(13f,  0.0f),
            new(50f,  0.0f),
            new(13f,  0.15f),
            new(10f,  0.3f),
        ];

        public List<WeightedFloat> RandomYieldsPerBudModifier = DefaultResultsPerBud();
        public List<WeightedFloat> RandomYieldModifiers = DefaultYieldModifiers();
        public List<WeightedFloat> RandomQualityModifiers = DefaultQualityModifiers();

        [JsonIgnore] public WeightedPicker<float> RandomYieldPerBudPicker;
        [JsonIgnore] public WeightedNormalizer RandomYieldModifierPicker;
        [JsonIgnore] public WeightedNormalizer RandomYieldQualityPicker;
    }

    public class ModPlants : ModuleBase<ModPlantsConfiguration>
    {
        public ModPlants()
        {
            ClassInjector.RegisterTypeInIl2Cpp<PlantModified>();
            ClassInjector.RegisterTypeInIl2Cpp<PlantBaseQuality>();
            ClassInjector.RegisterTypeInIl2Cpp<PotBaseValues>();
        }

        protected override void OnBeforeConfigurationLoaded()
        {
            base.OnBeforeConfigurationLoaded();
            Configuration.RandomYieldsPerBudModifier.Clear();
            Configuration.RandomYieldModifiers.Clear();
            Configuration.RandomQualityModifiers.Clear();
        }

        public override void Load()
        {
            base.Load();

            // Backfill any list the user emptied / that wasn't present in the JSON, then persist so the
            // written config always shows the working defaults.
            bool changed = false;
            if (IsInvalid(Configuration.RandomYieldsPerBudModifier))
            {
                Configuration.RandomYieldsPerBudModifier = ModPlantsConfiguration.DefaultResultsPerBud();
                changed = true;
            }
            if (IsInvalid(Configuration.RandomYieldModifiers))
            {
                Configuration.RandomYieldModifiers = ModPlantsConfiguration.DefaultYieldModifiers();
                changed = true;
            }
            if (IsInvalid(Configuration.RandomQualityModifiers))
            {
                Configuration.RandomQualityModifiers = ModPlantsConfiguration.DefaultQualityModifiers();
                changed = true;
            }
            if (changed)
                Configuration.SaveConfiguration();

            // Per-bud count: discrete weighted pick (value = count, weight = chance).
            Configuration.RandomYieldPerBudPicker = new();
            Configuration.RandomYieldPerBudPicker.AddRange(
                Configuration.RandomYieldsPerBudModifier.Select(p => new KeyValuePair<float, float>(p.Value, p.Weight)));

            // Yield and quality: interpolated weighted normalizers (Add(weight, value)).
            Configuration.RandomYieldModifierPicker = new();
            foreach (WeightedFloat entry in Configuration.RandomYieldModifiers)
                Configuration.RandomYieldModifierPicker.Add(entry.Weight, entry.Value);

            Configuration.RandomYieldQualityPicker = new();
            foreach (WeightedFloat entry in Configuration.RandomQualityModifiers)
                Configuration.RandomYieldQualityPicker.Add(entry.Weight, entry.Value);
        }

        private static bool IsInvalid(List<WeightedFloat> list) =>
            list == null || list.Count == 0 || list.Sum(e => e.Weight) <= 0f;

        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;
        }
    }
}
