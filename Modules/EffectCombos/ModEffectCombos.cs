using Il2CppScheduleOne.Effects;
using Lithium.Helper;
using Lithium.Modules.Customers;
using Lithium.Modules.EffectCombos.BonusPayments;
using MelonLoader;
using UnityEngine;

namespace Lithium.Modules.EffectCombos
{
    public class EffectCombo
    {
        public string Name { get; set; }
        public int FixedBonus { get; set; } = 0;
        public float PercentageBonus { get; set; } = 0f;
        public string[] Effects { get; set; } = [];
    }

    public class ComboGenerationRanges
    {
        public float ThreeEffectChance { get; set; } = 0.5f;

        public int TwoEffectFixedBonusMin { get; set; } = 5;
        public int TwoEffectFixedBonusMax { get; set; } = 15;
        public int ThreeEffectFixedBonusMin { get; set; } = 15;
        public int ThreeEffectFixedBonusMax { get; set; } = 30;

        public float TwoEffectPercentageBonusMin { get; set; } = 3f;
        public float TwoEffectPercentageBonusMax { get; set; } = 8f;
        public float ThreeEffectPercentageBonusMin { get; set; } = 8f;
        public float ThreeEffectPercentageBonusMax { get; set; } = 15f;
    }

    public class ModEffectCombosConfiguration : ModuleConfiguration
    {
        public override string Name => "EffectCombos";
        public bool AffectsDealers { get; set; } = true;

        public int AutoGenerateCount { get; set; } = 20;

        public ComboGenerationRanges GenerationRanges { get; set; } = new ComboGenerationRanges();

        public EffectCombo[] Combos { get; set; } = [];

        public override void Validate()
        {
            AutoGenerateCount = ConfigValidator.AtLeast(Name, nameof(AutoGenerateCount), AutoGenerateCount, 0);

            ComboGenerationRanges r = GenerationRanges;
            r.ThreeEffectChance = ConfigValidator.InRange(Name, "GenerationRanges.ThreeEffectChance", r.ThreeEffectChance, 0f, 1f);

            int twoFixedMin = ConfigValidator.AtLeast(Name, "GenerationRanges.TwoEffectFixedBonusMin", r.TwoEffectFixedBonusMin, 0);
            int twoFixedMax = ConfigValidator.AtLeast(Name, "GenerationRanges.TwoEffectFixedBonusMax", r.TwoEffectFixedBonusMax, 0);
            ConfigValidator.EnsureOrdered(Name, "GenerationRanges.TwoEffectFixedBonusMin", "GenerationRanges.TwoEffectFixedBonusMax", ref twoFixedMin, ref twoFixedMax);
            r.TwoEffectFixedBonusMin = twoFixedMin;
            r.TwoEffectFixedBonusMax = twoFixedMax;

            int threeFixedMin = ConfigValidator.AtLeast(Name, "GenerationRanges.ThreeEffectFixedBonusMin", r.ThreeEffectFixedBonusMin, 0);
            int threeFixedMax = ConfigValidator.AtLeast(Name, "GenerationRanges.ThreeEffectFixedBonusMax", r.ThreeEffectFixedBonusMax, 0);
            ConfigValidator.EnsureOrdered(Name, "GenerationRanges.ThreeEffectFixedBonusMin", "GenerationRanges.ThreeEffectFixedBonusMax", ref threeFixedMin, ref threeFixedMax);
            r.ThreeEffectFixedBonusMin = threeFixedMin;
            r.ThreeEffectFixedBonusMax = threeFixedMax;

            float twoPctMin = ConfigValidator.AtLeast(Name, "GenerationRanges.TwoEffectPercentageBonusMin", r.TwoEffectPercentageBonusMin, 0f);
            float twoPctMax = ConfigValidator.AtLeast(Name, "GenerationRanges.TwoEffectPercentageBonusMax", r.TwoEffectPercentageBonusMax, 0f);
            ConfigValidator.EnsureOrdered(Name, "GenerationRanges.TwoEffectPercentageBonusMin", "GenerationRanges.TwoEffectPercentageBonusMax", ref twoPctMin, ref twoPctMax);
            r.TwoEffectPercentageBonusMin = twoPctMin;
            r.TwoEffectPercentageBonusMax = twoPctMax;

            float threePctMin = ConfigValidator.AtLeast(Name, "GenerationRanges.ThreeEffectPercentageBonusMin", r.ThreeEffectPercentageBonusMin, 0f);
            float threePctMax = ConfigValidator.AtLeast(Name, "GenerationRanges.ThreeEffectPercentageBonusMax", r.ThreeEffectPercentageBonusMax, 0f);
            ConfigValidator.EnsureOrdered(Name, "GenerationRanges.ThreeEffectPercentageBonusMin", "GenerationRanges.ThreeEffectPercentageBonusMax", ref threePctMin, ref threePctMax);
            r.ThreeEffectPercentageBonusMin = threePctMin;
            r.ThreeEffectPercentageBonusMax = threePctMax;
        }
    }

