using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Lithium.Modules.Customers.Architecture
{
    internal sealed class SaveSlotStore<TValue>
    {
        private readonly string _storageFolder;
        private readonly string _label;

        private Dictionary<string, TValue> _entries = new();
        private string _currentSaveKey;
        private bool _loaded;

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

        public bool Remove(string key)
        {
            EnsureLoaded();
            if (!_entries.Remove(key))
                return false;
            Persist();
            return true;
        }

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
                return;

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
