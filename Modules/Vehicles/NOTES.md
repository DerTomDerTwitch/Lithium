# Vehicles Module Notes

## ModVehicles.cs

Module class, per-vehicle configuration, and the Apply() override for vehicle prefab mutation.

**`VehicleConfiguration` fields (per vehicle entry):**
- `OverrideSlotCount` (bool) / `SlotCount` (int): When true, sets `vehiclePrefab.Storage.SlotCount` to the given value. Controls how many inventory slots the vehicle's storage has.
- `OverridePrice` (bool) / `Price` (int): When true, sets `vehiclePrefab.vehiclePrice` to the given value. Overrides the in-game purchase price.
- `OverrideDefaultColor` (bool) / `Color` (`EVehicleColor`, default `White`): Config field present but **not yet applied in `Apply()`** — the color override is declared in the config struct but the corresponding mutation is missing from the loop body. This is dead config as of the current code.

**`ModVehiclesConfiguration.Overrides`:** A `Dictionary<string, VehicleConfiguration>` keyed by vehicle internal name (lowercase). The default keys cover all seven base vehicles: `"shitbox"`, `"bruiser"`, `"hounddog"`, `"dinkler"`, `"cheetah"`, `"veeper"`, `"hotbox"`. Each entry is initialized with `new()` (all overrides disabled), so the JSON will contain a stub for every vehicle out of the box.

**`Apply()` behaviour:** Runs at scene load (Main scene). Iterates `Overrides`, resolves each vehicle's `LandVehicle` prefab via `NetworkSingleton<VehicleManager>.Instance.GetVehiclePrefab(key)`, then conditionally writes `vehiclePrice` and `Storage.SlotCount`. Mutations target the shared prefab, so they affect every subsequent spawn of that vehicle type.

**Gotcha — no Patches/ subfolder:** All logic is applied once in `Apply()`; no Harmony patches are needed because prefab field writes persist for the session.

**Gotcha — color field unimplemented:** `OverrideDefaultColor` / `Color` appear in `VehicleConfiguration` and will be serialized to JSON, but the Apply loop contains no branch for them. Users setting this field will see no effect until the code is completed.
