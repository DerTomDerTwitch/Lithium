using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Lithium.Modules.Customers.Architecture
{
    /// <summary>
    /// A per-savegame <c>Dictionary&lt;string, TValue&gt;</c> persisted to
    /// <c>UserData/Lithium/&lt;folder&gt;/&lt;save&gt;.json</c>. The save key is resolved lazily via
    /// <see cref="SaveSlotKey"/> (the loaded-save folder isn't reliably available the instant a save
    /// loads, but always is by the time the state is first read or written), so the first access after
    /// a load reads that save's file and unrelated saves keep their own state.
    ///
    /// Backs <see cref="ContractRetryTracker"/> and <see cref="OfferDeadlineTracker"/>, which were
    /// previously two near-identical hand-rolled copies of this load/persist machinery.
    /// </summary>
    internal sealed class SaveSlotStore<TValue>
    {
        private readonly string _storageFolder;
        private readonly string _label; // used in log messages, e.g. "contract retries"

        private Dictionary<string, TValue> _entries = new();
        private string _currentSaveKey;
        private bool _loaded;

        /// <param name="folderName">Sub-folder under <c>UserData/Lithium/</c> (e.g. "ContractRetries").</param>
        /// <param name="label">Human-readable name for log messages (e.g. "contract retries").</param>
        public SaveSlotStore(string folderName, string label)
        {
            _storageFolder = Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium", folderName);
            _label = label;
        }

        public bool TryGet(string key, out TValue value)
        {
            EnsureLoaded();
            return _entries.TryGetValue(key, out value);
        }

        public void Set(string key, TValue value)
        {
            EnsureLoaded();
            _entries[key] = value;
            Persist();
        }

        /// <summary>Removes the entry and persists; returns false (no write) if the key was absent.</summary>
        public bool Remove(string key)
        {
            EnsureLoaded();
            if (!_entries.Remove(key))
                return false;
            Persist();
            return true;
        }

        /// <summary>
        /// Called when a save loads: drop the in-memory state and force the next access to re-resolve the
        /// save and reload its file. Does not touch disk, so unrelated saves keep their stored state.
        /// </summary>
        public void Unload()
        {
            _entries = new();
            _currentSaveKey = null;
            _loaded = false;
        }

        private void EnsureLoaded()
        {
            if (_loaded)
                return;

            string key = SaveSlotKey.Resolve();
            if (key == null)
                return; // save folder not known yet — try again on the next access.

            _currentSaveKey = key;
            _entries = ReadFromDisk(key) ?? new();
            _loaded = true;
        }

        private void Persist()
        {
            if (_currentSaveKey == null)
                _currentSaveKey = SaveSlotKey.Resolve();
            if (_currentSaveKey == null)
                return;

            try
            {
                Directory.CreateDirectory(_storageFolder);
                File.WriteAllText(FilePath(_currentSaveKey), JsonConvert.SerializeObject(_entries, Formatting.Indented));
            }
            catch (Exception e)
            {
                Log.Warning($"[Lithium] Failed to save {_label}: {e.Message}");
            }
        }

        private Dictionary<string, TValue> ReadFromDisk(string key)
        {
            string file = FilePath(key);
            if (!File.Exists(file))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, TValue>>(File.ReadAllText(file));
            }
            catch (Exception e)
            {
                Log.Warning($"[Lithium] Failed to load {_label}: {e.Message}");
                return null;
            }
        }

        private string FilePath(string key) => Path.Combine(_storageFolder, $"{key}.json");
    }
}