    public class ModEffectCombos : ModuleBase<ModEffectCombosConfiguration>
    {
        private static readonly string[] NameAdjectives =
        {
            "Golden", "Cosmic", "Velvet", "Midnight", "Electric", "Crimson", "Royal", "Frosted",
            "Blazing", "Mystic", "Silver", "Lunar", "Savage", "Hazy", "Diamond", "Wicked", "Turbo",
            "Phantom", "Emerald", "Neon", "Sacred", "Feral", "Glacial", "Solar"
        };

        private static readonly string[] NameNouns =
        {
            "Tiger", "Haze", "Dream", "Storm", "Punch", "Rush", "Bliss", "Nova", "Mirage", "Comet",
            "Dragon", "Voyage", "Frenzy", "Cyclone", "Reverie", "Jackpot", "Tornado", "Serpent",
            "Phoenix", "Avalanche", "Tsunami", "Eclipse", "Whirlwind", "Surge"
        };

        private static readonly string[] FallbackEffects =
        {
            "Anti-Gravity", "Athletic", "Balding", "Bright-Eyed", "Calming", "Calorie-Dense",
            "Cyclopean", "Disorienting", "Electrifying", "Energizing", "Euphoric", "Explosive",
            "Focused", "Foggy", "Gingeritis", "Glowing", "Jennerising", "Laxative", "Long Faced",
            "Munchies", "Paranoia", "Refreshing", "Schizophrenia", "Sedating", "Seizure-Inducing",
            "Shrinking", "Slippery", "Smelly", "Sneaky", "Spicy", "Thought-Provoking", "Toxic",
            "Tropic Thunder", "Zombifying"
        };

        protected override void OnBeforeConfigurationLoaded()
        {
            base.OnBeforeConfigurationLoaded();
            Configuration.Enabled = true;
        }

        public override void Apply()
        {
            if (Configuration.AutoGenerateCount > 0 && (Configuration.Combos == null || Configuration.Combos.Length == 0))
                GenerateCombos(Configuration.AutoGenerateCount);

            if (!Configuration.Enabled)
                return;

            Core.Get<ModCustomers>().RegisterBonusPaymentHandler(new EffectComboBonus());
        }

        private void GenerateCombos(int count)
        {
            List<string> effects = GetAvailableEffectNames();
            if (effects.Count < 2)
            {
                Log.Warning("[Lithium] EffectCombos: not enough effects available to generate combos yet.");
                return;
            }

            System.Random rng = new();
            ComboGenerationRanges ranges = Configuration.GenerationRanges;
            List<EffectCombo> combos = [];
            HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> usedEffectSets = [];

            int attempts = 0;
            int maxAttempts = count * 100;
            while (combos.Count < count && attempts < maxAttempts)
            {
                attempts++;

                bool three = effects.Count >= 3 && rng.NextDouble() < ranges.ThreeEffectChance;
                int effectCount = three ? 3 : 2;

                List<string> chosen = PickDistinct(effects, effectCount, rng);
                string signature = string.Join("+", chosen.OrderBy(e => e, StringComparer.OrdinalIgnoreCase));
                if (!usedEffectSets.Add(signature))
                    continue;

                string name = GenerateName(rng, usedNames);
                if (name == null)
                    break;

                combos.Add(new EffectCombo
                {
                    Name = name,
                    Effects = chosen.ToArray(),
                    FixedBonus = three
                        ? rng.Next(ranges.ThreeEffectFixedBonusMin, ranges.ThreeEffectFixedBonusMax + 1)
                        : rng.Next(ranges.TwoEffectFixedBonusMin, ranges.TwoEffectFixedBonusMax + 1),
                    PercentageBonus = three
                        ? RollFloat(rng, ranges.ThreeEffectPercentageBonusMin, ranges.ThreeEffectPercentageBonusMax)
                        : RollFloat(rng, ranges.TwoEffectPercentageBonusMin, ranges.TwoEffectPercentageBonusMax),
                });
            }

            Configuration.Combos = combos.ToArray();
            Configuration.SaveConfiguration();
            Log.Info($"[Lithium] EffectCombos: generated {combos.Count} effect combos.");
        }

        private static List<string> GetAvailableEffectNames()
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Effect effect in Resources.FindObjectsOfTypeAll<Effect>())
                {
                    if (effect != null && !string.IsNullOrWhiteSpace(effect.Name))
                        names.Add(effect.Name);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[Lithium] EffectCombos: failed to read effects from game ({e.Message}); using fallback list.");
            }

            if (names.Count < 2)
                return [.. FallbackEffects];

            return [.. names];
        }

        private static float RollFloat(System.Random rng, float min, float max) =>
            (float)(min + rng.NextDouble() * (max - min));

        private static List<string> PickDistinct(List<string> source, int count, System.Random rng)
        {
            List<string> pool = [.. source];
            List<string> result = new(count);
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int idx = rng.Next(pool.Count);
                result.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            return result;
        }

        private static string GenerateName(System.Random rng, HashSet<string> used)
        {
            for (int i = 0; i < 100; i++)
            {
                string name = $"{NameAdjectives[rng.Next(NameAdjectives.Length)]} {NameNouns[rng.Next(NameNouns.Length)]}";
                if (used.Add(name))
                    return name;
            }
            return null;
        }
    }
}
