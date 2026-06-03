using System;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Property;
using Lithium.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Lithium.Modules.Banking
{
    /// <summary>How a bank transfer fee is calculated.</summary>
    public enum EFeeMode
    {
        /// <summary>Fee is a percentage of the transferred amount (with optional min/max bounds).</summary>
        Percent,
        /// <summary>Fee is a flat amount regardless of how much is transferred.</summary>
        Fixed
    }

    /// <summary>
    /// A fee charged on ATM transactions (deposits convert cash → bank balance, withdrawals the reverse).
    /// The fee is always taken from the online (bank) balance. Configure either a percentage with optional
    /// floor/cap (e.g. "5%, at least $5, at most $50") or a flat amount (e.g. "always $5").
    /// </summary>
    public class TransferFeeConfiguration
    {
        [JsonProperty(Order = 1)] public bool Enabled = false;

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty(Order = 2)] public EFeeMode Mode = EFeeMode.Percent;

        // Percent mode:
        [JsonProperty(Order = 3)] public float Percent = 5f;   // 5 = 5%
        [JsonProperty(Order = 4)] public float MinFee = 0f;    // 0 = no floor
        [JsonProperty(Order = 5)] public float MaxFee = 0f;    // 0 = no cap

        // Fixed mode:
        [JsonProperty(Order = 6)] public float FixedAmount = 5f;

        [JsonProperty(Order = 7)] public bool ApplyToDeposits = true;
        [JsonProperty(Order = 8)] public bool ApplyToWithdrawals = false;

        /// <summary>Returns the fee to charge for a transaction of <paramref name="amount"/> (never more than the amount itself).</summary>
        public float Compute(float amount)
        {
            if (!Enabled || amount <= 0f)
                return 0f;

            float fee;
            if (Mode == EFeeMode.Fixed)
            {
                fee = FixedAmount;
            }
            else
            {
                fee = amount * (Percent / 100f);
                if (MinFee > 0f && fee < MinFee) fee = MinFee;
                if (MaxFee > 0f && fee > MaxFee) fee = MaxFee;
            }

            if (fee < 0f) fee = 0f;
            if (fee > amount) fee = amount;
            return fee;
        }

        public void Validate(string config)
        {
            Percent = ConfigValidator.InRange(config, $"TransferFee.{nameof(Percent)}", Percent, 0f, 100f);
            MinFee = ConfigValidator.AtLeast(config, $"TransferFee.{nameof(MinFee)}", MinFee, 0f);
            MaxFee = ConfigValidator.AtLeast(config, $"TransferFee.{nameof(MaxFee)}", MaxFee, 0f);
            FixedAmount = ConfigValidator.AtLeast(config, $"TransferFee.{nameof(FixedAmount)}", FixedAmount, 0f);
        }
    }

    /// <summary>ATM deposit limits. A value of -1 means "no limit" for that period.</summary>
    public class AtmConfiguration
    {
        // The vanilla game enforces only a weekly deposit limit (default $10,000). -1 disables it.
        [JsonProperty(Order = 1)] public float WeeklyDepositLimit = 10000f;
        // Added by Lithium: a per-day deposit cap on top of the weekly one. -1 disables it.
        [JsonProperty(Order = 2)] public float DailyDepositLimit = -1f;

        public bool WeeklyLimited => WeeklyDepositLimit >= 0f;
        public bool DailyLimited => DailyDepositLimit >= 0f;

        public void Validate()
        {
            // Normalise any negative value to the canonical -1 ("unlimited").
            if (WeeklyDepositLimit < 0f) WeeklyDepositLimit = -1f;
            if (DailyDepositLimit < 0f) DailyDepositLimit = -1f;
        }
    }

    /// <summary>Per-business laundering overrides, keyed by the business' display name in the laundering UI.</summary>
    public class BusinessLaunderingConfiguration
    {
        /// <summary>When true, replaces the business' laundering capacity (max cash in flight) with <see cref="Capacity"/>.</summary>
        [JsonProperty(Order = 1)] public bool OverrideCapacity = false;
        [JsonProperty(Order = 2)] public float Capacity = 0f;
        /// <summary>Multiplies laundering throughput speed. &gt;1 finishes faster, &lt;1 slower, 1 = vanilla.</summary>
        [JsonProperty(Order = 3)] public float SpeedMultiplier = 1f;
    }

    /// <summary>
    /// Optional scaling of laundering capacity and/or speed by the player's current rank. Each dictionary maps a
    /// rank name (see <see cref="ERank"/>: Street_Rat, Hoodlum, Peddler, Hustler, Bagman, Enforcer, Shot_Caller,
    /// Block_Boss, Underlord, Baron, Kingpin) to a multiplier. The multiplier of the highest rank you have reached
    /// applies (a step function); ranks you have not reached yet are ignored. Multipliers stack on top of the
    /// per-business values.
    /// </summary>
    public class LaunderingXpScalingConfiguration
    {
        [JsonProperty(Order = 1)] public bool Enabled = false;
        [JsonProperty(Order = 2)] public Dictionary<string, float> CapacityMultiplierByRank = new()
        {
            { nameof(ERank.Hustler), 1.5f },
            { nameof(ERank.Kingpin), 3.0f }
        };
        [JsonProperty(Order = 3)] public Dictionary<string, float> SpeedMultiplierByRank = new()
        {
            { nameof(ERank.Hustler), 1.5f },
            { nameof(ERank.Kingpin), 3.0f }
        };
    }

    public class LaunderingConfiguration
    {
        /// <summary>
        /// Per-business overrides. Entries are auto-discovered when a save loads, so the exact business names
        /// always appear here after the first launch; the seeded names are the vanilla laundering fronts.
        /// </summary>
        [JsonProperty(Order = 1)] public Dictionary<string, BusinessLaunderingConfiguration> Businesses = new()
        {
            { "Laundromat", new() },
            { "Car Wash", new() },
            { "Post Office", new() },
            { "Taco Ticklers", new() }
        };

        [JsonProperty(Order = 2)] public LaunderingXpScalingConfiguration XpScaling = new();
    }

    public class ModBankingConfiguration : ModuleConfiguration
    {
        public override string Name => "Banking";

        public AtmConfiguration Atm = new();
        public TransferFeeConfiguration TransferFee = new();
        public LaunderingConfiguration Laundering = new();

        public override void Validate()
        {
            Atm.Validate();
            TransferFee.Validate(Name);

            foreach (string key in Laundering.Businesses.Keys.ToList())
            {
                BusinessLaunderingConfiguration b = Laundering.Businesses[key];
                b.Capacity = ConfigValidator.AtLeast(Name, $"Laundering.Businesses[{key}].Capacity", b.Capacity, 0f);
                b.SpeedMultiplier = ConfigValidator.AtLeast(Name, $"Laundering.Businesses[{key}].SpeedMultiplier", b.SpeedMultiplier, 0.01f);
            }
        }
    }

    /// <summary>
    /// Banking tweaks: ATM weekly/daily deposit limits, a configurable bank-transfer fee on ATM transactions,
    /// per-business money-laundering capacity and speed, and optional rank-based scaling of laundering capacity
    /// and speed. See the patches under <c>Patches/</c>; each short-circuits on <see cref="ModuleConfiguration.Enabled"/>.
    /// </summary>
    public class ModBanking : ModuleBase<ModBankingConfiguration>
    {
        /// <summary>Running total of cash deposited at ATMs today; reset on day change (and on save load).</summary>
        public static float DailyDepositSum;

        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;

            DailyDepositSum = 0f;

            // Auto-discover laundering businesses so their exact names show up in Banking.json for editing.
            Il2CppSystem.Collections.Generic.List<Business> businesses = Business.Businesses;
            if (businesses == null)
                return;

            bool added = false;
            foreach (Business business in businesses)
            {
                string name = business?.PropertyName;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (!Configuration.Laundering.Businesses.ContainsKey(name))
                {
                    Configuration.Laundering.Businesses[name] = new BusinessLaunderingConfiguration();
                    added = true;
                    Log.Info($"[Banking] Discovered laundering business '{name}'");
                }
            }

            if (added)
                Configuration.SaveConfiguration();
        }

        /// <summary>
        /// Returns the multiplier from <paramref name="byRank"/> for the highest rank the player has reached
        /// (step function), or 1 when scaling is disabled / unavailable / nothing matches.
        /// </summary>
        public static float GetRankMultiplier(LaunderingXpScalingConfiguration xp, Dictionary<string, float> byRank)
        {
            if (xp == null || !xp.Enabled || byRank == null || byRank.Count == 0)
                return 1f;

            LevelManager levelManager = NetworkSingleton<LevelManager>.Instance;
            if (levelManager == null)
                return 1f;

            int currentRank = (int)levelManager.Rank;
            float multiplier = 1f;
            int bestRank = int.MinValue;

            foreach (KeyValuePair<string, float> pair in byRank)
            {
                if (!Enum.TryParse(pair.Key, out ERank rank))
                    continue;

                int rankIndex = (int)rank;
                if (rankIndex <= currentRank && rankIndex > bestRank)
                {
                    bestRank = rankIndex;
                    multiplier = pair.Value;
                }
            }

            return multiplier < 0f ? 1f : multiplier;
        }
    }
}
