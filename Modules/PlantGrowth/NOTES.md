# PlantGrowth Module — Notes

Extracted from source comments. One section per file.

---

## ModPlants.cs

### Overview
Main module file. Contains `ModPlantsConfiguration` and `ModPlants : ModuleBase<ModPlantsConfiguration>`.

### Configuration fields
- `GrowthModifier` (default `0.7f`) — multiplied onto the pot's base `GrowSpeedMultiplier` each `OnMinPass`.
- `WaterDrainModifier` (default `4f`) — multiplied onto the pot's base `_moistureDrainPerHour` each `OnMinPass`.

### WeightedFloat / picker lists
Three JSON-serialised lists of `{ Weight, Value }` pairs drive the harvest randomisation:

1. **`RandomYieldsPerBudModifier`** — discrete weighted pick (`WeightedPicker<float>`). `Value` is product count per bud, `Weight` is relative chance. Defaults: 80% → 1, 17% → 2, 3% → 3.
2. **`RandomYieldModifiers`** — overall plant yield multiplier, rolled once when the plant finishes growing (`WeightedNormalizer`, interpolated). Defaults: ~50% stays at 1.0, sometimes 0.75/1.25, rarely 0.5/2.0. The doubled `1.0` entry creates the wide "default amount" plateau.
3. **`RandomQualityModifiers`** — quality offset added to the plant's quality level (`WeightedNormalizer`, interpolated). Quality bands are wide (Standard spans 0.40–0.75) so offsets are modest. Most harvests land near the plant's own quality, sometimes ±0.15 (one tier off), rarely ±0.3 (two tiers off). Stacks on top of any fertilizer/PGF `QualityChange` already baked into the plant's quality.

### Default factory methods
`DefaultResultsPerBud()`, `DefaultYieldModifiers()`, `DefaultQualityModifiers()` are static factory methods (not shared static lists). This is intentional: `OnBeforeConfigurationLoaded` clears the three instance lists before the JSON merge, so clearing the instance list can never corrupt the defaults themselves.

### OnBeforeConfigurationLoaded
Clears all three weighted lists before the JSON is merged, so user JSON fully replaces the defaults rather than appending to them.

### Load() — backfill logic
After loading, if any list is null, empty, or has zero total weight, it is replaced with the factory default and the config is re-saved. This ensures the written JSON always shows working defaults, and protects against a user emptying a list entirely.

Picker construction:
- `RandomYieldPerBudPicker` — `WeightedPicker<float>`, added as `(Value, Weight)` pairs.
- `RandomYieldModifierPicker` and `RandomYieldQualityPicker` — `WeightedNormalizer`, added as `(Weight, Value)`.

### Validate()
`GrowthModifier` is clamped to at least `0.001f` (a zero/negative value would stall or break growth; the patch also floors it). `WaterDrainModifier` is clamped to at least `0f` (negative is nonsensical). Warn-and-clamp rather than silently halt every plant.

### IL2CPP registration (constructor)
Three MonoBehaviours are registered in the `ModPlants` constructor:
- `ClassInjector.RegisterTypeInIl2Cpp<PlantModified>()`
- `ClassInjector.RegisterTypeInIl2Cpp<PlantBaseQuality>()`
- `ClassInjector.RegisterTypeInIl2Cpp<PotBaseValues>()`
Registration must happen before any scene load that might attach these components.

---

## HarvestQuality.cs

### Overview
Static utility class providing `ComputeBaseQuality(Plant plant)`. Shared by both harvest paths (player hand-harvest via `PlantHarvestablePatch` and botanist whole-plant via `PlantGetHarvestedProductPatch`) so both always produce quality from identical logic.

### Why not read `plant.QualityLevel` directly?
The plant's stored `QualityLevel` is unreliable: older builds could leak per-bud quality bumps into it, and it can drift out of range. Instead, a clean base is rebuilt every harvest: `Plant.BaseQualityLevel` (vanilla Standard base = `0.5`) plus the `QualityChange` of every additive applied to the pot. Additives that don't affect quality (e.g. pure speed-grow) contribute `0`. Result is `Mathf.Clamp01`'d.

