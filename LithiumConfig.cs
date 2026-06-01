using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Lithium
{
    /// <summary>
    /// Global (non-module) Lithium settings, stored at <c>UserData/Lithium/Lithium.json</c>. Loaded once
    /// at startup, before any module, so logging during module load already respects it.
    /// </summary>
    public class LithiumConfig
    {
        public static LithiumConfig Instance { get; private set; } = new LithiumConfig();

        // When true, Lithium writes its informational logs to the MelonLoader console. Off by default to
        // keep the console clean. Warnings and errors are always shown regardless of this setting.
        public bool Debug = false;

        private static string ConfigFolder => Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium");
        private static string FilePath => Path.Combine(ConfigFolder, "Lithium.json");

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    JsonConvert.PopulateObject(File.ReadAllText(FilePath), Instance);

                // Re-save so the file is created on first run and gains any keys added in newer versions.
                Save();
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[Lithium] Failed to load Lithium.json: {e.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(Instance, Formatting.Indented));
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[Lithium] Failed to save Lithium.json: {e.Message}");
            }
        }
    }
}
