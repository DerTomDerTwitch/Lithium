using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Lithium.Modules.Dealers.Architecture;
using Newtonsoft.Json;

namespace Lithium.Modules.Dealers
{
    public class RobberyConfiguration
    {
        [JsonProperty(Order = 1)] public bool PreventWhenArmed = true;

        [JsonProperty(Order = 2)] public float OutdatedWeaponImmunityChance = 0.5f;
    }

    public class WeaponAlertConfiguration
    {
        [JsonProperty(Order = 1)] public bool Enabled = true;
    }

    public class ShortageConfiguration
    {
        [JsonProperty(Order = 1)] public bool Enabled = true;

        [JsonProperty(Order = 2)] public int LeadHours = 8;
    }

    public class WeeklyReportConfiguration
    {
        [JsonProperty(Order = 1)] public bool Enabled = true;
    }

    public class ModDealersConfiguration : ModuleConfiguration
    {
        public override string Name => "Dealers";

        [JsonProperty(Order = 1)] public RobberyConfiguration Robbery = new();
        [JsonProperty(Order = 2)] public WeaponAlertConfiguration WeaponAlerts = new();
        [JsonProperty(Order = 3)] public ShortageConfiguration Shortage = new();
        [JsonProperty(Order = 4)] public WeeklyReportConfiguration WeeklyReport = new();

        public override void Validate()
        {
            if (Robbery.OutdatedWeaponImmunityChance < 0f) Robbery.OutdatedWeaponImmunityChance = 0f;
            if (Robbery.OutdatedWeaponImmunityChance > 1f) Robbery.OutdatedWeaponImmunityChance = 1f;
            if (Shortage.LeadHours < 1) Shortage.LeadHours = 1;
        }
    }

    public class ModDealers : ModuleBase<ModDealersConfiguration>
    {
        private int _lastDay = -1;
        private int _lastHour = -1;
        private bool _initialised;

        private readonly HashSet<string> _alertedShortages = new();

        public override void Apply()
        {
            DealerStatsStore.Unload();
            _alertedShortages.Clear();
            _lastDay = -1;
            _lastHour = -1;
            _initialised = false;
        }

        public void Tick()
        {
            if (!Configuration.Enabled)
                return;

            TimeManager time = TimeManager.Instance;
            if (time == null)
                return;

            int day = time.ElapsedDays;
            int hour = time.CurrentTime / 100;

            if (!_initialised)
            {
                _lastDay = day;
                _lastHour = hour;
                _initialised = true;
                return;
            }

            if (hour != _lastHour || day != _lastDay)
            {
                _lastHour = hour;
                OnHourPass();
            }

            if (day != _lastDay)
            {
                int prevDay = _lastDay;
                _lastDay = day;
                OnDayPass();
                if (day / 7 != prevDay / 7)
                    OnWeekPass(day / 7);
            }
        }

        private void OnHourPass()
        {
            if (!Configuration.Shortage.Enabled)
                return;

            TimeManager time = TimeManager.Instance;
            if (time == null)
                return;

            int nowAbs = time.GetDateTime().GetMinSum();
            int leadMins = Configuration.Shortage.LeadHours * 60;

            _alertedShortages.RemoveWhere(key =>
            {
                int at = key.LastIndexOf('@');
                return at >= 0 && int.TryParse(key.Substring(at + 1), out int abs) && abs < nowAbs;
            });

            foreach (Dealer dealer in RecruitedDealers())
            {
                List<Shortfall> shortfalls = DealerShortageCalculator.Compute(dealer);
                if (shortfalls.Count == 0)
                    continue;

                foreach (IGrouping<int, Shortfall> group in shortfalls.GroupBy(s => s.DealAbsMinute).OrderBy(g => g.Key))
                {
                    int dealAbs = group.Key;
                    int until = dealAbs - nowAbs;
                    if (until <= 0 || until > leadMins)
                        continue;

                    string key = $"{dealer.ID}@{dealAbs}";
                    if (!_alertedShortages.Add(key))
                        break;

                    string items = string.Join(", ", group.Select(s => $"{s.Deficit}x {s.ProductName}"));
                    int hrs = Math.Max(1, until / 60);
                    DealerMessenger.Send(dealer,
                        $"Heads up - I'm going to come up short for a deal in about {hrs}h. I still need {items} or I can't deliver. Restock me.");
                    break;
                }
            }
        }

        private void OnDayPass()
        {
            if (!Configuration.WeaponAlerts.Enabled)
                return;

            foreach (Dealer dealer in RecruitedDealers())
            {
                switch (DealerWeaponInspector.Classify(dealer))
                {
                    case WeaponStatus.None:
                        DealerMessenger.Send(dealer,
                            "I've got nothing to defend myself with - if the cartel hits me they'll clean me out. Put a weapon in my inventory.");
                        break;
                    case WeaponStatus.Outdated:
                        DealerMessenger.Send(dealer,
                            "My weapon's getting old - there's better available at your rank now. Swap it out or I'm only half-safe if I get jumped.");
                        break;
                }
            }
        }

        private void OnWeekPass(int weekIndex)
        {
            if (!Configuration.WeeklyReport.Enabled)
                return;

            foreach (Dealer dealer in RecruitedDealers())
            {
                Dictionary<string, int> sold = DealerStatsStore.RollWeek(dealer.ID, weekIndex);
                if (sold.Count == 0)
                    continue;

                int total = sold.Values.Sum();
                string breakdown = string.Join(", ",
                    sold.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value}x {kv.Key}"));
                DealerMessenger.Send(dealer, $"Weekly numbers: I moved about {total} units - {breakdown}.");
            }
        }

        private static IEnumerable<Dealer> RecruitedDealers()
        {
            Il2CppSystem.Collections.Generic.List<Dealer> all = Dealer.AllPlayerDealers;
            if (all == null)
                yield break;

            foreach (Dealer dealer in all)
                if (dealer != null && dealer.IsRecruited)
                    yield return dealer;
        }
    }
}
