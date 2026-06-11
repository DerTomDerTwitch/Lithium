using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Lithium
{
    public class LithiumConfig
    {
        public static LithiumConfig Instance { get; private set; } = new LithiumConfig();

        public bool Debug = false;

        // Authoring/debug actions and the debug-logging toggle now live in the in-game Mod Manager app
        // (see LithiumModMenu). The old per-hotkey gate fields were removed; legacy keys in an existing
        // Lithium.json are simply ignored by JsonConvert and dropped on the next Save.

        private static string ConfigFolder => Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium");
        private static string FilePath => Path.Combine(ConfigFolder, "Lithium.json");

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    JsonConvert.PopulateObject(File.ReadAllText(FilePath), Instance);

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
