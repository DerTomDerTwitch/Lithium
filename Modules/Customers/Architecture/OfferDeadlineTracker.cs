using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Lithium.Modules.Customers.Architecture
{
    /// <summary>
    /// Remembers, per customer, the absolute in-game minute (GameDateTime.GetMinSum) at which a pending
    /// contract offer is actually allowed to expire — i.e. the deadline the customer was told about.
    ///
    /// The native offer-expiry check is invisible in the IL2CPP proxy assemblies, so we don't rely on it
    /// honouring our extended window. Instead <see cref="Lithium.Modules.Customers.Patches.CustomerOfferDeadlinePatch"/>
    /// records the deadline when the offer is made and the ExpireOffer guard keeps the deal alive until it
    /// passes, guaranteeing the cancellation never beats the promised time.
    ///
    /// State is persisted to <c>UserData/Lithium/OfferDeadlines/&lt;save&gt;.json</c> (keyed per savegame
    /// slot, see <see cref="SaveSlotKey"/>) so the extended window survives quitting and reloading: the
    /// stored deadline is an absolute in-game minute, which keeps the same meaning across reloads of that
    /// save. Without this, a save load would drop the deadline and the restored offer would fall back to
    /// the game's (shorter) native expiry — exactly the "promised Friday, cancelled the same day" bug.
    /// </summary>
    internal static class OfferDeadlineTracker
    {
        private static readonly string StorageFolder =
            Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium", "OfferDeadlines");

        // customerName -> absolute in-game minute at which the offer may expire.
        private static Dictionary<string, int> Deadlines = new();

        // The save the in-memory state belongs to, and whether we've loaded that save's file yet. Both are
        // resolved lazily (the loaded-save folder isn't reliably available the instant a save loads, but it
        // always is by the time an offer is made or its expiry is checked). Mirrors ContractRetryTracker.
        private static string CurrentSaveKey;
        private static bool Loaded;

        public static void Set(string customerName, int deadlineMinSum)
        {
            if (string.IsNullOrEmpty(customerName))
                return;

            EnsureLoaded();
            Deadlines[customerName] = deadlineMinSum;
            Persist();
        }

        public static bool TryGet(string customerName, out int deadlineMinSum)
        {
            deadlineMinSum = 0;
            if (string.IsNullOrEmpty(customerName))
                return false;

            EnsureLoaded();
            return Deadlines.TryGetValue(customerName, out deadlineMinSum);
        }

        public static void Clear(string customerName)
        {
            if (string.IsNullOrEmpty(customerName))
                return;

            EnsureLoaded();
            if (Deadlines.Remove(customerName))
                Persist();
        }

        /// <summary>
        /// Called when a save loads: drop the in-memory state and force the next access to re-resolve the
        /// save and reload its file. Does not touch disk, so unrelated saves keep their stored deadlines.
        /// </summary>
        public static void Unload()
        {
            Deadlines = new();
            CurrentSaveKey = null;
            Loaded = false;
        }

        private static void EnsureLoaded()
        {
            if (Loaded)
                return;

            string key = SaveSlotKey.Resolve();
            if (key == null)
                return; // save folder not known yet — try again on the next access.

            CurrentSaveKey = key;
            Deadlines = ReadFromDisk(key) ?? new();
            Loaded = true;
        }

        private static void Persist()
        {
            if (CurrentSaveKey == null)
                CurrentSaveKey = SaveSlotKey.Resolve();
            if (CurrentSaveKey == null)
                return;

            try
            {
                Directory.CreateDirectory(StorageFolder);
                File.WriteAllText(FilePath(CurrentSaveKey), JsonConvert.SerializeObject(Deadlines, Formatting.Indented));
            }
            catch (Exception e)
            {
                Log.Warning($"[Lithium] Failed to save offer deadlines: {e.Message}");
            }
        }

        private static Dictionary<string, int> ReadFromDisk(string key)
        {
            string file = FilePath(key);
            if (!File.Exists(file))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(file));
            }
            catch (Exception e)
            {
                Log.Warning($"[Lithium] Failed to load offer deadlines: {e.Message}");
                return null;
            }
        }

        private static string FilePath(string key) => Path.Combine(StorageFolder, $"{key}.json");
    }
}
