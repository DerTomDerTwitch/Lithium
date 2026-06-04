using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Lithium
{
    public class LithiumConfig
    {
        public static LithiumConfig Instance { get; private set; } = new LithiumConfig();

        public bool Debug = false;

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
