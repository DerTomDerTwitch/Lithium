using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Vehicles;
using Il2CppTMPro;
using Lithium.Modules.Customers;
using Lithium.Modules.ElectricBill;
using Lithium.Modules.Police.PropertyContraband;
using Lithium.Modules.Rent;
using Lithium.Modules.Storyline;
using MelonLoader;
using MelonLoader.Preferences;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Lithium
{
    /// <summary>
    /// Integrates Lithium with the in-game "ModManager &amp; PhoneApp" mod. Two parts:
    ///
    ///   1. <b>Settings</b> — a single MelonPreferences category (<c>Lithium_General</c>) holding a
    ///      Debug-logging toggle that mirrors <see cref="LithiumConfig.Debug"/>. The Mod Manager groups
    ///      any category whose Identifier starts with a registered mod's name ("Lithium") under that mod,
    ///      so Lithium shows up in the app/main-menu Mods panel and the toggle renders there. Saving the
    ///      app fires MelonLoader's <see cref="Core.OnPreferencesSaved"/> (no hard dependency on the Mod
    ///      Manager assembly), which we route here to sync the toggle into <c>Lithium.json</c>.
    ///
    ///   2. <b>Action buttons</b> — the former authoring/debug hotkeys, injected as real one-press
    ///      <see cref="Button"/>s. MelonPreferences has no button type, so we Harmony-postfix the Mod
    ///      Manager's private <c>PopulateModSettings(MelonBase)</c> (resolved by name via reflection — a
    ///      soft dependency; if the Mod Manager isn't installed we just skip) and, when the selected mod
    ///      is Lithium, clone its Save button once per tool and wire each clone's onClick to run the tool
    ///      immediately. Pressing the button runs the dump/action once; nothing is persisted as a toggle.
    /// </summary>
    public static class LithiumModMenu
    {
        private const string GeneralId = "Lithium_General";

        private static MelonPreferences_Category _generalCategory;
        private static MelonPreferences_Entry<bool> _debugEntry;

        private sealed class Tool
        {
            public readonly string Name;
            public readonly bool RequiresSave;
            public readonly Action Run;
            public Tool(string name, bool requiresSave, Action run)
            {
                Name = name;
                RequiresSave = requiresSave;
                Run = run;
            }
        }

        private static readonly List<Tool> Tools = new();

        // Roots the onClick delegates so the IL2CPP interop layer can't GC them while the buttons live.
        private static readonly List<Action> RootedHandlers = new();

        /// <summary>Creates the Debug setting and hooks the Mod Manager UI. Called once from Core init.</summary>
        public static void Initialize()
        {
            try
            {
                _generalCategory = MelonPreferences.CreateCategory(GeneralId, "Lithium - General");
                _debugEntry = _generalCategory.CreateEntry<bool>(
                    "Debug", LithiumConfig.Instance.Debug, "Debug logging",
                    "Verbose Log.Info output to the MelonLoader console.",
                    false, false, (ValueValidator)null, null);
                // Lithium.json stays the canonical store the rest of the code reads, so force the value
                // loaded from it over whatever a stale MelonPreferences.cfg may hold.
                _debugEntry.Value = LithiumConfig.Instance.Debug;
                _generalCategory.SaveToFile(false);

                BuildToolTable();
                TryHookModManager();

                Log.Info("[Lithium] Mod Manager menu initialized.");
            }
            catch (Exception e)
            {
                Log.Error($"[Lithium] Mod Manager menu init failed: {e}");
            }
        }

        private static void BuildToolTable()
        {
            Tools.Clear();
            Tools.Add(new Tool("Reload & reapply all configs", false, Core.ReloadConfiguration));
            Tools.Add(new Tool("Dump rent / dead-drop info", true, RentDebug.Dump));
            Tools.Add(new Tool("Dump buildables / appliances", true, BuildablesDebug.Dump));
            Tools.Add(new Tool("Dump RV furniture", true, RVFurnitureDebug.Dump));
            Tools.Add(new Tool("Dump customer order patterns", true, OrderPatternDebug.Dump));
            Tools.Add(new Tool("Dump NPC roster", true, NpcRosterDebug.Dump));
            Tools.Add(new Tool("Dump police contraband scan", true, PoliceContrabandDebug.Dump));
            Tools.Add(new Tool("Expand Veeper storage", true, ExpandVeeperStorage));
            Tools.Add(new Tool("Toggle rent lockout (test)", true, () => Core.Get<ModRent>()?.DebugToggleLockout()));
        }

        /// <summary>
        /// Syncs the Debug toggle into LithiumConfig. Called from <see cref="Core.OnPreferencesSaved"/>
        /// (fired by the Mod Manager's Save → MelonPreferences.Save → MelonLoader).
        /// </summary>
        public static void OnPreferencesSaved()
        {
            if (_debugEntry != null && _debugEntry.Value != LithiumConfig.Instance.Debug)
            {
                LithiumConfig.Instance.Debug = _debugEntry.Value;
                LithiumConfig.Save();
                Log.Warning($"[Lithium] Debug logging {(_debugEntry.Value ? "enabled" : "disabled")} via Mod Manager.");
            }
        }

        // --- Mod Manager UI injection (soft dependency, resolved by reflection) ----------------------

        private static void TryHookModManager()
        {
            try
            {
                Type appType = AccessTools.TypeByName("ModManagerPhoneApp.ModSettingsAppCreator");
                if (appType == null)
                {
                    Log.Info("[Lithium] ModManager app not installed — tool buttons unavailable.");
                    return;
                }

                MethodInfo original = AccessTools.Method(appType, "PopulateModSettings");
                if (original == null)
                {
                    Log.Warning("[Lithium] ModManager.PopulateModSettings not found — tool buttons unavailable.");
                    return;
                }

                MethodInfo postfix = typeof(LithiumModMenu).GetMethod(
                    nameof(InjectToolButtons), BindingFlags.Static | BindingFlags.NonPublic);
                new HarmonyLib.Harmony("com.lithium.modmenu").Patch(original, postfix: new HarmonyMethod(postfix));
                Log.Info("[Lithium] ModManager tool-button hook applied.");
            }
            catch (Exception e)
            {
                Log.Warning($"[Lithium] Could not hook the ModManager UI: {e.Message}");
            }
        }

        /// <summary>
        /// Postfix of ModManager's <c>PopulateModSettings(MelonBase melon)</c>. When the Lithium mod is the
        /// one being shown, appends a "Debug Tools" header and a real button per <see cref="Tools"/> entry.
        /// Runs after the Mod Manager cleared+rebuilt the panel, so these are re-created cleanly each time.
        /// </summary>
        private static void InjectToolButtons(object __instance, MelonBase __0)
        {
            try
            {
                if (__0?.Info?.Name != "Lithium") return;

                GameObject app = Traverse.Create(__instance).Field("modManagerAppInstance").GetValue<GameObject>();
                if (app == null) return;

                Transform content = app.transform.Find("RightPanel/ConfigPanel/Viewport/Content");
                Transform saveButton = app.transform.Find("RightPanel/SaveButton");
                if (content == null || saveButton == null) return;

                RootedHandlers.Clear();

                // Section header, cloned from the Mod Manager's own category template.
                GameObject categoryTemplate = Traverse.Create(__instance).Field("categoryTemplate").GetValue<GameObject>();
                if (categoryTemplate != null)
                {
                    GameObject header = UnityEngine.Object.Instantiate(categoryTemplate, content);
                    header.name = "Lithium_ToolsHeader";
                    Transform title = header.transform.Find("Title");
                    if (title != null)
                    {
                        TMP_Text titleText = title.GetComponent<TMP_Text>();
                        if (titleText != null) titleText.SetText("Debug Tools (press to run)");
                    }
                    header.SetActive(true);
                }

                foreach (Tool tool in Tools)
                {
                    GameObject buttonGo = UnityEngine.Object.Instantiate(saveButton.gameObject, content);
                    buttonGo.name = "Lithium_Tool_" + tool.Name;
                    buttonGo.transform.localScale = Vector3.one;

                    Button button = buttonGo.GetComponent<Button>();
                    if (button == null) { UnityEngine.Object.Destroy(buttonGo); continue; }
                    ((UnityEventBase)button.onClick).RemoveAllListeners();

                    TMP_Text label = buttonGo.GetComponentInChildren<TMP_Text>(true);
                    if (label != null)
                    {
                        label.SetText(tool.Name);
                        label.enableAutoSizing = true;
                        label.fontSizeMin = 6f;
                    }

                    // Size it like a list row (the Save button isn't a layout element by default).
                    LayoutElement layout = buttonGo.GetComponent<LayoutElement>();
                    if (layout == null) layout = buttonGo.AddComponent<LayoutElement>();
                    layout.minHeight = 48f;
                    layout.preferredHeight = 48f;
                    layout.flexibleWidth = 1f;

                    Tool captured = tool;
                    Action handler = () => RunTool(captured);
                    RootedHandlers.Add(handler);
                    ((UnityEvent)button.onClick).AddListener((UnityAction)handler);

                    buttonGo.SetActive(true);
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Lithium] Injecting tool buttons failed: {e}");
            }
        }

        private static void RunTool(Tool tool)
        {
            if (tool.RequiresSave && !Core.IsInMainScene)
            {
                Log.Warning($"[Lithium] '{tool.Name}' needs a loaded save — load a game first.");
                return;
            }

            try
            {
                Log.Warning($"[Lithium] Running '{tool.Name}' from Mod Manager.");
                tool.Run();
            }
            catch (Exception e)
            {
                Log.Error($"[Lithium] '{tool.Name}' failed: {e}");
            }
        }

        private static void ExpandVeeperStorage()
        {
            LandVehicle[] veepers = VehicleManager.Instance.AllVehicles.ToArray()
                .Where(v => v.IsPlayerOwned)
                .Where(v => v.VehicleCode == "veeper")
                .ToArray();
            foreach (LandVehicle vehicle in veepers)
            {
                vehicle.Storage.SlotCount = 20;
                for (int i = vehicle.Storage.ItemSlots.Count; i <= vehicle.Storage.SlotCount; i++)
                {
                    vehicle.Storage.ItemSlots.Add(new());
                }
            }
        }
    }
}
