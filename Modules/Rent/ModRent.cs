using System;
using System.Collections;
using System.Collections.Generic;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace Lithium.Modules.Rent
{
    /// <summary>Per-location rent settings. You fill in the dead drop and contact; entries are auto-seeded
    /// (disabled, $0) for every owned property/business the first time a save loads so the exact names appear.</summary>
    public class RentLocationConfiguration
    {
        /// <summary>Whether this location charges rent. Auto-seeded entries start disabled.</summary>
        [JsonProperty(Order = 1)] public bool Enabled = false;

        /// <summary>Rent charged every <see cref="ModRentConfiguration.RentIntervalDays"/> in-game days.</summary>
        [JsonProperty(Order = 2)] public float WeeklyRent = 0f;

        /// <summary>Display name of the dead drop where rent is paid (shown in the messages). Find names via the F8 dump.</summary>
        [JsonProperty(Order = 3)] public string DeadDropName = "";

        /// <summary>Optional dead drop GUID, used in preference to the name if two drops share a name (see F8 dump).</summary>
        [JsonProperty(Order = 4)] public string DeadDropGUID = "";

        /// <summary>Full name of the NPC who texts about this location's rent (see the F8 dump's contacts list).</summary>
        [JsonProperty(Order = 5)] public string ContactNpcName = "Fixer";
    }

    public class ModRentConfiguration : ModuleConfiguration
    {
        public override string Name => "Rent";

        /// <summary>In-game days between rent charges (a "week"). Each location's cadence is anchored to when it was first seen owned.</summary>
        [JsonProperty(Order = 1)] public int RentIntervalDays = 7;

        /// <summary>Days after rent becomes due, while still unpaid, before the property is locked to the player. 0 = lock immediately.</summary>
        [JsonProperty(Order = 2)] public int DaysUntilLockout = 2;

        /// <summary>When true, a final warning text is sent the day before lockout.</summary>
        [JsonProperty(Order = 3)] public bool SendFinalWarning = true;

        /// <summary>
        /// Days of "paid" rent a freshly bought property gets before rent applies. A property bought during
        /// play isn't charged for the period it was bought in — its first rent lands one interval later — and
        /// any charge that would fall within this many days of the purchase is also waived (only matters when
        /// <see cref="RentIntervalDays"/> is short). Properties already owned when the save loads are
        /// unaffected: they're billed the current week immediately. 0 = bill freshly bought properties at once.
        /// </summary>
        [JsonProperty(Order = 4)] public int FreshPurchaseGraceDays = 3;

        /// <summary>
        /// Per-location settings, keyed by the property's display name. These ship as sensible defaults; any
        /// location not listed is auto-discovered (disabled, $0) the first time a save loads.
        /// </summary>
        [JsonProperty(Order = 5)] public Dictionary<string, RentLocationConfiguration> Locations = new()
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
            FreshPurchaseGraceDays = ConfigValidator.AtLeast(Name, nameof(FreshPurchaseGraceDays), FreshPurchaseGraceDays, 0);

            foreach (string key in Locations.Keys.ToList())
            {
                RentLocationConfiguration loc = Locations[key];
                loc.WeeklyRent = ConfigValidator.AtLeast(Name, $"Locations[{key}].WeeklyRent", loc.WeeklyRent, 0f);
            }
        }
    }

    /// <summary>
    /// Weekly rent per location, paid by dropping cash at an assigned dead drop. Charging, overdue warnings
    /// and the lock-out (exterior-only, so you can always leave but not return until paid) run from a daily
    /// tick (<c>TimeManager.PassMinute</c> postfix detecting a day change). Rent is frozen while a location is
    /// locked. Per-save state lives in <see cref="RentLocationState"/>, persisted via a <see cref="SaveSlotStore{TValue}"/>.
    /// </summary>
    public class ModRent : ModuleBase<ModRentConfiguration>
    {
        private static readonly SaveSlotStore<RentLocationState> Store = new("Rent", "rent state");

        // Property codes currently locked for non-payment. Kept in memory so the door-access patch (called
        // very frequently) never touches disk. Rebuilt from persisted state on first tick after a load.
        private static readonly HashSet<string> LockedCodes = new();

        // Property codes owned at the moment this save loaded. Captured once (after ownership is restored) and
        // consulted only when a location is first seen: those present at load are "established" and bill the
        // current week immediately, while anything that becomes owned later is "freshly bought" and gets the
        // FreshPurchaseGraceDays grace. In-memory only — the resulting anchor is persisted, so the
        // established/fresh choice is made once per location and never re-evaluated on later loads.
        private readonly HashSet<string> _establishedAtLoad = new();
        private bool _establishedCaptured;

        private int _lastElapsedDay = -1;
        private bool _initialised;

        public override void Apply()
        {
            // Always publish the location list (even when disabled) so every owned property/business shows up
            // in Rent.json to be configured before the module is ever turned on.
            DiscoverLocations();

            if (!Configuration.Enabled)
                return;

            // A save just loaded: drop in-memory state so the next access re-resolves this save's file.
            Store.Unload();
            LockedCodes.Clear();
            _establishedAtLoad.Clear();
            _establishedCaptured = false;
            _lastElapsedDay = -1;
            _initialised = false;

            // Text the player any outstanding rent as soon as the world has finished loading. Can't do this
            // inline: on a fresh load the NPC registry / messaging aren't populated yet, so a message sent now
            // would be silently dropped (the contact NPC won't resolve). Wait for readiness like ModCustomers.
            MelonCoroutines.Start(LoadReminderRoutine());
        }

        // Waits (capped) for messaging + the NPC registry to come up after a load, then sends the outstanding
        // -rent reminders once. Mirrors ModCustomers' startup-overview routine.
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

            // Small buffer so ownership has settled (properties are restored a moment after the scene inits).
            yield return new WaitForSeconds(2f);
            Core.Get<ModRent>()?.SendLoadReminders();
        }

        private static bool WorldReady() =>
            MessagingManager.Instance != null
            && NPCManager.NPCRegistry != null && NPCManager.NPCRegistry.Count > 0;

        /// <summary>Called every in-game minute; does real work only when the day rolls over.</summary>
        public void Tick()
        {
            if (!Configuration.Enabled)
                return;

            TimeManager time = TimeManager.Instance;
            if (time == null)
                return;

            int today = time.ElapsedDays;

            // First tick after a load: seed config from owned locations and rebuild the locked set, but don't
            // process a day (properties/save may have only just become available).
            if (!_initialised)
            {
                DiscoverLocations();
                CaptureEstablishedAtLoad();
                RebuildLockedFromState();
                _lastElapsedDay = today;
                _initialised = true;
                return;
            }

            if (today == _lastElapsedDay)
                return;

            _lastElapsedDay = today;
            ProcessDay(today);
        }

        private void ProcessDay(int today)
        {
            DiscoverLocations(); // pick up anything bought since the last load

            foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
            {
                string code = prop.PropertyCode;
                RentLocationState state = Store.TryGet(code, out RentLocationState s) ? s : new RentLocationState();

                // Frozen while locked: advance the anchor so no back-rent accrues, but keep the owed balance.
                // Still owed, so keep texting daily until they pay their way back in.
                if (state.LockedOut)
                {
                    state.LastChargedDay = today;
                    RentMessenger.Send(loc, StillLockedMessage(prop, loc, state.Owed));
                    Store.Set(code, state);
                    continue;
                }

                // Apply due charges. On first sight the anchor depends on whether this property was already
                // owned at load (current week billed now) or bought during play (first period free per grace).
                bool charged = ApplyDueCharges(state, loc, today, _establishedAtLoad.Contains(code));

                // Track whether this location already texted the player this day, so the daily reminder
                // below doesn't pile on top of a charge / warning / lockout notice.
                bool messaged = false;

                if (charged)
                {
                    RentMessenger.Send(loc,
                        $"Rent of ${loc.WeeklyRent:N0} for {prop.PropertyName} is due. Drop it at {loc.DeadDropName}. " +
                        $"You have {Configuration.DaysUntilLockout} day(s) before I change the locks.");
                    messaged = true;
                }

                // Overdue handling.
                if (state.Owed > 0f && state.DueSinceDay >= 0)
                {
                    int overdue = today - state.DueSinceDay;

                    if (Configuration.SendFinalWarning && !state.WarningSent
                        && Configuration.DaysUntilLockout >= 1 && overdue == Configuration.DaysUntilLockout - 1)
                    {
                        state.WarningSent = true;
                        RentMessenger.Send(loc,
                            $"Final warning: ${state.Owed:N0} rent still owed for {prop.PropertyName}. " +
                            $"Pay at {loc.DeadDropName} by tomorrow or you're locked out.");
                        messaged = true;
                    }

                    if (overdue >= Configuration.DaysUntilLockout)
                    {
                        state.LockedOut = true;
                        LockedCodes.Add(code);
                        RentMessenger.Send(loc,
                            $"You're locked out of {prop.PropertyName}. Pay the ${state.Owed:N0} you owe at " +
                            $"{loc.DeadDropName} to get back in.");
                        messaged = true;
                    }
                    else if (!messaged)
                    {
                        // Rent still outstanding but no other notice today: send a daily reminder so the
                        // player gets a nudge from the landlord at the start of every day they owe.
                        RentMessenger.Send(loc, ReminderMessage(prop, loc, state.Owed));
                    }
                }

                Store.Set(code, state);
            }
        }

        /// <summary>
        /// Initialises the cadence anchor on first sight and applies every weekly charge that has come due,
        /// mutating <paramref name="state"/> in place; returns whether anything was charged. The caller
        /// persists the result. The first-sight anchor depends on <paramref name="establishedAtLoad"/>:
        /// <list type="bullet">
        /// <item>established (owned when the save loaded) — anchored one interval in the past, so the current
        /// week's rent bills immediately;</item>
        /// <item>freshly bought (acquired during play) — the period it was bought in is free, so the anchor is
        /// set to today, deferring the first charge a full interval. Any whole interval that still fits inside
        /// <see cref="ModRentConfiguration.FreshPurchaseGraceDays"/> is pre-skipped too (only bites when the
        /// interval is shorter than the grace).</item>
        /// </list>
        /// </summary>
        private bool ApplyDueCharges(RentLocationState state, RentLocationConfiguration loc, int today, bool establishedAtLoad)
        {
            if (state.LastChargedDay < 0)
            {
                int interval = Configuration.RentIntervalDays;
                state.LastChargedDay = establishedAtLoad
                    ? today - interval                                              // bill the current week now
                    : today + (Configuration.FreshPurchaseGraceDays / interval) * interval; // free until first charge
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

        /// <summary>
        /// Once the world is ready after a save loads, bring every enabled, owned location's rent up to date
        /// (charging the current week on first sight) and text the player its status if anything is owed — so
        /// outstanding rent is both billed and surfaced right away rather than waiting for the next day
        /// rollover. The charged state is persisted, so it survives the next save/load.
        /// </summary>
        private void SendLoadReminders()
        {
            if (!Configuration.Enabled)
                return;

            TimeManager time = TimeManager.Instance;
            if (time == null)
                return;
            int today = time.ElapsedDays;

            // Everything owned at this point is "established", so first-sight billing here charges the current
            // week (not the freshly-bought grace, which is reserved for properties acquired later during play).
            CaptureEstablishedAtLoad();

            int sent = 0;
            foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
            {
                string code = prop.PropertyCode;
                RentLocationState state = Store.TryGet(code, out RentLocationState s) ? s : new RentLocationState();

                // Bill the current week (and any back weeks) now, unless the location is frozen by a lockout.
                if (!state.LockedOut)
                    ApplyDueCharges(state, loc, today, _establishedAtLoad.Contains(code));

                Store.Set(code, state); // persist the freshly billed state so it survives save/load

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

        /// <summary>
        /// Called when a storage UI closes. If the closed storage is a dead drop assigned to a rent location,
        /// deduct up to the outstanding rent from the cash sitting in it (leftover cash stays), update state,
        /// and text the result. Best-effort — never throws into the game's close path.
        /// </summary>
        public void ProcessPayment(StorageEntity closed)
        {
            if (!Configuration.Enabled || closed == null)
                return;

            try
            {
                int closedId = closed.GetInstanceID();
                DeadDrop drop = null;
                foreach (DeadDrop dd in DeadDrop.DeadDrops)
                {
                    if (dd != null && dd.Storage != null && dd.Storage.GetInstanceID() == closedId)
                    {
                        drop = dd;
                        break;
                    }
                }
                if (drop == null)
                    return;

                string dropName = drop.DeadDropName;
                string dropGuid = drop.GUID.ToString();

                // Find the location whose config points at this drop (GUID wins over name).
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
                if (!Store.TryGet(code, out RentLocationState state) || state.Owed <= 0f)
                    return; // nothing owed — don't touch the player's cash or spam them

                float paid = TakeCash(drop.Storage, state.Owed);
                if (paid <= 0f)
                    return;

                state.Owed -= paid;
                if (state.Owed < 0.01f)
                {
                    state.Owed = 0f;
                    state.DueSinceDay = -1;
                    state.WarningSent = false;
                    bool wasLocked = state.LockedOut;
                    state.LockedOut = false;
                    LockedCodes.Remove(code);
                    RentMessenger.Send(loc, wasLocked
                        ? $"Rent paid in full for {prop.PropertyName}. Your access is restored."
                        : $"Received ${paid:N0}. {prop.PropertyName} rent is paid up. Thanks.");
                }
                else
                {
                    RentMessenger.Send(loc,
                        $"Received ${paid:N0} toward {prop.PropertyName} rent. Still owed: ${state.Owed:N0} at {loc.DeadDropName}.");
                }

                Store.Set(code, state);
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Payment processing failed: {e.Message}");
            }
        }

        /// <summary>Removes up to <paramref name="max"/> cash from the storage's cash items; returns the amount taken.</summary>
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
                cash.SetBalance(bal - take); // clears the item when it hits 0
                remaining -= take;
            }
            return max - remaining;
        }

        /// <summary>True when the module is on and the given property is currently locked for non-payment.</summary>
        public static bool IsLockedOut(string propertyCode)
        {
            return !string.IsNullOrEmpty(propertyCode) && LockedCodes.Contains(propertyCode);
        }

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

        /// <summary>
        /// Snapshot (once per load) the codes of every property/business owned right now. Whichever post-load
        /// path runs first — the first tick or the load-reminder coroutine — captures the set; both run after
        /// ownership has been restored. Anything not in this set when first seen was bought during play and so
        /// qualifies for the freshly-bought grace.
        /// </summary>
        private void CaptureEstablishedAtLoad()
        {
            if (_establishedCaptured)
                return;
            try
            {
                _establishedAtLoad.Clear();
                foreach (Property prop in AllOwned())
                {
                    if (!string.IsNullOrEmpty(prop.PropertyCode))
                        _establishedAtLoad.Add(prop.PropertyCode);
                }
                _establishedCaptured = true;
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Failed to capture established locations: {e.Message}");
            }
        }

        /// <summary>Seed a (disabled, $0) config entry for any owned property/business not yet present, so its exact name is editable.</summary>
        private void DiscoverLocations()
        {
            try
            {
                bool added = false;
                // List every property/business, not just owned ones: scene property objects are present at
                // load while ownership is restored from the save a moment later, and pre-buy configuration is
                // useful. The charging logic still filters to owned+enabled, so unowned entries never bill.
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

        /// <summary>All owned properties and businesses, de-duplicated by <c>PropertyCode</c> (Business is a Property).</summary>
        private static IEnumerable<Property> AllOwned()
        {
            return Deduplicate(Property.OwnedProperties, Business.OwnedBusinesses);
        }

        /// <summary>Every property and business (owned or not), de-duplicated by <c>PropertyCode</c>. Used for config discovery.</summary>
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
