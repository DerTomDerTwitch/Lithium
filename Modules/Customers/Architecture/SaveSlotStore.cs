using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Lithium.Modules.Customers.Architecture
{
    // Implemented by every per-save store so a single game-save hook can flush them all at once.
    internal interface IFlushableStore
    {
        void Flush();
    }

    // Process-wide registry of every SaveSlotStore instance. The save-flush patch (SaveFlushPatch, on
    // SaveManager.Save) calls FlushAll() when the game actually writes a save, so the in-memory "floating"
    // values are committed to disk only then — never on every mutation.
    internal static class SaveSlotStores
    {
        private static readonly List<IFlushableStore> Stores = new();

        public static void Register(IFlushableStore store)
        {
            if (store != null && !Stores.Contains(store))
                Stores.Add(store);
        }

        // Commit every store's pending in-memory changes to disk. Called only when the game saves.
        public static void FlushAll()
        {
            foreach (IFlushableStore store in Stores)
            {
                try
                {
                    store.Flush();
                }
                catch (Exception e)
                {
                    Log.Warning($"[Lithium] Store flush failed: {e.Message}");
                }
            }
        }
    }

    // Per-save key/value store backing the modules' runtime state (rent, electric bill, dealers, etc.).
    //
    // Persistence model — "floating" in-memory values, disk only on game save:
    //   * Set/Remove mutate the in-memory dictionary and mark it dirty; they do NOT touch disk.
    //   * TryGet always returns the live in-memory value.
    //   * The file on disk is rewritten ONLY when the game itself saves (manual save or sleeping), via
    //     SaveSlotStores.FlushAll() from the SaveManager.Save hook.
    // This mirrors the game's own save contract: if the player returns to menu or quits WITHOUT saving,
    // the runtime changes are discarded (the file still holds the last *saved* state). Writing on every
    // mutation (the old behaviour) leaked unsaved runtime state to disk, so a no-save quit could not revert.
    internal sealed class SaveSlotStore<TValue> : IFlushableStore
    {
        private readonly string _storageFolder;
        private readonly string _label;
        private readonly string _fileDiscriminator;

        private Dictionary<string, TValue> _entries = new();
        private string _currentSaveKey;
        private bool _loaded;
        private bool _dirty;

        // folderName groups a feature's files; fileDiscriminator lets two stores share that folder without
        // clobbering each other's per-save file. By default the file is "{saveKey}.json"; with a discriminator
        // it is "{saveKey} - {discriminator}.json". Required whenever a feature keeps more than one store in the
        // same folder (e.g. ChemistOrders' order store vs. its history store), since both would otherwise write
        // the same path and the later flush would overwrite the earlier one.
        public SaveSlotStore(string folderName, string label, string fileDiscriminator = null)
        {
            _storageFolder = Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium", folderName);
            _label = label;
            _fileDiscriminator = fileDiscriminator;
            SaveSlotStores.Register(this);
        }

        public bool TryGet(string key, out TValue value)
        {
            EnsureLoaded();
            return _entries.TryGetValue(key, out value);
        }

        // Updates the in-memory ("floating") value. Persisted to disk only on the next game save (Flush).
        public void Set(string key, TValue value)
        {
            EnsureLoaded();
            _entries[key] = value;
            _dirty = true;
        }

        // Removes the in-memory ("floating") value. Persisted to disk only on the next game save (Flush).
        public bool Remove(string key)
        {
            EnsureLoaded();
            if (!_entries.Remove(key))
                return false;
            _dirty = true;
            return true;
        }

        // Discards in-memory state (including any unsaved changes) so the next access reloads from disk.
        // Called when a save is (re)loaded. Intentionally does NOT flush: unsaved runtime changes are meant
        // to be dropped on a fresh load.
        public void Unload()
        {
            _entries = new();
            _currentSaveKey = null;
            _loaded = false;
            _dirty = false;
        }

        // Commits pending in-memory changes to disk. Invoked only by SaveSlotStores.FlushAll() when the game
        // saves. No-op when nothing changed since the last flush, or when no save is currently loaded.
        public void Flush()
        {
            if (!_dirty || !_loaded)
                return;
            Persist();
            _dirty = false;
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

        private string FilePath(string key) => Path.Combine(_storageFolder,
            string.IsNullOrEmpty(_fileDiscriminator) ? $"{key}.json" : $"{key} - {_fileDiscriminator}.json");
    }
}
