using Il2CppScheduleOne.Persistence;

namespace Lithium.Modules.Customers.Architecture
{
    /// <summary>
    /// Resolves a filename-safe, human-readable id for the currently loaded save slot, used to key
    /// per-save persistence files under <c>UserData/Lithium/</c>. The slot folder name (e.g.
    /// "SaveGame_1") already uniquely identifies the slot; the organisation name (e.g. "Greenacre") is
    /// appended only for readability. Returns null until both are available, so the key never changes
    /// mid-session — callers retry on the next access (both are populated by contract-generation time).
    /// </summary>
    internal static class SaveSlotKey
    {
        public static string Resolve()
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
