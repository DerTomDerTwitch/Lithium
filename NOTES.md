# Repo-root / Modules infrastructure — Notes

## Core.cs

The `Core : MelonMod` entry point. Manages the full module lifecycle.

### Module list
`Modules` is a static `List<ModuleBase>` containing one instance of every module class. To disable a module entirely (removing its patches), comment it out of this list. A registered module is still gated at runtime by the `Enabled` flag in its JSON config — the list controls whether the module exists at all; the config flag controls whether its logic runs.

### Two-phase lifecycle

1. **`OnInitializeMelon()`** — runs once at mod load (before any scene). Loads `LithiumConfig`, then calls `module.Load()` on every module (which instantiates config and patches). Then calls `harmony.PatchAll()` to apply all Harmony patches across the assembly.

2. **`OnSceneWasInitialized()`** — calls `module.Apply()` on every module only when `sceneName == "Main"` (i.e. a save is loaded). `Apply()` is for one-time runtime mutations that need game objects/singletons to exist. Sets `_sceneIsMain = true` when entering the main scene and resets it (and `_isFirstStart`) when returning to the Menu.

### `Core.Get<T>()`
`public static T Get<T>() where T : ModuleBase` — retrieves a module instance from `Modules` by type. Used throughout patches to access a module's configuration.

### `_sceneIsMain` flag
True while a save (the "Main" scene) is loaded. A config reload only re-runs `Apply()` when this is set, because `Apply()` touches live game objects/singletons that don't exist at the menu.

### `ReloadConfiguration()` — Ctrl+Shift+F8
Re-reads every config file from disk (global `Lithium.json` and each module's JSON) and, when a save is loaded, re-runs each module's `Apply()` so runtime/prefab mutations pick up the new values. Patches that read their config live update the instant the config object is reloaded. Both `Load()` and `Apply()` calls are individually wrapped in try/catch so one module's failure doesn't abort the rest.

### Debug hotkeys (OnUpdate)
F8 is shared between two user-facing tools (neither gated behind the `Debug` flag):
- **Ctrl+Shift+F8** — `ReloadConfiguration()`: reload and reapply every Lithium config from disk.
- **plain F8** — `RentDebug.Dump()`: dump dead drops/properties for authoring the Rent config.

The remaining hotkeys are dev-only and gated behind `Log.DebugEnabled` (the global `Debug` flag in `Lithium.json`):
- **F5** — expand all player-owned Veeper vehicles' storage to 20 slots.
- **F6** — `OrderPatternDebug.Dump()`: dump customer order patterns.
- **F7** — `NpcRosterDebug.Dump()`: dump the NPC roster.

---

## Log.cs

Central logging for Lithium. All code should call `Log.Info/Warning/Error` — never call `MelonLogger` or `Core.Logger` directly.

- `Log.Info(message)` — emitted only when `LithiumConfig.Instance.Debug` is true. Silenced by default to keep the console clean.
- `Log.Warning(message)` — always shown, regardless of the `Debug` flag.
- `Log.Error(message)` — always shown, regardless of the `Debug` flag.
- `Log.DebugEnabled` — bool property; use this to guard expensive log-string building: `if (Log.DebugEnabled) Log.Info($"... {expensive}")`.

---

## LithiumConfig.cs

Global (non-module) Lithium settings stored at `UserData/Lithium/Lithium.json`. Loaded once at startup (in `OnInitializeMelon`) before any module, so logging during module load already respects the `Debug` flag.

- `Debug` (bool, default `false`) — when true, enables informational log messages. Off by default to keep the console clean.
- `Load()` — if the file exists, merges it onto `Instance` via `JsonConvert.PopulateObject`, then calls `Save()`. The re-save ensures the file is created on first run and gains any keys added in newer mod versions.
- `Save()` — serializes `Instance` to indented JSON and writes to `Lithium.json`. Creates the `Lithium/` folder if absent.

---

## Modules/ModuleBase.cs

Abstract base for all modules. Two-level hierarchy:

- `ModuleBase` — defines the `Load()` and `Apply()` abstract methods that `Core` calls.
- `ModuleBase<TConfiguration>` — generic subclass for modules with typed config. `Load()` creates a fresh `TConfiguration` instance via `Activator.CreateInstance<TConfiguration>()`, calls `OnBeforeConfigurationLoaded()` (hook for the module to mutate defaults before the JSON merge), then `Configuration.LoadConfiguration()` and `Configuration.Validate()`.
- `OnBeforeConfigurationLoaded()` — virtual no-op; override to mutate default config values before the JSON is merged. Example use: `ModPlants` clears its default weighted lists here so the user's JSON fully replaces them rather than appending.

---

## Modules/ModuleConfiguration.cs

Base class for all module config POCOs. Serialized via Newtonsoft.Json to `MelonEnvironment.UserDataDirectory\Lithium\<Name>.json`.

- `Enabled` (`bool`) — serialized with `[JsonProperty(Order = -500)]` so it always appears first in the JSON file.
- `Name` (`abstract string`, `[JsonIgnore]`) — the module name used for the config filename.
- `LoadConfiguration()` — if the file exists, merges JSON onto `this` via `JsonConvert.PopulateObject`, then calls `SaveConfiguration()`. The re-save fills in missing keys (fields added in a newer mod version) with their defaults and drops obsolete keys, while preserving all user-set values. If no file exists, writes the defaults.
- `SaveConfiguration()` — serializes `this` to indented JSON; creates the `Lithium/` folder if absent.
- `Validate()` — virtual no-op; override to sanity-check loaded values and log warnings for nonsensical input (e.g. negative multipliers, a min above its max). Runs after the JSON has been merged onto the object, so it sees the user's actual values. Should clamp/correct in place. Use helpers in `ConfigValidator`.
