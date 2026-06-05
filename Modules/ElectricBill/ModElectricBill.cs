using System;
using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Property;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using Lithium.Modules.EndOfDayFreeze;
using Newtonsoft.Json;
using UnityEngine;

namespace Lithium.Modules.ElectricBill
{
    // Per-appliance power draw, in watts. Standby applies while built but idle/off; InUse applies while
    // the appliance is operating (machines) or switched on (lights).
    public class ElectricBillApplianceConfig
    {
        [JsonProperty(Order = 1)] public float StandbyWatts;
        [JsonProperty(Order = 2)] public float InUseWatts;

        public ElectricBillApplianceConfig() { }

        public ElectricBillApplianceConfig(float standby, float inUse)
        {
            StandbyWatts = standby;
            InUseWatts = inUse;
        }
    }

    public class ModElectricBillConfiguration : ModuleConfiguration
    {
        public override string Name => "ElectricBill";

        [JsonProperty(Order = 1)] public int BillingIntervalDays = 7;

        // Dollars per kilowatt-hour. Accrued watt-minutes are converted to kWh and multiplied by this.
        [JsonProperty(Order = 2)] public float RatePerKwh = 2.0f;

        [JsonProperty(Order = 3)] public Dictionary<string, ElectricBillApplianceConfig> Appliances = new()
        {
            ["floorlamp"] = new(0f, 12f),
            ["antiquewalllamp"] = new(0f, 10f),
            ["modernwalllamp"] = new(0f, 10f),
            ["ledgrowlight"] = new(5f, 200f),
            ["fullspectrumgrowlight"] = new(8f, 450f),
            ["halogengrowlight"] = new(10f, 600f),
            ["bigsprinkler"] = new(2f, 40f),
            ["chemistrystation"] = new(15f, 1500f),
            ["laboven"] = new(20f, 2200f),
            ["mixingstation"] = new(10f, 500f),
            ["mixingstationmk2"] = new(15f, 800f),
            ["cauldron"] = new(12f, 1200f),
            ["packagingstation"] = new(8f, 300f),
            ["packagingstationmk2"] = new(10f, 450f),
            ["launderingstation"] = new(5f, 250f),
        };

        public override void Validate()
        {
            BillingIntervalDays = ConfigValidator.AtLeast(Name, nameof(BillingIntervalDays), BillingIntervalDays, 1);
            RatePerKwh = ConfigValidator.AtLeast(Name, nameof(RatePerKwh), RatePerKwh, 0f);

            foreach (string id in new List<string>(Appliances.Keys))
            {
                ElectricBillApplianceConfig a = Appliances[id];
                a.StandbyWatts = ConfigValidator.AtLeast(Name, $"Appliances[{id}].StandbyWatts", a.StandbyWatts, 0f);
                a.InUseWatts = ConfigValidator.AtLeast(Name, $"Appliances[{id}].InUseWatts", a.InUseWatts, 0f);
            }
        }
    }

    public class ModElectricBill : ModuleBase<ModElectricBillConfiguration>
    {
        private readonly struct ApplianceRef
        {
            public readonly BuildableItem Item;
            public readonly string Id;
            public readonly string Guid;

            public ApplianceRef(BuildableItem item, string id, string guid)
            {
                Item = item;
                Id = id;
                Guid = guid;
            }
        }

        private static readonly SaveSlotStore<ElectricBillState> Store = new("ElectricBill", "electric bill state");

        // Property codes currently powered off. Read every tick by the freeze patches, so kept in memory
        // (rebuilt from persisted state on load), mirroring ModRent.LockedCodes.
        private static readonly HashSet<string> PowerCutCodes = new();

        private readonly Dictionary<string, List<ApplianceRef>> _appliancesByCode = new();
        private readonly Dictionary<string, Property> _ownedByCode = new();

        private int _lastElapsedDay = -1;
        private int _minutesSincePersist;
        private bool _initialised;

        public static bool IsPowerCut(string propertyCode) =>
            !string.IsNullOrEmpty(propertyCode) && PowerCutCodes.Contains(propertyCode);

        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;