---

## Behaviours/PlantModified.cs

### Overview
Marker `MonoBehaviour` attached to a `Plant` once its harvest yield has been rolled by `PlantGrowthDonePatch`. Carries no state — its presence is the signal that prevents `Plant.GrowthDone` from rolling the yield a second time (e.g. if the event fires more than once).

---

## Behaviours/PlantBaseQuality.cs

### Overview
`MonoBehaviour` attached to a `Plant` during a player hand-harvest. Carries two fields:
- `Quality` — the clean computed base quality to restore after each bud is harvested.
- `NeedsNotification` — flag: only the first bud shows the harvest quality notification.

No comments beyond what the field names convey.

---

## Behaviours/PotBaseValues.cs

### Overview
`MonoBehaviour` attached to a `Pot` in `PotStartPatch.Postfix`. Captures the pot's original (unmodified) values at `Start` so `PotMinPassPatch` can multiply from the true base each tick instead of compounding the modifier.

### Fields
- `BaseWaterDrainPerHour` — captured from `pot._moistureDrainPerHour`.
- `BaseGrowSpeedMultiplier` — captured from `pot.GrowSpeedMultiplier`.

### Gotcha: API rename
The water system was reworked: `Pot.WaterDrainPerHour` is now `GrowContainer._moistureDrainPerHour` (`Pot` derives from `GrowContainer`). The field used is `_moistureDrainPerHour`.

---

## Patches/PotStartPatch.cs

Patches `Pot.Start` (postfix). Attaches a `PotBaseValues` component and calls `Init` on it to capture baseline values. No significant comments beyond the standard enabled-guard.

---

## Patches/PotMinPassPatch.cs

Patches `Pot.OnMinPass` (prefix). Applies `GrowthModifier` and `WaterDrainModifier` to the pot immediately before the game's per-minute tick.

### Why not patch the getter?
`Pot.GrowSpeedMultiplier` is a field-backed accessor that Il2CppInterop cannot hook (callers read the field directly, so a getter postfix never fires). `OnMinPass` reads that field when it advances growth. The fix: write the modified value onto the field right before `OnMinPass` runs, using the base captured in `PotBaseValues` so the modifier never compounds across ticks. Water drain (`_moistureDrainPerHour`) is handled the same way.

### Magic number
`GrowSpeedMultiplier` is floored at `0.001f` (mirrors the `Validate()` clamp; prevents a zero or negative growth speed).

---

## Patches/PlantGrowthDonePatch.cs

Patches `Plant.GrowthDone` (prefix). Multiplies the plant's `YieldMultiplier` by a value sampled from `RandomYieldModifierPicker` (`WeightedNormalizer`).

### Multiplayer guard
Plant growth is server-authoritative (Pot syncs progress via RPC). The yield roll uses `UnityEngine.Random`, so it must only happen on the server — otherwise each peer would roll a different `YieldMultiplier` and desync the harvested amount. Guard: `if (!InstanceFinder.IsServer) return`.

### Re-roll prevention
`PlantModified` marker component is checked/added here. If already present the prefix returns early. If absent it is added and then the roll is applied. This prevents `GrowthDone` from re-rolling the yield if the event fires more than once on the same plant.

---

## Patches/PlantHarvestablePatch.cs

Patches `PlantHarvestable.Harvest` (prefix + postfix). Handles the **player hand-harvest** path.

### PlayerHarvestInProgress flag
`internal static bool PlayerHarvestInProgress` is set `true` in the prefix and cleared in the postfix. `PlantGetHarvestedProductPatch` reads this flag to avoid double-applying a quality roll if `GetHarvestedProduct` is reached from the player path.

### SkipFlags / GenerateFlags dictionaries
- `GenerateFlags` — keyed by `PlantHarvestable` instance; set when the quality offset and per-bud count have been rolled for this instance. Prevents re-rolling if `Prefix` is somehow called twice.
- `SkipFlags` — set when the player's inventory cannot fit the item; the postfix skips cleanup for that bud.

