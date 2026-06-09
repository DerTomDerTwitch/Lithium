using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Lithium
{
    public class LithiumConfig
    {
        public static LithiumConfig Instance { get; private set; } = new LithiumConfig();

        public bool Debug = false;

        // Per-hotkey gates. Each authoring/debug hotkey in Core.OnUpdate only fires when its flag is on.
        // The config-reload hotkey defaults on (broadly useful for live config editing); the rest are
        // opt-in authoring/dump tools that default off.
        public bool HotkeyCtrlShiftF8ReloadConfig = true; // Ctrl+Shift+F8 — reload & reapply all configs
        public bool HotkeyF8RentDump = false;             // F8 — RentDebug.Dump
        public bool HotkeyF9BuildablesDump = false;       // F9 — BuildablesDebug.Dump
        public bool HotkeyF10RVFurnitureDump = false;     // F10 — RVFurnitureDebug.Dump
        public bool HotkeyF5ExpandVeeperStorage = false;  // F5 — expand Veeper storage
        public bool HotkeyF6OrderPatternDump = false;     // F6 — OrderPatternDebug.Dump
        public bool HotkeyF7NpcRosterDump = false;        // F7 — NpcRosterDebug.Dump
        public bool HotkeyF11PoliceScanDump = false;      // F11 — PoliceContrabandDebug.Dump (opt-in)

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
