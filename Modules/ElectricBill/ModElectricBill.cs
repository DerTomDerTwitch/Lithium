using System;
using System.Collections.Generic;
using Il2CppFishNet;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Misc;
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

        // Built-in room lights — the wall-switch fixtures a property ships with (not placeable items).
        // Each controlled light draws InUseWatts while its switch is on, StandbyWatts while off.
        [JsonProperty(Order = 4)] public ElectricBillApplianceConfig BuiltInLight = new(0f, 40f);

        public override void Validate()
        {
            BillingIntervalDays = ConfigValidator.AtLeast(Name, nameof(BillingIntervalDays), BillingIntervalDays, 1);
            RatePerKwh = ConfigValidator.AtLeast(Name, nameof(RatePerKwh), RatePerKwh, 0f);
            BuiltInLight.StandbyWatts = ConfigValidator.AtLeast(Name, "BuiltInLight.StandbyWatts", BuiltInLight.StandbyWatts, 0f);
            BuiltInLight.InUseWatts = ConfigValidator.AtLeast(Name, "BuiltInLight.InUseWatts", BuiltInLight.InUseWatts, 0f);

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
        private readonly Dictionary<string, List<ModularSwitch>> _switchesByCode = new();
        private readonly Dictionary<string, Property> _ownedByCode = new();

        // Synthetic appliance id for the property's built-in (wall-switch) room lights.
        private const string BuiltInLightId = "builtinlight";
        private const string BuiltInLightName = "Built-in lights";

        private int _lastElapsedDay = -1;
        private int _minutesSincePersist;
        private bool _initialised;

        // Metering baseline for the frame-based driver (DriveUpdate). _lastMinSum is the in-game minute
        // counter (TimeManager.GetTotalMinSum) at the previous poll; the delta since then is the number of
        // in-game minutes to bill. _eodRealAccum accumulates real time during the frozen 4 AM stall (when
        // the minute counter doesn't advance). _pendingSkipResync defers a baseline resync to the next poll
        // after a time-skip the TimeSkipBillingPatch already billed, so those minutes aren't billed twice.
        private int _lastMinSum = -1;
        private float _eodRealAccum;
        private bool _pendingSkipResync;

        public static bool IsPowerCut(string propertyCode) =>
            !string.IsNullOrEmpty(propertyCode) && PowerCutCodes.Contains(propertyCode);

        // Host-only read of a property's outstanding (unpaid) power bill — the cash debt billed each interval.
        // Used by the Rent dead-drop payment path so cash dropped there settles the power bill with the rent.
        public float GetOutstandingBill(string propertyCode)
        {
            if (!Configuration.Enabled || string.IsNullOrEmpty(propertyCode) || !InstanceFinder.IsServer)
                return 0f;
            return Store.TryGet(propertyCode, out ElectricBillState state) ? state.OutstandingBill : 0f;
        }

        // Host-only: force this property's accrued electricity into its bill NOW — instead of waiting for the
        // module's own weekly rollover — re-anchoring the cadence to today. Called by ModRent the moment rent
        // is charged, so electricity falls due together with rent and is settled together at the dead drop.
        // Idempotent per in-game day (if the regular rollover already billed/anchored today, this just reports
        // the result), so the same minutes are never billed twice. Returns the property's resulting outstanding
        // power bill (0 if the module is off, the property is the exempt RV, or not on the host).
        public float BillNowWithRent(Property prop)
        {
            if (!Configuration.Enabled || prop == null || !InstanceFinder.IsServer)
                return 0f;

            string code = prop.PropertyCode;
            if (string.IsNullOrEmpty(code) || prop.TryCast<RV>() != null)
                return 0f;

            try
            {
                ElectricBillState state = GetOrCreate(code);
                int today = TimeManager.Instance != null ? TimeManager.Instance.ElapsedDays : state.LastBilledDay;

                // The regular rollover runs earlier in the same frame; only bill if it hasn't already billed or
                // anchored today, otherwise just read back the outstanding amount it produced.
                if (state.LastBilledDay != today)
                {
                    BillOnce(prop, state);
                    state.LastBilledDay = today;
                    Store.Set(code, state);
                    PublishElectric(code, state);
                }

                return state.OutstandingBill;
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] Bill-with-rent failed: {e.Message}");
                return 0f;
            }
        }

        // Outcome of a cash payment against a property's electricity bill, so the caller (the rent drop path)
        // can fold it into a combined landlord message instead of a separate notification.
        public sealed class CashPaymentResult
        {
            public float Paid;          // cash actually applied to the bill
            public float Remaining;     // bill still outstanding afterwards
            public bool Cleared;        // bill fully settled by this payment
            public bool PowerRestored;  // power came back on as a result
        }

        // Host-only: register a cash payment (collected at the rent dead drop) against this property's
        // outstanding power bill, restoring power if it clears. Electricity is billed as a cash debt payable
        // here (no bank auto-deduct), so this is the sole settlement path. Pass announce:false to suppress the
        // power-bill notification when the caller reports the result itself (e.g. the combined rent message).
        public CashPaymentResult ApplyCashPayment(Property prop, float amount, bool announce = true)
        {
            CashPaymentResult result = new();
            if (!Configuration.Enabled || prop == null || amount <= 0f || !InstanceFinder.IsServer)
                return result;

            string code = prop.PropertyCode;
            if (string.IsNullOrEmpty(code) || !Store.TryGet(code, out ElectricBillState state) || state.OutstandingBill <= 0f)
                return result;

            result.Paid = Math.Min(state.OutstandingBill, amount);
            state.OutstandingBill = Math.Max(0f, state.OutstandingBill - amount);
            if (state.OutstandingBill < 0.01f)
            {
                state.OutstandingBill = 0f;
                result.Cleared = true;
                if (state.PoweredOff)
                {
                    RestorePower(prop, state);
                    result.PowerRestored = true;
                }
                if (announce)
                    ElectricBillNotifier.Send("Power bill paid", $"{prop.PropertyName}: ${result.Paid:N2} settled in cash");
            }
            else if (announce)
            {
                ElectricBillNotifier.Send("Power bill — partial", $"{prop.PropertyName}: ${result.Paid:N2} paid, ${state.OutstandingBill:N2} left");
            }
            result.Remaining = state.OutstandingBill;

            Store.Set(code, state);
            PublishElectric(code, state);
            return result;
        }

        // One grouped appliance line for the phone app's electric table.
        public sealed class ApplianceLine
        {
            public string Name;
            public int Count;
            public float CurrentWatts;   // sum of each unit's live draw (in-use if active, else standby)
            public float AccruedCost;    // actual cost accrued by this source this billing period
            public float ProjectedCost;  // cost over one billing interval at the current draw
        }

        // Read-only electric snapshot for one property, for the phone app.
        public sealed class ElectricAppView
        {
            public bool ModuleEnabled;
            public bool PoweredOff;
            public float OutstandingBill;
            public float TotalWatts;
            public float TotalAccrued;
            public float TotalProjected;
            public List<ApplianceLine> Lines = new();
        }

        // Builds the live appliance breakdown for a property: per-source actual cost accrued this period
        // plus the live draw and a week projection. Pass refresh:true (on app open / dropdown change) to
        // rescan placed buildables and switches; otherwise the cached lists are reused (cheap).
        public ElectricAppView GetAppView(string propertyCode, bool refresh)
        {
            ElectricAppView view = new() { ModuleEnabled = Configuration.Enabled };
            if (!Configuration.Enabled || string.IsNullOrEmpty(propertyCode))
                return view;

            try
            {
                if (refresh || _ownedByCode.Count == 0)
                    RefreshApplianceCache();

                Store.TryGet(propertyCode, out ElectricBillState state);
                if (state != null)
                {
                    view.PoweredOff = state.PoweredOff;
                    view.OutstandingBill = state.OutstandingBill;
                }
                else if (!InstanceFinder.IsServer)
                {
                    // Client: the bill tick never ran here, so the save store is empty. Read the power-cut
                    // status and outstanding amount the host replicated. The live appliance breakdown below
                    // is still computed locally from the (networked) buildables + the client's own config.
                    view.PoweredOff = HostStateSync.GetBool($"bill_cut_{propertyCode}", false);
                    view.OutstandingBill = HostStateSync.GetNumber($"bill_outstanding_{propertyCode}", 0f);
                }

                float hours = Configuration.BillingIntervalDays * 24f;
                Dictionary<string, ApplianceLine> grouped = new();

                // Placeable appliances.
                if (_appliancesByCode.TryGetValue(propertyCode, out List<ApplianceRef> appliances))
                {
                    foreach (ApplianceRef app in appliances)
                    {
                        if (app.Item == null)
                            continue;
                        if (!Configuration.Appliances.TryGetValue(app.Id, out ElectricBillApplianceConfig cfg))
                            continue;
                        float watts = ApplianceStateResolver.IsActive(app.Id, app.Item) ? cfg.InUseWatts : cfg.StandbyWatts;
                        ApplianceLine line = GetLine(grouped, app.Id, ApplianceDisplayName(app));
                        line.Count++;
                        line.CurrentWatts += watts;
                    }
                }

                // Built-in room lights, grouped as a single source.
                if (_switchesByCode.TryGetValue(propertyCode, out List<ModularSwitch> switches))
                {
                    foreach (ModularSwitch sw in switches)
                    {
                        if (sw == null)
                            continue;
                        int lights = CountLights(sw);
                        float per = sw.isOn ? Configuration.BuiltInLight.InUseWatts : Configuration.BuiltInLight.StandbyWatts;
                        ApplianceLine line = GetLine(grouped, BuiltInLightId, BuiltInLightName);
                        line.Count += lights;
                        line.CurrentWatts += lights * per;
                    }
                }

                foreach (KeyValuePair<string, ApplianceLine> kv in grouped)
                {
                    ApplianceLine line = kv.Value;
                    line.ProjectedCost = line.CurrentWatts * hours / 1000f * Configuration.RatePerKwh;
                    if (state != null && state.AccruedByAppliance.TryGetValue(kv.Key, out float wm))
                        line.AccruedCost = wm / 60f / 1000f * Configuration.RatePerKwh;
                    view.TotalWatts += line.CurrentWatts;
                    view.TotalAccrued += line.AccruedCost;
                    view.TotalProjected += line.ProjectedCost;
                    view.Lines.Add(line);
                }
                view.Lines.Sort((a, b) => b.ProjectedCost.CompareTo(a.ProjectedCost));
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] App view build failed: {e.Message}");
            }
            return view;
        }

        private static ApplianceLine GetLine(Dictionary<string, ApplianceLine> grouped, string id, string name)
        {
            if (!grouped.TryGetValue(id, out ApplianceLine line))
            {
                line = new ApplianceLine { Name = name };
                grouped[id] = line;
            }
            return line;
        }

        private static string ApplianceDisplayName(ApplianceRef app)
        {
            try
            {
                ItemInstance inst = app.Item != null ? app.Item.ItemInstance : null;
                if (inst != null && !string.IsNullOrEmpty(inst.Name))
                    return inst.Name;
            }
            catch { }
            return app.Id;
        }

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
            _lastMinSum = -1;
            _eodRealAccum = 0f;
            _pendingSkipResync = false;
            _initialised = false;
        }

        // Frame-based driver, forwarded from Core.OnUpdate (host-only). Replaces the former postfix on
        // TimeManager.PassMinute: PassMinute() is a one-line private forwarder (it just calls
        // PassMinute_Client(CurrentTime)) that the IL2CPP build can inline into TimeLoop, so a Harmony
        // postfix on it is not reliably invoked — the same fragility that moved the Rent module onto
        // OnUpdate. When the postfix didn't fire, metering never ran and the bill stayed $0. Metering is now
        // derived from the in-game minute counter (GetTotalMinSum): the delta since the last poll is the
        // number of in-game minutes to bill. This advances exactly with game time, naturally pauses when the
        // clock is paused (the counter stops), and never depends on PassMinute. Host-only: state is
        // authoritative on the server and replicated to clients via HostStateSync.
        public void DriveUpdate()
        {
            if (!Configuration.Enabled || !InstanceFinder.IsServer)
                return;

            TimeManager time = TimeManager.Instance;
            if (time == null)
                return;

            int today = time.ElapsedDays;

            if (!_initialised)
            {
                RefreshApplianceCache();
                RebuildCutFromState();
                PublishElectricAll();
                _lastElapsedDay = today;
                _lastMinSum = time.GetTotalMinSum();
                _eodRealAccum = 0f;
                _pendingSkipResync = false;
                _initialised = true;
                return;
            }

            int minSum = time.GetTotalMinSum();

            if (_pendingSkipResync)
            {
                // A sleep / story time-skip was just metered by TimeSkipBillingPatch (the same count the game
                // advances cooks by). The minute counter jumped across the skip; resync the baseline without
                // accruing so those minutes aren't billed a second time here.
                _pendingSkipResync = false;
                _lastMinSum = minSum;
                _eodRealAccum = 0f;
            }
            else if (time.IsEndOfDay)
            {
                // 4 AM end-of-day stall: the clock is frozen here (ShouldMinutePass is false), so the minute
                // counter doesn't advance. With EndOfDayFreeze on, production is frozen too — bill nothing.
                // With it off, machines keep producing (the AFK exploit), so meter against real time at the
                // game's minute cadence so you still pay for what runs.
                if (!EndOfDayFreezeActive() && Time.timeScale > 0f)
                    AccrueEndOfDayRealtime(time);
                _lastMinSum = minSum;
            }
            else
            {
                _eodRealAccum = 0f;
                int delta = minSum - _lastMinSum;
                if (delta > 0)
                    AccrueMinutes(delta);
                // delta <= 0 (paused, no change, or a discontinuity) accrues nothing; just resync.
                _lastMinSum = minSum;
            }

            if (today != _lastElapsedDay)
            {
                _lastElapsedDay = today;
                RefreshApplianceCache();
                RebuildCutFromState();
                ProcessDayRollover(today);
                PersistAll();
            }
        }

        // Meters the frozen 4 AM stall against real time (only reached when EndOfDayFreeze is off). The
        // minute counter doesn't move here, so convert elapsed real seconds to whole in-game minutes at the
        // current cadence (MinuteDuration real-seconds per in-game minute, scaled by the time multiplier and
        // timeScale), carrying the remainder so the rate stays accurate over the stall.
        private void AccrueEndOfDayRealtime(TimeManager time)
        {
            float minuteDuration = TimeManager.MinuteDuration;
            float scale = time.TimeSpeedMultiplier * Time.timeScale;
            if (minuteDuration <= 0f || scale <= 0f)
                return;

            float realSecondsPerMinute = minuteDuration / scale;
            _eodRealAccum += Time.unscaledDeltaTime;

            int mins = (int)(_eodRealAccum / realSecondsPerMinute);
            if (mins > 0)
            {
                _eodRealAccum -= mins * realSecondsPerMinute;
                AccrueMinutes(mins);
            }
        }

        // --- Per-minute metering ---------------------------------------------------------------------

        // Accrues `mins` in-game minutes of energy across every owned property — the delta the frame-based
        // driver detected since the last poll (or the count metered during the 4 AM stall). A powered-off
        // property accrues nothing and instead re-forces any re-toggled lights off.
        private void AccrueMinutes(int mins)
        {
            if (mins <= 0)
                return;

            foreach (string code in _ownedByCode.Keys)
            {
                ElectricBillState state = GetOrCreate(code);

                if (state.PoweredOff)
                {
                    if (_appliancesByCode.TryGetValue(code, out List<ApplianceRef> cut))
                        EnforceCut(cut);
                    continue;
                }

                AccrueProperty(code, state, mins);
            }

            _minutesSincePersist += mins;
            if (_minutesSincePersist >= 60)
            {
                PersistAll();
                _minutesSincePersist = 0;
            }
        }

        // Accrues one block of watt-minutes for a property, broken down per appliance id (and the
        // synthetic built-in-lights id). minutes = 1 for per-minute metering, or the skipped count.
        private void AccrueProperty(string code, ElectricBillState state, float minutes)
        {
            if (_appliancesByCode.TryGetValue(code, out List<ApplianceRef> appliances))
            {
                foreach (ApplianceRef app in appliances)
                {
                    if (app.Item == null)
                        continue;
                    if (!Configuration.Appliances.TryGetValue(app.Id, out ElectricBillApplianceConfig cfg))
                        continue;
                    float watts = ApplianceStateResolver.IsActive(app.Id, app.Item) ? cfg.InUseWatts : cfg.StandbyWatts;
                    AddAccrual(state, app.Id, watts * minutes);
                }
            }

            if (_switchesByCode.TryGetValue(code, out List<ModularSwitch> switches))
            {
                float watts = BuiltInLightWatts(switches);
                if (watts > 0f)
                    AddAccrual(state, BuiltInLightId, watts * minutes);
            }
        }

        private static void AddAccrual(ElectricBillState state, string id, float wattMinutes)
        {
            state.AccruedWattMinutes += wattMinutes;
            state.AccruedByAppliance.TryGetValue(id, out float current);
            state.AccruedByAppliance[id] = current + wattMinutes;
        }

        // Current draw (watts) of a property's built-in room lights: each controlled light draws the
        // configured in-use watts while its switch is on, standby watts while off.
        private float BuiltInLightWatts(List<ModularSwitch> switches)
        {
            float total = 0f;
            foreach (ModularSwitch sw in switches)
            {
                if (sw == null)
                    continue;
                float per = sw.isOn ? Configuration.BuiltInLight.InUseWatts : Configuration.BuiltInLight.StandbyWatts;
                total += CountLights(sw) * per;
            }
            return total;
        }

        private static int CountLights(ModularSwitch sw)
        {
            try
            {
                Il2CppReferenceArray<Il2CppScheduleOne.Misc.ToggleableLight> lights = sw.LightsToControl;
                int n = lights != null ? lights.Length : 0;
                return n > 0 ? n : 1;
            }
            catch
            {
                return 1;
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

            // The minute counter the frame-based driver tracks (GetTotalMinSum) will jump across this skip.
            // Flag a baseline resync so DriveUpdate's next poll absorbs that jump without billing the skipped
            // minutes a second time — they're billed right here, matching the count the game advances cooks by.
            _pendingSkipResync = true;

            try
            {
                foreach (string code in _ownedByCode.Keys)
                {
                    ElectricBillState state = GetOrCreate(code);
                    if (state.PoweredOff)
                        continue;
                    AccrueProperty(code, state, minutes);
                }
                PersistAll();
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] Time-skip accrual failed: {e.Message}");
            }
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
                    PublishElectric(code, state);
                    continue;
                }

                int intervals = (today - state.LastBilledDay) / Configuration.BillingIntervalDays;
                if (intervals >= 1)
                {
                    state.LastBilledDay += intervals * Configuration.BillingIntervalDays;
                    BillOnce(prop, state);
                }

                Store.Set(code, state);
                PublishElectric(code, state);
            }
        }

        // Host-only: mirror a property's power-cut status and outstanding bill onto the replication channel so
        // a client's phone app shows them. No-op on clients (SetBool/SetNumber self-guard). The live appliance
        // breakdown is computed client-side and needs no replication.
        private static void PublishElectric(string code, ElectricBillState state)
        {
            if (string.IsNullOrEmpty(code) || state == null)
                return;
            HostStateSync.SetBool($"bill_cut_{code}", state.PoweredOff);
            HostStateSync.SetNumber($"bill_outstanding_{code}", state.OutstandingBill);
        }

        // Host-only: re-assert every owned property's power state onto the channel (used on load).
        private void PublishElectricAll()
        {
            if (!InstanceFinder.IsServer)
                return;
            foreach (string code in _ownedByCode.Keys)
                if (Store.TryGet(code, out ElectricBillState state))
                    PublishElectric(code, state);
        }

        // Issues the period's electricity as a cash debt payable at the property's rent dead drop — there is
        // NO bank auto-deduct; the player settles rent and electricity together there (ModRent.CreditFromDrop).
        // One interval of grace: power is only cut if a PREVIOUS bill was still unpaid when this one lands, so
        // a player who keeps the drop funded (the rent sweep clears OutstandingBill each tick) is never cut.
        private void BillOnce(Property prop, ElectricBillState state)
        {
            float kwh = state.AccruedWattMinutes / 60f / 1000f;
            float amount = kwh * Configuration.RatePerKwh;
            state.AccruedWattMinutes = 0f;
            state.AccruedByAppliance.Clear();

            bool hadUnpaid = state.OutstandingBill > 0.01f;
            state.OutstandingBill += amount;

            if (state.OutstandingBill < 0.01f)
            {
                state.OutstandingBill = 0f;
                return;
            }

            if (hadUnpaid && !state.PoweredOff)
            {
                // A full billing interval passed with the bill unpaid — cut power until it's settled in cash.
                CutPower(prop, state);
                ElectricBillNotifier.Send("Power cut off",
                    $"{prop.PropertyName}: ${state.OutstandingBill:N2} unpaid. Settle it in cash at the dead drop.");
            }
            else
            {
                ElectricBillNotifier.Send("Power bill due",
                    $"{prop.PropertyName}: ${state.OutstandingBill:N2}. Pay it in cash at the dead drop with your rent.");
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
                _switchesByCode.Clear();
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

                    // Built-in room lights ship with the property as wall switches.
                    Il2CppSystem.Collections.Generic.List<ModularSwitch> switches = prop.Switches;
                    if (switches != null && switches.Count > 0)
                    {
                        List<ModularSwitch> list = new();
                        foreach (ModularSwitch sw in switches)
                            if (sw != null)
                                list.Add(sw);
                        if (list.Count > 0)
                            _switchesByCode[code] = list;
                    }
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
