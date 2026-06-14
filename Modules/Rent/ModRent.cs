using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using Lithium.Modules.ElectricBill;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace Lithium.Modules.Rent
{
    public class RentLocationConfiguration
    {
        [JsonProperty(Order = 1)] public bool Enabled = false;

        [JsonProperty(Order = 2)] public float WeeklyRent = 0f;

        [JsonProperty(Order = 3)] public string DeadDropName = "";

        [JsonProperty(Order = 4)] public string DeadDropGUID = "";

        [JsonProperty(Order = 5)] public string ContactNpcName = "Fixer";
    }

    public class ModRentConfiguration : ModuleConfiguration
    {
        public override string Name => "Rent";

        // The rent period: in-game days between rent charges. Each location's first charge lands one full
        // period after it's acquired (the first period is always free), then every period after. Lower this
        // (e.g. to 1) to test the overdue texts / lockout quickly.
        [JsonProperty(Order = 1)] public int RentIntervalDays = 7;

        [JsonProperty(Order = 2)] public int DaysUntilLockout = 2;

        [JsonProperty(Order = 3)] public bool SendFinalWarning = true;

        [JsonProperty(Order = 4)] public Dictionary<string, RentLocationConfiguration> Locations = new()
        {
            ["RV"] = new(),
            ["Sweatshop"] = new() { Enabled = true, WeeklyRent = 200f, DeadDropName = "North arcade wall", DeadDropGUID = "dd3d22f1-da56-4673-9203-640eeaf915fc", ContactNpcName = "Mrs. Ming" },
            ["Hyland Manor"] = new(),
            ["Motel Room"] = new() { Enabled = true, WeeklyRent = 75f, DeadDropName = "Behind motel office", DeadDropGUID = "d66b3fd6-7b7f-4e98-b000-6d5a197f7437", ContactNpcName = "Donna Martin" },
            ["Sewer Office"] = new(),
            ["Laundromat"] = new() { Enabled = true, WeeklyRent = 250f, DeadDropName = "Alleyway behind the laundromat", DeadDropGUID = "555e565d-2f65-4882-9edb-7167168b2e00", ContactNpcName = "Doris Lubbin" },
            ["Storage Unit"] = new() { Enabled = true, WeeklyRent = 500f, DeadDropName = "Behind Casino", DeadDropGUID = "f5614c42-2c74-42d5-bee2-025e84718792", ContactNpcName = "Geraldine Poon" },
            ["Taco Ticklers"] = new() { Enabled = true, WeeklyRent = 1000f, DeadDropName = "Taco Ticklers exterior wall", DeadDropGUID = "79200bb6-7d39-44d9-ab22-c63f136d8cdf", ContactNpcName = "Dean Webster" },
            ["Bungalow"] = new() { Enabled = true, WeeklyRent = 300f, DeadDropName = "Brown apartment block", DeadDropGUID = "b77ed1a0-c729-41d5-b8a2-81b480ca971f", ContactNpcName = "Hank Stevenson" },
            ["Barn"] = new() { Enabled = true, WeeklyRent = 750f, DeadDropName = "Behind fire station", DeadDropGUID = "86835825-f5f8-454d-9c74-c31e257f9cc2", ContactNpcName = "Harold Colt" },
            ["Post Office"] = new() { Enabled = true, WeeklyRent = 850f, DeadDropName = "Central canal", DeadDropGUID = "4ac50ff1-ad3c-415b-aa77-c80249dfa473", ContactNpcName = "Bruce Norton" },
            ["Car Wash"] = new() { Enabled = true, WeeklyRent = 1000f, DeadDropName = "Behind auto shop", DeadDropGUID = "aaea12a7-ee38-47ba-aeb8-fb0d45a03957", ContactNpcName = "Kelly Reynolds" },
            ["Docks Warehouse"] = new() { Enabled = true, WeeklyRent = 2500f, DeadDropName = "Behind Randy's bait & tackle", DeadDropGUID = "baf08ceb-a0e7-4e4a-baa8-6b4cc992cb15", ContactNpcName = "Carl Bundy" },
        };

        public override void Validate()
        {
            RentIntervalDays = ConfigValidator.AtLeast(Name, nameof(RentIntervalDays), RentIntervalDays, 1);
            DaysUntilLockout = ConfigValidator.AtLeast(Name, nameof(DaysUntilLockout), DaysUntilLockout, 0);

            foreach (string key in Locations.Keys.ToList())
            {
                RentLocationConfiguration loc = Locations[key];
                loc.WeeklyRent = ConfigValidator.AtLeast(Name, $"Locations[{key}].WeeklyRent", loc.WeeklyRent, 0f);
            }
        }
    }

    public class ModRent : ModuleBase<ModRentConfiguration>
    {
        private static readonly SaveSlotStore<RentLocationState> Store = new("Rent", "rent state");

        private static readonly HashSet<string> LockedCodes = new();

        // Sentinel key written into the per-save store the first time the mod ever runs on a save. Its presence
        // means the established-vs-fresh baseline has already been decided and persisted. Not a real property
        // code, and the store is only ever iterated by property code, so it never collides with a location.
        // Versioned so a logic change (first-week-free baseline) can run a one-time migration on saves whose
        // baseline was written under the old "bill pre-existing properties immediately" rule.
        private const string BaselineKey = "__lithium_rent_baseline_v2__";
        private const string LegacyBaselineKey = "__lithium_rent_baseline__";

        private bool _baselineReady;
        private int _lastElapsedDay = -1;
        private bool _initialised;
        private float _nextDriveTime;

        // TEMP throttled diagnostics (caps each unique key at a few prints) — surfaces WHY the rent tick may
        // bail, without flooding the log. Uses Log.Warning so it shows even with Debug off.
        private static readonly Dictionary<string, int> _diag = new();
        private static void Diag(string key, string detail = null)
        {
            if (_diag.TryGetValue(key, out int c) && c >= 5)
                return;
            _diag[key] = c + 1;
            Log.Warning($"[Rent][DIAG] {key}{(detail != null ? " " + detail : "")}");
        }

        public override void Apply()
        {
            DiscoverLocations();

            if (!Configuration.Enabled)
                return;

            Store.Unload();
            LockedCodes.Clear();
            _baselineReady = false;
            _lastElapsedDay = -1;
            _initialised = false;
            _nextDriveTime = 0f;
            _diag.Clear();

            MelonCoroutines.Start(LoadReminderRoutine());
        }

        private static IEnumerator LoadReminderRoutine()
        {
            float waited = 0f;
            while (waited < 30f && !WorldReady())
            {
                yield return new WaitForSeconds(1f);
                waited += 1f;
            }

            if (!WorldReady())
            {
                Log.Warning("[Rent] World not ready after 30s; skipped load rent reminders.");
                yield break;
            }

            yield return new WaitForSeconds(2f);
            Core.Get<ModRent>()?.SendLoadReminders();
        }

        private static bool WorldReady() =>
            MessagingManager.Instance != null
            && NPCManager.NPCRegistry != null && NPCManager.NPCRegistry.Count > 0;

        // Frame-based driver, forwarded from Core.OnUpdate. Replaces the old TimeManager.PassMinute hook:
        // PassMinute is driven by the game's TimeLoop, which stalls whenever game-time is paused (ESC/pause
        // menu, timeScale == 0) or not yet flowing — so the rent tick (and with it the load-time lockout
        // reconcile + door enforcement) would silently never run until the player let the in-game clock
        // advance. OnUpdate fires every frame regardless, so the lockout now engages on load and the door
        // locks even while paused. Host-only: rent state is authoritative on the server and replicated to
        // clients via HostStateSync (the old PassMinute hook was implicitly host-only because TimeLoop runs
        // only on the server; that guarantee is restored here by the explicit IsServer check).
        public void DriveUpdate()
        {
            if (!Configuration.Enabled)
                return;
            if (!InstanceFinder.IsServer)
                return;

            // Throttle to ~1s real-time. The rent logic's real granularity is day-level; polling ElapsedDays
            // once a second catches a day rollover within a second of it happening in-game, and keeps the
            // per-frame cost negligible.
            float now = Time.realtimeSinceStartup;
            if (now < _nextDriveTime)
                return;
            _nextDriveTime = now + 1f;

            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Drive update failed: {e.Message}");
            }
        }

        public void Tick()
        {
            if (!Configuration.Enabled)
                return;

            TimeManager time = TimeManager.Instance;
            if (time == null)
            {
                Diag("time-null");
                return;
            }

            // Don't touch rent state until the save is fully loaded and the per-save baseline has been written.
            // EnsureBaseline decides — exactly once per save, persisted to disk — which properties pre-existed
            // (billed immediately) versus which are fresh purchases (grace). Until it succeeds, the owned-property
            // list may be incomplete and any decision we made would be wrong.
            if (!EnsureBaseline())
            {
                Diag("baseline-false", DescribeLoadState());
                return;
            }

            int today = time.ElapsedDays;
            Diag("tick-run", $"today={today} init={_initialised}");

            if (!_initialised)
            {
                DiscoverLocations();
                RebuildLockedFromState();
                // Enforce any lockout whose threshold was crossed since the last time ProcessDay ran (e.g.
                // across the reload boundary that resets _lastElapsedDay). Without this, a property that went
                // overdue while the game was closed reads as long-overdue yet stays accessible until the next
                // live day rollover.
                ReconcileLockoutsOnLoad(today);
                PublishAll();
                _lastElapsedDay = today;
                _initialised = true;
            }

            // Anchor any property acquired during play immediately, so its "freshly bought" grace status is
            // persisted before a save/reload (or Alt+F4) can happen.
            AnchorNewLocations(today);

            // Credit any rent cash sitting in the dead drops — dropped by ANY player, not just the host.
            // Tick() is host-only (TimeManager.PassMinute runs only on the server), so this is the
            // authoritative place to register payment for the whole game; the close-menu patch only gives
            // the host immediate feedback for its own drops.
            ScanDeadDropPayments();

            if (today == _lastElapsedDay)
                return;

            _lastElapsedDay = today;
            ProcessDay(today);
        }

        // Establishes — once per save, persisted — the rent baseline. The first time the mod ever runs on a save,
        // every property already owned is "pre-existing" and billed immediately; that decision is recorded by
        // writing each property's state plus a sentinel key. On every later load the sentinel is already present,
        // so we skip straight to ready — which means any owned property that still lacks state is necessarily a
        // fresh purchase (grace), never a pre-existing one. This is what makes a freshly-bought property survive
        // a reload/Alt+F4 without being billed on the spot. Gated on LoadManager reporting a fully loaded save so
        // the owned-property list is populated before we snapshot it. Returns true once the baseline exists.
        private static string DescribeLoadState()
        {
            LoadManager lm = LoadManager.Instance;
            if (lm == null)
                return "lm=null";
            return $"loaded={lm.IsGameLoaded} loading={lm.IsLoading} status={lm.LoadStatus} server={InstanceFinder.IsServer}";
        }

        private bool EnsureBaseline()
        {
            if (_baselineReady)
                return true;

            LoadManager lm = LoadManager.Instance;
            if (lm == null || !lm.IsGameLoaded || lm.IsLoading || lm.LoadStatus != LoadManager.ELoadStatus.None)
                return false;

            if (!Store.TryGet(BaselineKey, out _))
            {
                int today = TimeManager.Instance?.ElapsedDays ?? 0;

                // One-time migration for saves whose baseline was written by the old logic, which backdated
                // LastChargedDay and billed every pre-existing property on first sight (making them read
                // "overdue" with no matching lockout enforcement). Re-anchor each enabled, owned property that
                // is NOT genuinely locked out to the fresh-purchase grace anchor and clear the bogus debt,
                // granting the intended first-week-free retroactively. A real, enforced lockout is left intact.
                if (Store.TryGet(LegacyBaselineKey, out _))
                {
                    int migrated = 0;
                    foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
                    {
                        string code = prop.PropertyCode;
                        if (string.IsNullOrEmpty(code) || !Store.TryGet(code, out RentLocationState state))
                            continue;
                        if (state.LockedOut)
                            continue;

                        RentLocationState reset = new RentLocationState();
                        ApplyDueCharges(reset, loc, today);
                        Store.Set(code, reset);
                        migrated++;
                    }
                    Store.Remove(LegacyBaselineKey);
                    Store.Set(BaselineKey, new RentLocationState());
                    Log.Info($"[Rent] Migrated rent baseline to first-week-free ({migrated} location(s) re-anchored).");
                    _baselineReady = true;
                    return true;
                }

                int established = 0;
                foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
                {
                    string code = prop.PropertyCode;
                    if (string.IsNullOrEmpty(code) || Store.TryGet(code, out _))
                        continue;

                    RentLocationState state = new RentLocationState();
                    // First-week-free: pre-existing properties are anchored like a fresh purchase (no immediate
                    // charge); the first rent lands one interval from now.
                    ApplyDueCharges(state, loc, today);
                    Store.Set(code, state);
                    established++;
                }

                Store.Set(BaselineKey, new RentLocationState());
                Log.Info($"[Rent] Rent baseline established ({established} pre-existing location(s) anchored, first week free).");
            }

            _baselineReady = true;
            return true;
        }

        // Persists a fresh-purchase anchor for any enabled, owned location that has no state yet. Because the
        // established-vs-fresh decision is made once at baseline (EnsureBaseline) and persisted, any owned
        // location still lacking state afterwards must be a property acquired since — i.e. a fresh purchase that
        // gets the grace window, never an immediate charge. Writing the anchor here on every tick means even an
        // Alt+F4 right after buying can't make the property look pre-existing on reload (it already has state;
        // and if the tick was missed, the persisted baseline still classifies it as fresh). No money is taken.
        private void AnchorNewLocations(int today)
        {
            foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
            {
                string code = prop.PropertyCode;
                if (string.IsNullOrEmpty(code))
                    continue;
                if (Store.TryGet(code, out _))
                    continue;

                RentLocationState state = new RentLocationState();
                ApplyDueCharges(state, loc, today);
                Store.Set(code, state);
                PublishState(code, state);
                Log.Info($"[Rent] Anchored freshly acquired '{prop.PropertyName}' at day {today}; first period free.");
            }
        }

        // Debug/testing hotkey (F12): force every enabled, owned rent location into lockout immediately — no
        // waiting for the cadence — and text the contact, so both the lockout enforcement (e.g. the motel's
        // exterior door) and the messaging path can be verified in one press. Toggles: a second press clears
        // the lockout and the test debt, restoring access. Host-only (rent state lives on the host).
        public void DebugToggleLockout()
        {
            if (!Configuration.Enabled)
            {
                Log.Warning("[Rent] Lockout test ignored: the Rent module is disabled.");
                return;
            }
            if (!InstanceFinder.IsServer)
            {
                Log.Warning("[Rent] Lockout test ignored: only the host can change rent state.");
                return;
            }

            int today = TimeManager.Instance?.ElapsedDays ?? 0;
            int affected = 0;

            foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
            {
                string code = prop.PropertyCode;
                if (string.IsNullOrEmpty(code))
                    continue;

                RentLocationState state = Store.TryGet(code, out RentLocationState s) ? s : new RentLocationState();

                if (state.LockedOut)
                {
                    state.LockedOut = false;
                    state.Owed = 0f;
                    state.DueSinceDay = -1;
                    state.WarningSent = false;
                    LockedCodes.Remove(code);
                    RentMessenger.Send(loc, $"[TEST] Lockout cleared for {prop.PropertyName}. Your access is restored.");
                }
                else
                {
                    if (state.Owed <= 0f)
                        state.Owed = loc.WeeklyRent > 0f ? loc.WeeklyRent : 1f;
                    if (state.DueSinceDay < 0)
                        state.DueSinceDay = today;
                    state.LockedOut = true;
                    LockedCodes.Add(code);
                    RentMessenger.Send(loc,
                        $"[TEST] You're locked out of {prop.PropertyName}. Pay the ${state.Owed:N0} you owe at {loc.DeadDropName} to get back in.");
                }

                Store.Set(code, state);
                PublishState(code, state);
                affected++;
            }

            Log.Warning(affected > 0
                ? $"[Rent] Lockout test toggled {affected} owned, rent-enabled location(s)."
                : "[Rent] Lockout test: no owned, rent-enabled locations found (own the property and enable its rent first).");
        }

        private void ProcessDay(int today)
        {
            DiscoverLocations();

            foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
            {
                string code = prop.PropertyCode;
                RentLocationState state = Store.TryGet(code, out RentLocationState s) ? s : new RentLocationState();

                if (state.LockedOut)
                {
                    state.LastChargedDay = today;
                    RentMessenger.Send(loc, StillLockedMessage(prop, loc, state.Owed));
                    Store.Set(code, state);
                    PublishState(code, state);
                    continue;
                }

                bool charged = ApplyDueCharges(state, loc, today);

                bool messaged = false;

                if (charged)
                {
                    RentMessenger.Send(loc,
                        $"Rent of ${loc.WeeklyRent:N0} for {prop.PropertyName} is due. Drop it at {loc.DeadDropName}. " +
                        $"You have {Configuration.DaysUntilLockout} day(s) before I change the locks.");
                    messaged = true;
                }

                // Warning / lockout transition. Extracted into ReconcileOverdue so the SAME evaluation also
                // runs on load (ReconcileLockoutsOnLoad) and in SendLoadReminders — otherwise a threshold
                // crossed while ProcessDay wasn't watching the day roll over (charges caught up at load, or a
                // reload that reset the day tracker) would never lock the property, leaving it readable as
                // long-overdue yet still accessible.
                string transition = ReconcileOverdue(prop, loc, code, state, today);
                if (transition != null)
                {
                    RentMessenger.Send(loc, transition);
                    messaged = true;
                }

                if (state.Owed > 0f && state.DueSinceDay >= 0 && !state.LockedOut && !messaged)
                    RentMessenger.Send(loc, ReminderMessage(prop, loc, state.Owed));

                Store.Set(code, state);
                PublishState(code, state);
            }
        }

        private bool ApplyDueCharges(RentLocationState state, RentLocationConfiguration loc, int today)
        {
            if (state.LastChargedDay < 0)
            {
                // First sight (pre-existing or freshly bought): anchor the cadence to today so the first
                // charge lands exactly one rent period from now — the first period is always free, never
                // billed on the spot. This keeps the displayed "next due" date and the lockout enforcement
                // in lock-step (the old logic backdated the anchor and billed immediately, while ProcessDay
                // had never run the intervening days, so a property read "overdue" with no matching lockout).
                state.LastChargedDay = today;
            }

            bool charged = false;
            while (today - state.LastChargedDay >= Configuration.RentIntervalDays)
            {
                state.LastChargedDay += Configuration.RentIntervalDays;
                state.Owed += loc.WeeklyRent;
                if (state.DueSinceDay < 0)
                    state.DueSinceDay = state.LastChargedDay;
                charged = true;
            }
            return charged;
        }

        // Applies the final-warning and lockout transitions for a location based on its CURRENT overdue
        // count. A pure function of (today, state): unlike the surrounding day-gated messaging in ProcessDay,
        // this is safe to run on load and on any day, so a lockout threshold crossed while ProcessDay wasn't
        // watching the day roll over (charges caught up at load by SendLoadReminders, or a day advanced
        // across a reload that reset _lastElapsedDay) is still enforced. Mutates state (LockedOut /
        // WarningSent) and LockedCodes; returns the message for a NEW transition, or null if nothing changed
        // (already locked, paid up, or not yet at a threshold). Lockout is checked before the warning because
        // a catch-up can jump straight past the warning day.
        private string ReconcileOverdue(Property prop, RentLocationConfiguration loc, string code, RentLocationState state, int today)
        {
            if (state.LockedOut || state.Owed <= 0f || state.DueSinceDay < 0)
                return null;

            int overdue = today - state.DueSinceDay;

            if (overdue >= Configuration.DaysUntilLockout)
            {
                state.LockedOut = true;
                LockedCodes.Add(code);
                return $"You're locked out of {prop.PropertyName}. Pay the ${state.Owed:N0} you owe at " +
                       $"{loc.DeadDropName} to get back in.";
            }

            if (Configuration.SendFinalWarning && !state.WarningSent
                && Configuration.DaysUntilLockout >= 1 && overdue == Configuration.DaysUntilLockout - 1)
            {
                state.WarningSent = true;
                return $"Final warning: ${state.Owed:N0} rent still owed for {prop.PropertyName}. " +
                       $"Pay at {loc.DeadDropName} by tomorrow or you're locked out.";
            }

            return null;
        }

        // Re-evaluates every owned, rent-enabled location's lockout/warning against the CURRENT overdue count
        // the moment a save finishes loading. ProcessDay only crosses those thresholds on a live in-game day
        // rollover, but the day can advance across a load boundary (every load resets _lastElapsedDay to
        // today, so the rollover that pushed the property past the threshold is never "seen") or the charges
        // can be caught up at load by SendLoadReminders (which never locks). Either way the property would
        // accrue overdue days while staying unlocked. This pass closes that gap. State-only: the single load
        // notification is left to SendLoadReminders, so the returned transition message is discarded here.
        private void ReconcileLockoutsOnLoad(int today)
        {
            int seen = 0;
            foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
            {
                seen++;
                string code = prop.PropertyCode;
                if (string.IsNullOrEmpty(code) || !Store.TryGet(code, out RentLocationState state))
                {
                    Diag("reconcile-skip", $"{prop.PropertyName} code={code} (no state)");
                    continue;
                }
                if (state.LockedOut)
                {
                    Diag("reconcile-already-locked", prop.PropertyName);
                    continue;
                }

                ApplyDueCharges(state, loc, today);
                ReconcileOverdue(prop, loc, code, state, today);
                Store.Set(code, state);
                PublishState(code, state);
                Diag("reconcile-loc", $"{prop.PropertyName} owed={state.Owed} due={state.DueSinceDay} today={today} -> locked={state.LockedOut}");
            }
            Diag("reconcile-done", $"ownedEnabled={seen}");
        }

        private void SendLoadReminders()
        {
            if (!Configuration.Enabled)
                return;

            TimeManager time = TimeManager.Instance;
            if (time == null)
                return;

            if (!EnsureBaseline())
                return;

            int today = time.ElapsedDays;

            int sent = 0;
            foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
            {
                string code = prop.PropertyCode;
                RentLocationState state = Store.TryGet(code, out RentLocationState s) ? s : new RentLocationState();

                if (!state.LockedOut)
                {
                    ApplyDueCharges(state, loc, today);
                    // Lock if already past the threshold, so the message below reflects it (StillLocked vs
                    // Reminder). The transition text is discarded — the load reminder below is the single
                    // notification. Harmless if the Tick init block already locked it (ReconcileOverdue no-ops
                    // once LockedOut is set).
                    ReconcileOverdue(prop, loc, code, state, today);
                }

                Store.Set(code, state);
                PublishState(code, state);

                if (state.Owed <= 0f)
                    continue;

                RentMessenger.Send(loc, state.LockedOut
                    ? StillLockedMessage(prop, loc, state.Owed)
                    : ReminderMessage(prop, loc, state.Owed));
                sent++;
            }

            Log.Info($"[Rent] Load reminders sent for {sent} location(s) with outstanding rent.");
        }

        private static string StillLockedMessage(Property prop, RentLocationConfiguration loc, float owed) =>
            $"You're still locked out of {prop.PropertyName}. Pay the ${owed:N0} you owe at " +
            $"{loc.DeadDropName} to get back in.";

        private static string ReminderMessage(Property prop, RentLocationConfiguration loc, float owed) =>
            $"Reminder: ${owed:N0} rent still owed for {prop.PropertyName}. Drop it at {loc.DeadDropName}.";

        // Immediate-feedback path: when a player closes a dead-drop menu on the HOST, credit straight away.
        // Guarded to the server because rent state (the owed balance) only exists on the host, and taking the
        // cash (CashInstance.SetBalance) must be done by the server to network the removal. A client closing
        // the menu does nothing here — its drop is instead picked up by ScanDeadDropPayments on the host tick.
        public void ProcessPayment(StorageEntity closed)
        {
            if (!Configuration.Enabled || closed == null || !InstanceFinder.IsServer)
                return;

            try
            {
                int closedId = closed.GetInstanceID();
                foreach (DeadDrop dd in DeadDrop.DeadDrops)
                {
                    if (dd != null && dd.Storage != null && dd.Storage.GetInstanceID() == closedId)
                    {
                        CreditFromDrop(dd);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Payment processing failed: {e.Message}");
            }
        }

        // Host-only sweep over every rent dead drop, run each tick from Tick(). This is what makes a payment
        // "count for the game" rather than only for the host: a dead drop's storage is networked, so the host
        // sees cash that ANY player dropped — even though that client's local menu-close event never reaches
        // the host. Without this, only rent the host personally dropped (and closed) would ever be credited.
        public void ScanDeadDropPayments()
        {
            if (!Configuration.Enabled || !InstanceFinder.IsServer)
                return;

            Il2CppSystem.Collections.Generic.List<DeadDrop> drops = DeadDrop.DeadDrops;
            if (drops == null)
                return;

            foreach (DeadDrop dd in drops)
            {
                if (dd == null || dd.Storage == null)
                    continue;

                try
                {
                    CreditFromDrop(dd);
                }
                catch (Exception e)
                {
                    Log.Warning($"[Rent] Dead drop payment scan failed: {e.Message}");
                }
            }
        }

        // Credits any cash sitting in this dead drop against the owed rent of its matching, owned location.
        // Bails fast (no match / nothing owed / no cash) so it is cheap to call for every drop every tick.
        // Caller guarantees this runs on the server (see ProcessPayment / ScanDeadDropPayments).
        private void CreditFromDrop(DeadDrop drop)
        {
            if (drop == null || drop.Storage == null)
                return;

            string dropName = drop.DeadDropName;
            string dropGuid = drop.GUID.ToString();

            string matchKey = null;
            RentLocationConfiguration loc = null;
            foreach (KeyValuePair<string, RentLocationConfiguration> kv in Configuration.Locations)
            {
                RentLocationConfiguration c = kv.Value;
                if (!c.Enabled)
                    continue;
                bool match = (!string.IsNullOrEmpty(c.DeadDropGUID) && string.Equals(c.DeadDropGUID, dropGuid, StringComparison.OrdinalIgnoreCase))
                             || (string.IsNullOrEmpty(c.DeadDropGUID) && !string.IsNullOrEmpty(c.DeadDropName) && c.DeadDropName == dropName);
                if (match)
                {
                    matchKey = kv.Key;
                    loc = c;
                    break;
                }
            }
            if (loc == null)
                return;

            Property prop = FindOwned(matchKey);
            if (prop == null)
                return;

            string code = prop.PropertyCode;

            // The phone Property tab shows rent and electricity together, and electricity is paid here in cash
            // (no bank auto-deduct). So this drop settles BOTH: rent first, then the same property's outstanding
            // power bill from whatever cash is left — and reports them in ONE message from the landlord.

            // 1) Rent — take cash up to what's owed for this location.
            float rentPaid = 0f, rentOwedAfter = 0f;
            bool rentCleared = false, accessRestored = false;
            if (Store.TryGet(code, out RentLocationState state) && state.Owed > 0f)
            {
                rentPaid = TakeCash(drop.Storage, state.Owed);
                if (rentPaid > 0f)
                {
                    state.Owed -= rentPaid;
                    if (state.Owed < 0.01f)
                    {
                        state.Owed = 0f;
                        state.DueSinceDay = -1;
                        state.WarningSent = false;
                        accessRestored = state.LockedOut;
                        state.LockedOut = false;
                        LockedCodes.Remove(code);
                        rentCleared = true;
                    }
                    rentOwedAfter = state.Owed;
                    Store.Set(code, state);
                    PublishState(code, state);
                }
            }

            // 2) Power bill — settle this property's outstanding electricity from the remaining cash. Without
            // this, cash meant for the power bill would sit in the drop, ignored. announce:false suppresses the
            // ElectricBill module's own notification so the result is reported in the combined message below.
            // No-op when the ElectricBill module is off or nothing is outstanding.
            float electricPaid = 0f, electricOwedAfter = 0f;
            bool electricCleared = false, powerRestored = false;
            ModElectricBill electric = Core.Get<ModElectricBill>();
            if (electric != null)
            {
                float outstanding = electric.GetOutstandingBill(code);
                if (outstanding > 0f)
                {
                    float billCash = TakeCash(drop.Storage, outstanding);
                    if (billCash > 0f)
                    {
                        ModElectricBill.CashPaymentResult r = electric.ApplyCashPayment(prop, billCash, announce: false);
                        electricPaid = r.Paid;
                        electricOwedAfter = r.Remaining;
                        electricCleared = r.Cleared;
                        powerRestored = r.PowerRestored;
                    }
                }
            }

            if (rentPaid > 0f || electricPaid > 0f)
                RentMessenger.Send(loc, ComposeDropMessage(
                    prop, loc, rentPaid, rentOwedAfter, rentCleared, accessRestored,
                    electricPaid, electricOwedAfter, electricCleared, powerRestored));
        }

        // Builds the single landlord message confirming a dead-drop payment, covering rent and (when the
        // ElectricBill module settled one here) electricity together.
        private static string ComposeDropMessage(
            Property prop, RentLocationConfiguration loc,
            float rentPaid, float rentOwed, bool rentCleared, bool accessRestored,
            float electricPaid, float electricOwed, bool electricCleared, bool powerRestored)
        {
            StringBuilder sb = new();
            sb.Append($"Received ${rentPaid + electricPaid:N0} for {prop.PropertyName}");

            List<string> parts = new();
            if (rentPaid > 0f)
                parts.Add(rentCleared
                    ? $"${rentPaid:N0} rent (paid up)"
                    : $"${rentPaid:N0} rent (${rentOwed:N0} still owed)");
            if (electricPaid > 0f)
                parts.Add(electricCleared
                    ? $"${electricPaid:N0} electricity (paid up)"
                    : $"${electricPaid:N0} electricity (${electricOwed:N0} still owed)");
            if (parts.Count > 0)
                sb.Append(": ").Append(string.Join(", ", parts));
            sb.Append('.');

            if (accessRestored)
                sb.Append(" Your access is restored.");
            if (powerRestored)
                sb.Append(" Power is back on.");

            if ((rentPaid > 0f && !rentCleared) || (electricPaid > 0f && !electricCleared))
                sb.Append($" Drop the rest at {loc.DeadDropName}.");

            return sb.ToString();
        }

        private static float TakeCash(StorageEntity storage, float max)
        {
            float remaining = max;
            foreach (ItemSlot slot in storage.ItemSlots)
            {
                if (remaining <= 0f)
                    break;
                ItemInstance inst = slot?.ItemInstance;
                CashInstance cash = inst != null ? inst.TryCast<CashInstance>() : null;
                if (cash == null)
                    continue;

                float bal = cash.Balance;
                if (bal <= 0f)
                    continue;

                float take = Math.Min(bal, remaining);
                cash.SetBalance(bal - take);
                remaining -= take;
            }
            return max - remaining;
        }

        public static bool IsLockedOut(string propertyCode)
        {
            if (string.IsNullOrEmpty(propertyCode))
                return false;

            // The lockout decision is made and stored only on the host (Tick is host-only). Clients learn it
            // through the replicated variable so a non-host player is barred from an unpaid property too.
            if (!InstanceFinder.IsServer)
                return HostStateSync.GetBool($"rent_locked_{propertyCode}", false);

            return LockedCodes.Contains(propertyCode);
        }

        // Host-only: mirror a location's rent state onto the replication channel so clients can enforce the
        // lockout and show correct figures in the phone app. No-op on clients (SetBool/SetNumber self-guard).
        private static void PublishState(string code, RentLocationState state)
        {
            if (string.IsNullOrEmpty(code) || state == null)
                return;
            HostStateSync.SetBool($"rent_locked_{code}", state.LockedOut);
            HostStateSync.SetNumber($"rent_owed_{code}", state.Owed);
            HostStateSync.SetNumber($"rent_lastcharged_{code}", state.LastChargedDay);
            HostStateSync.SetNumber($"rent_duesince_{code}", state.DueSinceDay);
        }

        // Host-only: re-assert every owned location's current rent state onto the channel. Called on load so
        // an already-connected client converges without waiting for the next state change.
        private void PublishAll()
        {
            if (!InstanceFinder.IsServer)
                return;
            foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
            {
                _ = loc;
                if (Store.TryGet(prop.PropertyCode, out RentLocationState state))
                    PublishState(prop.PropertyCode, state);
            }
        }

        // Resolves the rent state used to build a phone-app view: the host reads its authoritative save store;
        // a client reconstructs it from the values the host replicated over HostStateSync (its own store is
        // empty because the rent tick never runs on clients).
        private bool TryGetViewState(string code, out RentLocationState state)
        {
            if (InstanceFinder.IsServer)
                return Store.TryGet(code, out state);

            state = new RentLocationState
            {
                LockedOut = HostStateSync.GetBool($"rent_locked_{code}", false),
                Owed = HostStateSync.GetNumber($"rent_owed_{code}", 0f),
                LastChargedDay = (int)HostStateSync.GetNumber($"rent_lastcharged_{code}", -1f),
                DueSinceDay = (int)HostStateSync.GetNumber($"rent_duesince_{code}", -1f),
            };
            return true;
        }

        // Read-only snapshot of a rent location for the phone app. Computed on demand from config + the
        // per-save store; no state is mutated here.
        public sealed class RentAppView
        {
            public string PropertyName;
            public string PropertyCode;
            public float WeeklyRent;
            public float Owed;          // outstanding total; 0 when paid up
            public bool Paid;           // nothing currently owed (incl. the first owned week)
            public bool FirstWeek;      // still in the grace / first week of ownership (no rent charged yet)
            public bool LockedOut;
            public bool HasNextDue;     // upcoming charge known (shown while paid)
            public int DaysUntilDue;
            public EDay NextDueWeekday;
            public bool HasOwedDue;     // due date of the current debt (shown while unpaid)
            public int DaysOverdue;
            public EDay OwedDueWeekday;
            public string DeadDropName;
            public string ContactName;
        }

        // One view per owned, rent-enabled location (exactly what the app dropdown lists). Empty when the
        // module is disabled.
        public List<RentAppView> GetAppViews()
        {
            List<RentAppView> views = new();
            if (!Configuration.Enabled)
                return views;

            TimeManager time = TimeManager.Instance;
            int today = time != null ? time.ElapsedDays : -1;
            EDay todayDow = time != null ? time.CurrentDay : EDay.Monday;

            foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
            {
                bool hasState = TryGetViewState(prop.PropertyCode, out RentLocationState state);

                RentAppView v = new()
                {
                    PropertyName = prop.PropertyName,
                    PropertyCode = prop.PropertyCode,
                    WeeklyRent = loc.WeeklyRent,
                    DeadDropName = string.IsNullOrEmpty(loc.DeadDropName) ? "—" : loc.DeadDropName,
                    ContactName = string.IsNullOrEmpty(loc.ContactNpcName) ? "—" : loc.ContactNpcName,
                    Owed = hasState ? state.Owed : 0f,
                    LockedOut = hasState && state.LockedOut,
                };

                // First week of ownership: no rent has been charged yet (no state, or the cadence is
                // anchored at/after today by the fresh-purchase grace). The Owed == 0 guard is essential:
                // ApplyDueCharges advances the anchor with `LastChargedDay += RentIntervalDays`, which lands
                // exactly ON today the day a charge fires (and the locked-out branch sets LastChargedDay =
                // today too) — so `LastChargedDay >= today` is ALSO true on a freshly-charged, genuinely-owed
                // day. Without the guard the app mislabels that day as first-week/paid (hiding the new charge),
                // then abruptly reads overdue the next day — i.e. "I paid recently, app said paid, slept once,
                // suddenly overdue".
                v.FirstWeek = !hasState || state.LastChargedDay < 0
                              || (today >= 0 && state.LastChargedDay >= today && v.Owed <= 0f);
                // Paid strictly means nothing is owed — never let the grace label paper over a real debt.
                v.Paid = v.Owed <= 0f;

                // Upcoming charge date (while paid).
                if (hasState && state.LastChargedDay >= 0 && today >= 0)
                {
                    v.DaysUntilDue = Math.Max(0, state.LastChargedDay + Configuration.RentIntervalDays - today);
                    v.NextDueWeekday = Weekday(todayDow, v.DaysUntilDue);
                    v.HasNextDue = true;
                }

                // Due date of the current debt (while unpaid).
                if (hasState && v.Owed > 0f && !v.FirstWeek && state.DueSinceDay >= 0 && today >= 0)
                {
                    v.DaysOverdue = Math.Max(0, today - state.DueSinceDay);
                    v.OwedDueWeekday = Weekday(todayDow, state.DueSinceDay - today);
                    v.HasOwedDue = true;
                }

                views.Add(v);
            }
            return views;
        }

        private static EDay Weekday(EDay today, int dayOffset) =>
            (EDay)((((int)today + dayOffset) % 7 + 7) % 7);

        private void RebuildLockedFromState()
        {
            try
            {
                LockedCodes.Clear();
                foreach (Property prop in AllOwned())
                {
                    if (Store.TryGet(prop.PropertyCode, out RentLocationState state) && state.LockedOut)
                        LockedCodes.Add(prop.PropertyCode);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Failed to rebuild locked set: {e.Message}");
            }
        }

        private void DiscoverLocations()
        {
            try
            {
                bool added = false;
                foreach (Property prop in AllProperties())
                {
                    string name = prop.PropertyName;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (!Configuration.Locations.ContainsKey(name))
                    {
                        Configuration.Locations[name] = new RentLocationConfiguration();
                        added = true;
                        Log.Info($"[Rent] Discovered location '{name}'");
                    }
                }
                if (added)
                    Configuration.SaveConfiguration();
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Location discovery failed: {e.Message}");
            }
        }

        private IEnumerable<(Property, RentLocationConfiguration)> EnabledOwnedLocations()
        {
            foreach (Property prop in AllOwned())
            {
                if (Configuration.Locations.TryGetValue(prop.PropertyName, out RentLocationConfiguration loc)
                    && loc.Enabled && loc.WeeklyRent > 0f)
                    yield return (prop, loc);
            }
        }

        private static Property FindOwned(string propertyName)
        {
            foreach (Property prop in AllOwned())
            {
                if (prop.PropertyName == propertyName)
                    return prop;
            }
            return null;
        }

        private static IEnumerable<Property> AllOwned()
        {
            return Deduplicate(Property.OwnedProperties, Business.OwnedBusinesses);
        }

        private static IEnumerable<Property> AllProperties()
        {
            return Deduplicate(Property.Properties, Business.Businesses);
        }

        private static IEnumerable<Property> Deduplicate(
            Il2CppSystem.Collections.Generic.List<Property> properties,
            Il2CppSystem.Collections.Generic.List<Business> businesses)
        {
            HashSet<string> seen = new();

            if (properties != null)
            {
                foreach (Property p in properties)
                {
                    if (p == null)
                        continue;
                    string c = p.PropertyCode;
                    if (!string.IsNullOrEmpty(c) && seen.Add(c))
                        yield return p;
                }
            }

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
