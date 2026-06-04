# DryingRacks Module Notes

## ModDryingRacks.cs

Configuration class `ModDryingRacksConfiguration` exposes two tunables:

- **`Capacity`** (default 20): item slot count applied to each `DryingRack` whenever its canvas opens.
- **`PerQualityDryTimes`**: a dictionary keyed by `EQuality` member name (`Trash`, `Poor`, `Standard`, `Premium`, `Heavenly`) that specifies how many in-game **minutes** a drying operation spends at each starting quality tier before it advances to the next tier. The values are intentionally graduated — higher tiers take progressively longer. `Heavenly` (1200 min) acts as the terminal cap; items that start at `Premium` or above and have elapsed their threshold are finalized directly to `Heavenly` quality rather than incremented. Defaults (minutes): Trash=240, Poor=360, Standard=720, Premium=800, Heavenly=1200.

`Validate()` clamps `Capacity` to a minimum of 1 and each dry-time entry to a minimum of 0.

The `Apply()` override is a stub (guarded by `Enabled`) — no runtime object mutations are needed; all logic runs through patches.

---

## Patches/DryingRackCapacityPatch.cs

**Patches:** `DryingRackCanvas.SetIsOpen` (Postfix)

On every canvas open event, sets `rack.ItemCapacity` to `config.Capacity`. Runs postfix so the game's own open logic completes first; the capacity override simply overwrites whatever the game just set.

---

## Patches/DryingRackPatch.cs

**Patches:** `DryingRack.OnMinPass` (Prefix — fully replaces the original)

This is the core time-advancement loop, run once per in-game minute per rack. It reimplements `OnMinPass` entirely (prefix returns `false` to skip the original):

1. Iterates `DryingOperations` via `.ToArray()` — a snapshot clone — to avoid collection-modification exceptions during iteration.
2. Increments each operation's `Time` counter by 1 each minute.
3. Looks up the per-quality threshold from `PerQualityDryTimes`, defaulting to 720 if the quality key is absent.
4. When `Time >= threshold`:
   - If `StartQuality >= EQuality.Premium`: attempts to finalize the operation to `EQuality.Heavenly` via `TryEndOperation`, but only if this instance is the server **and** there is output capacity for the resulting quantity. The seed passed to `TryEndOperation` is a random `int` in `[int.MinValue, int.MaxValue]`.
   - Otherwise: calls `IncreaseQuality()` to advance the operation one tier.

**Gotcha:** The 720-minute fallback default for unknown quality keys mirrors the vanilla `Standard` dry time and is used consistently across all three patches as the safe sentinel value.

---

## Patches/DryingOperationPatch.cs

**Patches:** `DryingOperation.GetQuality` (Prefix — fully replaces the original)

Overrides the quality-reporting method so that the rest of the game sees the correct "current quality" at any point in time:

- Returns `StartQuality + 1` (i.e. the next tier up) if the operation has elapsed its per-quality threshold.
- Returns `StartQuality` (unchanged) otherwise.

This keeps the game's own UI and logic consistent with the custom time thresholds rather than whatever the vanilla `GetQuality` would return.

---

## Patches/DryingOperationUIPatch.cs

**Patches:** `DryingOperationUI.UpdatePosition` (Prefix — fully replaces the original)

Reimplements the drying-operation progress indicator in the rack UI:

- Computes `tNorm` = `Time / threshold` clamped to [0, 1] — a linear 0→1 progress fraction.
- Computes `timeLeft` = `threshold - Time` clamped to [0, threshold], then splits into hours and minutes.
- Sets `Tooltip.text` to the string `"{hours}h {minutes}m until next tier"`.
- Moves the UI element's anchored X position by lerping between `left = -62.5f` and `right = 62.5f` using `tNorm`. These are the hardcoded pixel extents of the rack's progress slider track.

The original `UpdatePosition` is skipped (prefix returns `false`) because it would use vanilla time thresholds instead of the config-driven ones.
