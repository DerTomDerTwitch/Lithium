# BrickPress Module Notes

## ModBrickPress.cs

Module and configuration class. `Apply()` is intentionally empty — all behaviour is handled by the patch at runtime (no game-object mutations needed at scene load).

`InstantPress` defaults to `true` (the module's own `Enabled` flag defaults to `false`, so the feature is off unless the module is enabled in config).

`InstantPress` description: clicking "Begin" produces the brick immediately, skipping the interactive pour/press minigame. Output is identical to finishing the minigame by hand — only the manual pouring and lever-pulling steps are removed.

---

## Patches/BrickPressBeginButtonPatch.cs

Patches `BrickPressCanvas.BeginButtonPressed`.

### What it does

The "Begin" button normally spawns a `UseBrickPress` player task — the interactive pour-then-press minigame. With the module enabled this prefix short-circuits that: `CompletePress` is called directly (the same method the minigame task calls on completion). One click does the whole job.

### Why the UI is left open

`CompletePress` deposits the finished brick into the press's `OutputSlot`, which is only reachable through the open canvas (`OutputSlotUI`). The canvas `Update()` runs every frame and refreshes from press state, so it immediately shows the populated output slot and disables Begin. Closing the UI would hide the result and force the player to re-open the press just to collect it.

### Two unsafe cases that require fallback to vanilla

`CompletePress` **drains everything**: it consumes all loaded product and produces as many bricks as that yields. Two scenarios would destroy product if called blindly:

1. **Mixed inputs** — if product slots hold different items/qualities, only the dominant product should be pressed; the rest must be left untouched. Solution: identify "foreign" slots (items that don't `CanStackWith` the dominant item), detach them before calling `CompletePress`, then re-attach them afterwards. This ensures the drain only sees the dominant product.

2. **Output overflow** — if the resulting bricks don't all fit in the output slot (full, incompatible item, or only partial room), the excess would be voided. There is no way to press "only what fits" because a single `CompletePress` drains the lot with nowhere to stash a partial remainder. In this case the patch falls back to the vanilla minigame, which lets the player press exactly what they want with no risk of loss.

### Overflow probe

To check output capacity, a throwaway brick `ProductItemInstance` is constructed by copying the dominant product (`GetCopy(1)`) and calling `SetPackaging(brickPackaging)` on it. `GetCapacityForItem` on the `OutputSlot` uses this probe to account for the brick's stack limit and reject an output slot already holding an incompatible item.

### Magic numbers / key fields

- `brickPackaging.Quantity` — units of product consumed per brick (the recipe quantity). Used to compute `bricksToProduce = primaryQuantity / unitsPerBrick`.
- `press.GetMainInputs(...)` — returns the dominant input item and its total loaded quantity across all input slots, exactly what `CompletePress` would drain.
- `CanStackWith(primaryItem, false)` — second argument `false` means "strict" stacking check (item identity + quality); slots that fail this are considered foreign.

### Fallback conditions (prefix returns `true`, vanilla runs)

- Module disabled or `InstantPress` is false.
- `press` is null.
- `HasSufficientProduct` returns false or yields a null product.
- `BrickPackaging` is null or has `Quantity <= 0`.
- `GetMainInputs` returns a null primary item.
- `ProductSlots` is null.
- `bricksToProduce <= 0` (not enough dominant product for a whole brick).
- `GetCopy(1)` or `TryCast<ProductItemInstance>` fails.
- `outputCapacity < bricksToProduce` (overflow would void product).
