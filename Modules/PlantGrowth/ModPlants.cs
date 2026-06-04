using Il2CppInterop.Runtime.Injection;
using Lithium.Helper;
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

        public float GrowthModifier = 0.7f;
        public float WaterDrainModifier = 4f;

        public static List<WeightedFloat> DefaultResultsPerBud() =>
        [
            new(80f, 1f),
            new(17f, 2f),
            new(3f,  3f),
        ];

        public static List<WeightedFloat> DefaultYieldModifiers() =>
        [
            new(2f,  0.5f),
            new(10f, 0.75f),
            new(13f, 1.0f),
            new(50f, 1.0f),
            new(15f, 1.25f),
            new(8f,  2.0f),
        ];

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

        public override void Validate()
        {
            GrowthModifier = ConfigValidator.AtLeast(Name, nameof(GrowthModifier), GrowthModifier, 0.001f);
            WaterDrainModifier = ConfigValidator.AtLeast(Name, nameof(WaterDrainModifier), WaterDrainModifier, 0f);
        }
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

            Configuration.RandomYieldPerBudPicker = new();
            Configuration.RandomYieldPerBudPicker.AddRange(
                Configuration.RandomYieldsPerBudModifier.Select(p => new KeyValuePair<float, float>(p.Value, p.Weight)));

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