            Store.Unload();
            PowerCutCodes.Clear();
            _appliancesByCode.Clear();
            _ownedByCode.Clear();
            _lastElapsedDay = -1;
            _minutesSincePersist = 0;
            _initialised = false;
        }

        // Called every in-game minute from ElectricBillTickPatch.
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
                RefreshApplianceCache();
                RebuildCutFromState();
                _lastElapsedDay = today;
                _initialised = true;
                return;
            }

            // PassMinute keeps firing while the clock is frozen at 4 AM (the end-of-day AFK window)
            // without advancing the time. If EndOfDayFreeze is on, production is frozen too, so don't
            // bill there. If it's off, machines keep producing (the AFK exploit) so we still meter — you
            // pay for what you're running.
            bool endOfDayFrozen = time.IsEndOfDay && EndOfDayFreezeActive();
            if (!endOfDayFrozen)
                AccrueMinute();

            if (today != _lastElapsedDay)
            {
                _lastElapsedDay = today;
                RefreshApplianceCache();
                RebuildCutFromState();
                ProcessDayRollover(today);
                PersistAll();
            }
        }

        // --- Per-minute metering ---------------------------------------------------------------------

        private void AccrueMinute()
        {
            foreach (KeyValuePair<string, List<ApplianceRef>> kv in _appliancesByCode)
            {
                string code = kv.Key;
                ElectricBillState state = GetOrCreate(code);

                if (state.PoweredOff)
                {
                    EnforceCut(kv.Value);
                    continue;
                }

                state.AccruedWattMinutes += SumActiveWatts(kv.Value);
            }

            if (++_minutesSincePersist >= 60)
            {
                PersistAll();
                _minutesSincePersist = 0;
            }
        }

        // Bills a block of skipped in-game minutes (sleeping / story time-skips). The game advances each
        // station's cook by the same minute count via onTimeSkip, so metering this keeps electricity
        // consistent with the production it powered. Appliance state is sampled pre-skip (what was left
        // running when you slept). Powered-off properties accrue nothing — their machines are frozen by
        // the OnTimePass gate, which also blocks the skip path.
        public void AccrueTimeSkip(int minutes)
        {
            if (!Configuration.Enabled || minutes <= 0 || !_initialised)
                return;

            try
            {
                foreach (KeyValuePair<string, List<ApplianceRef>> kv in _appliancesByCode)
                {
                    ElectricBillState state = GetOrCreate(kv.Key);
                    if (state.PoweredOff)
                        continue;
                    state.AccruedWattMinutes += SumActiveWatts(kv.Value) * minutes;
                }
                PersistAll();
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] Time-skip accrual failed: {e.Message}");
            }
        }

        // Sum of each appliance's current draw (in-use if active, else standby) for one minute, in watts.
        private float SumActiveWatts(List<ApplianceRef> appliances)
        {
            float watts = 0f;
            foreach (ApplianceRef app in appliances)
            {
                if (app.Item == null)
                    continue;
                if (!Configuration.Appliances.TryGetValue(app.Id, out ElectricBillApplianceConfig cfg))
                    continue;
                watts += ApplianceStateResolver.IsActive(app.Id, app.Item) ? cfg.InUseWatts : cfg.StandbyWatts;
            }
            return watts;
        }

        private static bool EndOfDayFreezeActive()
        {
            ModEndOfDayFreeze module = Core.Get<ModEndOfDayFreeze>();
            return module != null && module.Configuration.Enabled;
        }

        // While cut, keep any lights the player re-toggled forced back off (machines stay frozen by the
        // freeze patches, which need no per-minute enforcement).
        private void EnforceCut(List<ApplianceRef> appliances)
        {
            foreach (ApplianceRef app in appliances)
            {
                if (app.Item == null || !ApplianceStateResolver.IsLight(app.Id))
                    continue;
                if (ApplianceStateResolver.IsActive(app.Id, app.Item))
                    ApplianceStateResolver.ForceOff(app.Id, app.Item);
            }
        }

        // --- Weekly billing --------------------------------------------------------------------------

        private void ProcessDayRollover(int today)
        {
            foreach (KeyValuePair<string, Property> kv in _ownedByCode)
            {
                string code = kv.Key;
                Property prop = kv.Value;
                ElectricBillState state = GetOrCreate(code);

                if (state.LastBilledDay < 0)
                {
                    // First sight: anchor the cadence, don't bill the partial week.
                    state.LastBilledDay = today;
                    Store.Set(code, state);
                    continue;
                }

                int intervals = (today - state.LastBilledDay) / Configuration.BillingIntervalDays;
                if (intervals >= 1)
                {
                    state.LastBilledDay += intervals * Configuration.BillingIntervalDays;
                    BillOnce(prop, state);
                }
                else if (state.PoweredOff && state.OutstandingBill > 0f)
                {
                    // No bill due today, but retry the auto-deduct so power can come back as soon as the
                    // player has the funds.
                    TryAutoPay(prop, state);
                }

                Store.Set(code, state);
            }
        }

        private void BillOnce(Property prop, ElectricBillState state)
        {
            float kwh = state.AccruedWattMinutes / 60f / 1000f;
            float amount = kwh * Configuration.RatePerKwh;
            state.AccruedWattMinutes = 0f;

            float due = amount + state.OutstandingBill;
            if (due < 0.01f)
            {
                state.OutstandingBill = 0f;
                return;
            }

            if (TryDeduct(due, prop))
            {
                state.OutstandingBill = 0f;
                if (state.PoweredOff)
                    RestorePower(prop, state);
                ElectricBillNotifier.Send("Power bill paid", $"{prop.PropertyName}: ${due:N2}");
            }
            else
            {
                state.OutstandingBill = due;
                if (!state.PoweredOff)
                    CutPower(prop, state);
                ElectricBillNotifier.Send("Power cut off", $"{prop.PropertyName}: ${due:N2} unpaid");
            }
        }

        private void TryAutoPay(Property prop, ElectricBillState state)
        {
            if (state.OutstandingBill <= 0f)
                return;

            if (TryDeduct(state.OutstandingBill, prop))
            {
                ElectricBillNotifier.Send("Power restored", $"{prop.PropertyName}: ${state.OutstandingBill:N2} paid");
                state.OutstandingBill = 0f;
                RestorePower(prop, state);
            }
        }

        private static bool TryDeduct(float amount, Property prop)
        {
            try
            {
                MoneyManager money = NetworkSingleton<MoneyManager>.Instance;
                if (money == null || money.onlineBalance < amount)
                    return false;

                money.CreateOnlineTransaction("Electricity", -amount, 1f, $"Power bill — {prop.PropertyName}");
                return true;
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] Deduction failed: {e.Message}");
                return false;
            }
        }

        // --- Power cut / restore ---------------------------------------------------------------------

        private void CutPower(Property prop, ElectricBillState state)
        {
            string code = prop.PropertyCode;
            state.PoweredOff = true;
            PowerCutCodes.Add(code);
            state.LightStatesAtCutoff.Clear();

            if (_appliancesByCode.TryGetValue(code, out List<ApplianceRef> appliances))
            {
                foreach (ApplianceRef app in appliances)
                {
                    if (app.Item == null)
                        continue;
                    if (ApplianceStateResolver.IsLight(app.Id))
                        state.LightStatesAtCutoff[app.Guid] = ApplianceStateResolver.IsActive(app.Id, app.Item);
                    ApplianceStateResolver.ForceOff(app.Id, app.Item);
                }
            }

            Log.Info($"[ElectricBill] Power cut at {prop.PropertyName} ({code}).");
        }

        private void RestorePower(Property prop, ElectricBillState state)
        {
            string code = prop.PropertyCode;
            state.PoweredOff = false;
            PowerCutCodes.Remove(code);

            if (_appliancesByCode.TryGetValue(code, out List<ApplianceRef> appliances))
            {
                foreach (ApplianceRef app in appliances)
                {
                    if (app.Item == null || !ApplianceStateResolver.IsLight(app.Id))
                        continue;
                    if (state.LightStatesAtCutoff.TryGetValue(app.Guid, out bool wasOn) && wasOn)
                        ApplianceStateResolver.Restore(app.Id, app.Item);
                }
            }

            state.LightStatesAtCutoff.Clear();
            Log.Info($"[ElectricBill] Power restored at {prop.PropertyName} ({code}).");
        }

        private void RebuildCutFromState()
        {
            try
            {
                PowerCutCodes.Clear();
                foreach (KeyValuePair<string, Property> kv in _ownedByCode)
                {
                    string code = kv.Key;
                    if (!Store.TryGet(code, out ElectricBillState state) || !state.PoweredOff)
                        continue;

                    PowerCutCodes.Add(code);

                    // Re-apply the visual shutoff (lights) for a save that was saved while powered off.
                    if (_appliancesByCode.TryGetValue(code, out List<ApplianceRef> appliances))
                        foreach (ApplianceRef app in appliances)
                            if (app.Item != null)
                                ApplianceStateResolver.ForceOff(app.Id, app.Item);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] Failed to rebuild cut set: {e.Message}");
            }
        }

        // --- Appliance discovery ---------------------------------------------------------------------

        private void RefreshApplianceCache()
        {
            try
            {
                _appliancesByCode.Clear();
                _ownedByCode.Clear();

                HashSet<string> ownedCodes = new();
                foreach (Property prop in AllOwned())
                {
                    // The RV is the starter home and is deliberately exempt from the electric bill.
                    if (prop.TryCast<RV>() != null)
                        continue;

                    string code = prop.PropertyCode;
                    if (string.IsNullOrEmpty(code))
                        continue;
                    ownedCodes.Add(code);
                    _ownedByCode[code] = prop;
                }

                BuildableItem[] all = UnityEngine.Object.FindObjectsOfType<BuildableItem>(true);
                foreach (BuildableItem bi in all)
                {
                    if (bi == null || bi.isGhost || bi.IsDestroyed)
                        continue;

                    ItemInstance inst = bi.ItemInstance;
                    if (inst == null)
                        continue;

                    string id = inst.ID;
                    if (string.IsNullOrEmpty(id) || !Configuration.Appliances.ContainsKey(id))
                        continue;

                    Property prop = bi.ParentProperty;
                    if (prop == null)
                        continue;

                    string code = prop.PropertyCode;
                    if (string.IsNullOrEmpty(code) || !ownedCodes.Contains(code))
                        continue;

                    if (!_appliancesByCode.TryGetValue(code, out List<ApplianceRef> list))
                    {
                        list = new List<ApplianceRef>();
                        _appliancesByCode[code] = list;
                    }
                    list.Add(new ApplianceRef(bi, id, bi.GUID.ToString()));
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] Appliance discovery failed: {e.Message}");
            }
        }

        private ElectricBillState GetOrCreate(string code)
        {
            if (Store.TryGet(code, out ElectricBillState state))
                return state;
            state = new ElectricBillState();
            Store.Set(code, state);
            return state;
        }

        private void PersistAll()
        {
            foreach (string code in _ownedByCode.Keys)
                if (Store.TryGet(code, out ElectricBillState state))
                    Store.Set(code, state);
        }

        private static IEnumerable<Property> AllOwned()
        {
            HashSet<string> seen = new();

            Il2CppSystem.Collections.Generic.List<Property> props = Property.OwnedProperties;
            if (props != null)
            {
                foreach (Property p in props)
                {
                    if (p == null)
                        continue;
                    string c = p.PropertyCode;
                    if (!string.IsNullOrEmpty(c) && seen.Add(c))
                        yield return p;
                }
            }

            Il2CppSystem.Collections.Generic.List<Business> businesses = Business.OwnedBusinesses;
            if (businesses != null)
            {
                foreach (Business b in businesses)
                {
                    if (b == null)
                        continue;
                    Property p = b;
                    string c = p.PropertyCode;
                    if (!string.IsNullOrEmpty(c) && seen.Add(c))
                        yield return p;
                }
            }
        }
    }
}
