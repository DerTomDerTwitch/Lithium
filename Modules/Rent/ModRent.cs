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

        [JsonProperty(Order = 1)] public int RentIntervalDays = 7;

        [JsonProperty(Order = 2)] public int DaysUntilLockout = 2;

        [JsonProperty(Order = 3)] public bool SendFinalWarning = true;

        [JsonProperty(Order = 4)] public int FreshPurchaseGraceDays = 3;

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

    public class ModRent : ModuleBase<ModRentConfiguration>
    {
        private static readonly SaveSlotStore<RentLocationState> Store = new("Rent", "rent state");

        private static readonly HashSet<string> LockedCodes = new();

        private readonly HashSet<string> _establishedAtLoad = new();
        private bool _establishedCaptured;

        private int _lastElapsedDay = -1;
        private bool _initialised;

        public override void Apply()
        {
            DiscoverLocations();

            if (!Configuration.Enabled)
                return;

            Store.Unload();
            LockedCodes.Clear();
            _establishedAtLoad.Clear();
            _establishedCaptured = false;
            _lastElapsedDay = -1;
            _initialised = false;

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
                    continue;
                }

                bool charged = ApplyDueCharges(state, loc, today, _establishedAtLoad.Contains(code));

                bool messaged = false;

                if (charged)
                {
                    RentMessenger.Send(loc,
                        $"Rent of ${loc.WeeklyRent:N0} for {prop.PropertyName} is due. Drop it at {loc.DeadDropName}. " +
                        $"You have {Configuration.DaysUntilLockout} day(s) before I change the locks.");
                    messaged = true;
                }

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
                        RentMessenger.Send(loc, ReminderMessage(prop, loc, state.Owed));
                    }
                }

                Store.Set(code, state);
            }
        }

        private bool ApplyDueCharges(RentLocationState state, RentLocationConfiguration loc, int today, bool establishedAtLoad)
        {
            if (state.LastChargedDay < 0)
            {
                int interval = Configuration.RentIntervalDays;
                state.LastChargedDay = establishedAtLoad
                    ? today - interval
                    : today + (Configuration.FreshPurchaseGraceDays / interval) * interval;
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

        private void SendLoadReminders()
        {
            if (!Configuration.Enabled)
                return;

            TimeManager time = TimeManager.Instance;
            if (time == null)
                return;
            int today = time.ElapsedDays;

            CaptureEstablishedAtLoad();

            int sent = 0;
            foreach ((Property prop, RentLocationConfiguration loc) in EnabledOwnedLocations())
            {
                string code = prop.PropertyCode;
                RentLocationState state = Store.TryGet(code, out RentLocationState s) ? s : new RentLocationState();

                if (!state.LockedOut)
                    ApplyDueCharges(state, loc, today, _establishedAtLoad.Contains(code));

                Store.Set(code, state);

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
                    return;

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
