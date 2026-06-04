using Il2CppScheduleOne.Persistence;

namespace Lithium.Modules.Customers.Architecture
{
    internal static class SaveSlotKey
    {
        public static string Resolve()
        {
            try
            {
                LoadManager loadManager = LoadManager.Instance;
                string path = loadManager?.LoadedGameFolderPath;
                if (string.IsNullOrEmpty(path))
                    return null;

                string slot = new DirectoryInfo(path).Name;
                if (string.IsNullOrEmpty(slot))
                    return null;

                string organisation = loadManager.ActiveSaveInfo?.OrganisationName;
                if (string.IsNullOrWhiteSpace(organisation))
                    return null;

                return Sanitize($"{slot} - {organisation}");
            }
            catch (Exception e)
            {
                Log.Warning($"[Lithium] Failed to resolve save slot key: {e.Message}");
                return null;
            }
        }

        private static string Sanitize(string raw)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                raw = raw.Replace(invalid, '_');
            return raw;
        }
    }
}
