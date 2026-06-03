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

    // Bonus ranges used ONLY when auto-generating the default combo set. Once generated, the combos are
    // written to the Combos array and editing those directly is the usual way to tune payouts — these
    // knobs only shape the first generation (or the next one after Combos is cleared). Min/Max are both
    // inclusive.
    public class ComboGenerationRanges
    {
        // Chance (0..1) that a generated combo uses 3 effects instead of 2. Three-effect combos are rarer
        // and pay more. 0.5 = an even split.
        public float ThreeEffectChance { get; set; } = 0.5f;

        // Per-unit fixed cash bonus rolled for each generated combo.
        public int TwoEffectFixedBonusMin { get; set; } = 5;
        public int TwoEffectFixedBonusMax { get; set; } = 15;
        public int ThreeEffectFixedBonusMin { get; set; } = 15;
        public int ThreeEffectFixedBonusMax { get; set; } = 30;

        // Percentage-of-payment bonus rolled for each generated combo.
        public float TwoEffectPercentageBonusMin { get; set; } = 3f;
        public float TwoEffectPercentageBonusMax { get; set; } = 8f;
        public float ThreeEffectPercentageBonusMin { get; set; } = 8f;
        public float ThreeEffectPercentageBonusMax { get; set; } = 15f;
    }

    public class ModEffectCombosConfiguration : ModuleConfiguration
    {
        public override string Name => "EffectCombos";
        public bool AffectsDealers { get; set; } = true;

        // How many combos to auto-generate when the config has none. Set to 0 to keep it empty.
        public int AutoGenerateCount { get; set; } = 20;

        // Bonus ranges applied during auto-generation (see ComboGenerationRanges).
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
        // Word lists the combo names are assembled from (Adjective + Noun, e.g. "Golden Tiger").
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

        // Used only if the effects can't be read from the game at runtime.
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
            // Feature was explicitly requested, so default it on (JSON can still turn it off).
            Configuration.Enabled = true;
        }

        public override void Apply()
        {
            // Generate the default combo set the first time (needs the game's effects, which exist
            // once a save is loaded). Runs regardless of Enabled so the config is populated and ready.
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

                bool three = effects.Count >= 3 && rng.NextDouble() < ranges.ThreeEffectChance; // 2 or 3 effects
                int effectCount = three ? 3 : 2;

                List<string> chosen = PickDistinct(effects, effectCount, rng);
                string signature = string.Join("+", chosen.OrderBy(e => e, StringComparer.OrdinalIgnoreCase));
                if (!usedEffectSets.Add(signature))
                    continue; // skip duplicate effect set

                string name = GenerateName(rng, usedNames);
                if (name == null)
                    break; // ran out of unique names

                combos.Add(new EffectCombo
                {
                    Name = name,
                    Effects = chosen.ToArray(),
                    // rng.Next upper bound is exclusive, so +1 to make the configured Max inclusive.
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