### Quality calculation (prefix)
Uses `HarvestQuality.ComputeBaseQuality` (clean base, not `QualityLevel`). Adds a random offset from `RandomYieldQualityPicker`. Also sets `ProductQuantity` via `RandomYieldPerBudPicker`. `PlantBaseQuality` component is attached to carry `Quality` (base to restore) and `NeedsNotification`.

`PlantBaseQuality.Quality` restoration also heals any plant whose stored `QualityLevel` was corrupted by earlier builds.

### Per-bud quality restore (postfix) — critical gotcha
`componentInParent.QualityLevel = comp.Quality` and `Object.Destroy(comp)` happen after **every** bud, not just the first (notification) bud. A whole-plant harvest fires `Harvest` once per bud in the same frame, and `Object.Destroy` is deferred to end-of-frame, so every bud after the first reuses the same `PlantBaseQuality` component. Gating the restore on `NeedsNotification` (true only for the first bud) left the per-bud quality offset stacking across buds, so quality random-walked into Trash/Heavenly. Restoring unconditionally keeps each bud's roll independent around the true base quality.

### Notification
One in-game notification is sent per plant (gated on `comp.NeedsNotification`), showing `{count}x {plant name}` and `{quality} quality`.

---

## Patches/PlantGetHarvestedProductPatch.cs

Handles the **botanist whole-plant harvest** path. Botanists do not go through `PlantHarvestable.Harvest` (that path checks the local `PlayerInventory` and is player-only). They harvest via `HarvestPotBehaviour`, which calls `Plant.GetHarvestedProduct(quantity)`. That method reads `QualityLevel` to set the item's quality, so the patch bumps `QualityLevel` around the call and restores it afterwards (the created item retains the rolled quality). Quantity is handled separately in `HarvestPotBehaviourYieldPatch`.

### Virtual dispatch gotcha
`GetHarvestedProduct` is virtual and the concrete plant subclasses (`WeedPlant`, `CocaPlant`) **override** it. Harmony patches a specific `MethodInfo`, so a patch on `Plant.GetHarvestedProduct` (base) never fires for those subclasses — virtual dispatch goes straight to the override. The patch therefore targets the concrete overrides only (via `TargetMethods()` yielding `WeedPlant` and `CocaPlant` overrides). The base is not patched so a subclass calling `base.GetHarvestedProduct` would not double-apply the bump.

### Multiplayer guard
Quality roll is server-authoritative; result is networked with the item. Guard: `if (!InstanceFinder.IsServer) return`.

### `__state` sentinel
`__state = float.NaN` is the "no change made / nothing to restore" sentinel used in the postfix. The postfix only restores `QualityLevel` if `!float.IsNaN(__state)`.

### PlayerHarvestInProgress guard
If `PlantHarvestablePatch.PlayerHarvestInProgress` is true, the prefix returns early to avoid double-applying a quality roll when the player path already handles it.

---

## Patches/HarvestPotBehaviourYieldPatch.cs

Patches `HarvestPotBehaviour.GetQuantityToHarvest` (postfix, private method patched by name string). Scales the botanist's harvested quantity by the per-bud yield multiplier from `RandomYieldPerBudPicker`.

### Context: harvest flow rework
The old `PotActionBehaviour.CompleteAction` (with an `EActionType.Harvest` check) was replaced by `HarvestPotBehaviour`. `GetQuantityToHarvest()` now returns how many product items the botanist pulls from a pot.

### Multiplayer guard
Botanists are server-controlled NPCs and the yield roll uses random, so only the server applies the multiplier. Clients calling this for prediction keep vanilla's value; the authoritative result is networked from the server.

### Math
`__result = Math.Max(1, (int)(__result * configuration.RandomYieldPerBudPicker.Pick()))` — floors at 1 so the botanist always harvests at least one item.
