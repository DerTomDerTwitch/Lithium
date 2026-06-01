using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Persistence;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Lithium.Modules.Customers.Architecture
{
    /// <summary>
    /// Remembers customers whose contract offer the player refused, or that expired unanswered, so they
    /// re-attempt an order the next day instead of waiting for their next scheduled order day.
    ///
    /// State is persisted to <c>UserData/Lithium/ContractRetries/&lt;save&gt;.json</c> so it survives
    /// quitting and reloading. The file is keyed per savegame slot — the slot folder name (e.g.
    /// "SaveGame_1") combined with the player's organisation name (e.g. "SaveGame_1 - Greenacre") — so
    /// each save keeps its own outstanding retries. The slot folder guarantees uniqueness even when two
    /// saves share an organisation name; the name is appended only to make the files human-readable.
    /// </summary>
    public static class ContractRetryTracker
    {
        private static readonly string StorageFolder =
            Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium", "ContractRetries");

        // customerName -> the weekday the customer should re-attempt on (the day after the refusal/expiry).
        // A weekday (rather than an absolute date) is stored because the order schedule the game consults,
        // GetOrderDays, is itself weekday-based; the day is captured once at refusal time so it stays put.
        private static Dictionary<string, EDay> Pending = new();

        // The save the in-memory state belongs to, and whether we've loaded that save's file yet. Both are
        // resolved lazily: the loaded-save folder path isn't reliably available the instant a save loads,
        // but it always is by the time contracts are generated or refused.
        private static string CurrentSaveKey;
        private static bool Loaded;

        public static void FlagForRetry(string customerName)
        {
            if (string.IsNullOrEmpty(customerName))
                return;

            EnsureLoaded();
            int next = ((int)TimeManager.Instance.CurrentDay + 1) % 7;
            Pending[customerName] = (EDay)next;
            Persist();
        }

        /// <summary>True if this customer owes a retry and today is the day to make it.</summary>
        public static bool IsRetryDay(string customerName)
        {
            EnsureLoaded();
            return Pending.TryGetValue(customerName, out EDay day) && day == TimeManager.Instance.CurrentDay;
        }

        public static bool HasPendingRetry(string customerName, out EDay retryDay)
        {
            EnsureLoaded();
            return Pending.TryGetValue(customerName, out retryDay);
        }

        public static void Clear(string customerName)
        {
            EnsureLoaded();
            if (Pending.Remove(customerName))
                Persist();
        }

        /// <summary>
        /// Called when a save loads: drop the in-memory state and force the next access to re-resolve the
        /// save and reload its file. Does not touch disk, so unrelated saves keep their stored retries.
        /// </summary>
        public static void Unload()
        {
            Pending = new();
            CurrentSaveKey = null;
            Loaded = false;
        }

        private static void EnsureLoaded()
        {
            if (Loaded)
                return;

            string key = ResolveSaveKey();
            if (key == null)
                return; // save folder not known yet — try again on the next access.

            CurrentSaveKey = key;
            Pending = ReadFromDisk(key) ?? new();
            Loaded = true;
        }

        private static void Persist()
        {
            if (CurrentSaveKey == null)
                CurrentSaveKey = ResolveSaveKey();
            if (CurrentSaveKey == null)
                return;

            try
            {
                Directory.CreateDirectory(StorageFolder);
                File.WriteAllText(FilePath(CurrentSaveKey), JsonConvert.SerializeObject(Pending, Formatting.Indented));
            }
            catch (Exception e)
            {
                Log.Warning($"[Lithium] Failed to save contract retries: {e.Message}");
            }
        }

        private static Dictionary<string, EDay> ReadFromDisk(string key)
        {
            string file = FilePath(key);
            if (!File.Exists(file))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, EDay>>(File.ReadAllText(file));
            }
            catch (Exception e)
            {
                Log.Warning($"[Lithium] Failed to load contract retries: {e.Message}");
                return null;
            }
        }

        private static string FilePath(string key) => Path.Combine(StorageFolder, $"{key}.json");

        // A filename-safe, human-readable id for the loaded save slot: the slot folder name (e.g.
        // "SaveGame_1") plus the organisation name (e.g. "SaveGame_1 - Greenacre"). The folder name alone
        // already uniquely identifies the slot; the organisation name is appended only for readability.
        // Returns null until both are available, so the key never changes mid-session (the existing lazy
        // callers simply retry on the next access — both are populated by contract-generation time).
        private static string ResolveSaveKey()
        {
            try
            {
                LoadManager loadManager = LoadManager.Instance;
                string path = loadManager?.LoadedGameFolderPath;
                if (string.IsNullOrEmpty(path))
                    return null; // save folder not known yet — try again on the next access.

                string slot = new DirectoryInfo(path).Name; // unique per slot, e.g. "SaveGame_1"
                if (string.IsNullOrEmpty(slot))
                    return null;

                string organisation = loadManager.ActiveSaveInfo?.OrganisationName;
                if (string.IsNullOrWhiteSpace(organisation))
                    return null; // save info not populated yet — retry so the key stays stable.

                return Sanitize($"{slot} - {organisation}");
            }
            catch
            {
                return null;
            }
        }

        // Replaces characters that are illegal in file names so the readable key is safe to use as a
        // filename (organisation names are player-entered and may contain e.g. ':' or '/').
        private static string Sanitize(string raw)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                raw = raw.Replace(invalid, '_');
            return raw;
        }
    }
}
