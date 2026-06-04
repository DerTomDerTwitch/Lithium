using System;
using System.Text;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Property;
using Lithium.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Lithium.Modules.Banking
{
    public enum EFeeMode
    {
        Percent,
        Fixed
    }

    public class TransferFeeConfiguration
    {
        [JsonProperty(Order = 1)] public bool Enabled = true;

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty(Order = 2)] public EFeeMode Mode = EFeeMode.Percent;

        [JsonProperty(Order = 3)] public float Percent = 5f;
        [JsonProperty(Order = 4)] public float MinFee = 5f;
        [JsonProperty(Order = 5)] public float MaxFee = 0f;

        [JsonProperty(Order = 6)] public float FixedAmount = 0f;

        [JsonProperty(Order = 7)] public bool ApplyToDeposits = false;
        [JsonProperty(Order = 8)] public bool ApplyToWithdrawals = true;

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

    public class AtmConfiguration
    {
        [JsonProperty(Order = 1)] public float WeeklyDepositLimit = 10000f;
        [JsonProperty(Order = 2)] public float DailyDepositLimit = -1f;

        [JsonIgnore] public bool WeeklyLimited => WeeklyDepositLimit >= 0f;
        [JsonIgnore] public bool DailyLimited => DailyDepositLimit >= 0f;

        public void Validate()
        {
            if (WeeklyDepositLimit < 0f) WeeklyDepositLimit = -1f;
            if (DailyDepositLimit < 0f) DailyDepositLimit = -1f;
        }
    }

    public class BusinessLaunderingConfiguration
    {
        [JsonProperty(Order = 1)] public float Capacity = -1f;
        [JsonProperty(Order = 2)] public float SpeedMultiplier = 1f;
        [JsonProperty(Order = 3)] public float Cut = 0f;
    }

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

    public class LaunderingReportConfiguration
    {
        [JsonProperty(Order = 1)] public bool Enabled = false;
        [JsonProperty(Order = 2)] public string ContactNpcName = "Herbert";
        [JsonProperty(Order = 3)] public string ContactDisplayName = "";
    }

    public class LaunderingConfiguration
    {
        [JsonProperty(Order = 1)] public Dictionary<string, BusinessLaunderingConfiguration> Businesses = new()
        {
            { "Laundromat", new() },
            { "Car Wash", new() },
            { "Post Office", new() },
            { "Taco Ticklers", new() }
        };

        [JsonProperty(Order = 2)] public LaunderingXpScalingConfiguration XpScaling = new();
        [JsonProperty(Order = 3)] public LaunderingReportConfiguration DailyReport = new();
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
                if (b.Capacity < 0f)
                    b.Capacity = -1f;
                b.SpeedMultiplier = ConfigValidator.AtLeast(Name, $"Laundering.Businesses[{key}].SpeedMultiplier", b.SpeedMultiplier, 0.01f);
                b.Cut = ConfigValidator.InRange(Name, $"Laundering.Businesses[{key}].Cut", b.Cut, 0f, 100f);
            }
        }
    }

    public class ModBanking : ModuleBase<ModBankingConfiguration>
    {
        public static float DailyDepositSum;

        private sealed class LaunderTally
        {
            public float Laundered;
            public float Cut;
        }

        private static readonly Dictionary<string, LaunderTally> Tallies = new();

        private int _lastElapsedDay = -1;
        private bool _initialised;

        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;

            DailyDepositSum = 0f;
            Tallies.Clear();
            _lastElapsedDay = -1;
            _initialised = false;

            DiscoverBusinesses();
        }

        private void DiscoverBusinesses()
        {
            Il2CppSystem.Collections.Generic.List<Business> businesses = Business.Businesses;
            if (businesses == null)
                return;

            bool changed = false;
            foreach (Business business in businesses)
            {
                string name = business?.PropertyName;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (!Configuration.Laundering.Businesses.TryGetValue(name, out BusinessLaunderingConfiguration entry))
                {
                    entry = new BusinessLaunderingConfiguration();
                    Configuration.Laundering.Businesses[name] = entry;
                    changed = true;
                    Log.Info($"[Banking] Discovered laundering business '{name}'");
                }

                if (entry.Capacity < 0f && business.LaunderCapacity > 0f)
                {
                    entry.Capacity = business.LaunderCapacity;
                    changed = true;
                }
            }

            if (changed)
                Configuration.SaveConfiguration();
        }

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

        public static void RecordLaundering(string businessName, float laundered, float cut)
        {
            if (string.IsNullOrEmpty(businessName))
                businessName = "Unknown";

            if (!Tallies.TryGetValue(businessName, out LaunderTally tally))
            {
                tally = new LaunderTally();
                Tallies[businessName] = tally;
            }

            tally.Laundered += laundered;
            tally.Cut += cut;
        }

        public void Tick()
        {
            if (!Configuration.Enabled)
                return;

            TimeManager time = TimeManager.Instance;
            if (time == null)
                return;

            int today = time.ElapsedDays;

            if (!_initialised)
            {
                DiscoverBusinesses();
                _lastElapsedDay = today;
                _initialised = true;
                return;
            }

            if (today == _lastElapsedDay)
                return;

            _lastElapsedDay = today;
            EmitDailyReport();
            Tallies.Clear();
        }

        private void EmitDailyReport()
        {
            LaunderingReportConfiguration report = Configuration.Laundering.DailyReport;
            if (!report.Enabled || Tallies.Count == 0)
                return;

            float totalLaundered = 0f;
            float totalCut = 0f;
            StringBuilder builder = new();
            builder.AppendLine("Daily laundering report:");

            foreach (KeyValuePair<string, LaunderTally> pair in Tallies)
            {
                if (pair.Value.Laundered <= 0f)
                    continue;

                totalLaundered += pair.Value.Laundered;
                totalCut += pair.Value.Cut;
                builder.AppendLine($"- {pair.Key}: laundered ${pair.Value.Laundered:N0}, cut ${pair.Value.Cut:N0}");
            }

            if (totalLaundered <= 0f)
                return;

            builder.Append($"Total: laundered ${totalLaundered:N0}, lost to cut ${totalCut:N0}");
            BankingContact.Send(report.ContactNpcName, report.ContactDisplayName, builder.ToString());
        }
    }
}
