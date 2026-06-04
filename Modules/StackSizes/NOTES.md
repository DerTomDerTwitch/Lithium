# StackSizes Module — Notes

## ModStackSizes.cs

### What it does
Defines the configuration POCO (`ModStackSizesConfiguration`) and the module class (`ModStackSizes`).
Stack-size overrides are applied per `EItemCategory` with per-item overrides and an ignore list.
Also exposes `ExperimentalCashStacking` and `CashMaxBalance` for the cash-stacking reimplementation.

### Configuration fields

| Field | Default | Meaning |
|---|---|---|
| `ExperimentalCashStacking` | `false` | Opt-in; reimplements the $1000 per-stack cash cap. Off by default — see risk note below. |
| `CashMaxBalance` | `100000` | Max dollars a single cash stack can hold when `ExperimentalCashStacking` is enabled. |
| `CategorySizes` | see below | Stack limit per `EItemCategory`. |
| `ItemOverrides` | `{}` | Per-item-ID overrides; take priority over category sizes. |
| `IgnoredItems` | `[]` | Item IDs completely excluded from stack-size changes. |

### Default `CategorySizes`

| Category | Limit |
|---|---|
| Product | 20 |
| Packaging | 20 |
| Agriculture | 20 |
| Tools | 10 |
| Furniture | 10 |
| Lighting | 10 |
| Cash | 1000 |
| Consumable | 20 |
| Equipment | 20 |
| Ingredient | 20 |
| Decoration | 10 |
| Clothing | 10 |
| Storage | 10 |

### Gotcha: CashInstance.MAX_BALANCE cannot be set in Apply()
`Apply()` explicitly does NOT write `CashInstance.MAX_BALANCE`. Writing that static field from an
uninitialised-class context (scene load or `Registry.Start`) throws an uncatchable `AccessViolationException`
because the IL2CPP class is not yet fully initialised at that point. Instead, the balance clamp is raised
from inside live `CashInstance` method patches (`CashSetBalancePatch`, `CashChangeBalancePatch`) where the
IL2CPP class is guaranteed to be initialised.

### ExperimentalCashStacking — risk note
The game hard-caps a cash stack at $1,000 via the native constant `CashInstance.MAX_BALANCE`, which cannot
be written at runtime. Enabling `ExperimentalCashStacking` makes Lithium reimplement the cash balance/drag
clamps to use `CashMaxBalance` instead. Because the native money-transfer logic cannot be fully inspected,
**test on a BACKUP save and watch your total money** — a discrepancy means you should disable this feature.

---

## ItemRegistry.cs

### What it does
Static helper that tracks every `ItemDefinition` that passes through `Registry`. On each item it checks:
1. If the item ID is in `IgnoredItems` → skip.
2. If the item ID is in `ItemOverrides` → apply that size and return.
3. If the item's category is in `CategorySizes` → apply that size.
4. Otherwise → log a warning that the category has no configured value.

`AllItemDefinitions` is populated by `RegistryStartPatch` (bulk, at `Registry.Start`) and augmented
incrementally by `RegistryAddToRegistryPatch` (per item as it is registered).

---

## Patches/RegistryStartPatch.cs

### Patched method
`Registry.Start` — `HarmonyPostfix`

### What it does
After the game's `Registry` initialises, iterates the entire `ItemRegistry` (IL2CPP list, must be copied
to a CLR `List` to use LINQ), stores all `ItemDefinition`s in `ItemRegistry.AllItemDefinitions`, then calls
`UpdateEntireRegistry()` to apply stack-size overrides to every item at once.

### No meaningful inline comments beyond logic.

---

## Patches/RegistryAddToRegistryPatch.cs

### Patched method
`Registry.AddToRegistry` — `HarmonyPostfix`

### What it does
Intercepts each item as it is registered (covers items added after `Registry.Start` fires). Adds the
`ItemDefinition` to `ItemRegistry.AllItemDefinitions` and immediately applies the stack-size override for
that item.

### No meaningful inline comments beyond logic.

---

## Patches/ExperimentalCashStackingPatch.cs

### Overview
This file reimplements the drag-amount picker and balance clamp for cash stacks, replacing the hard-coded
$1,000-per-stack game limit with the configurable `CashMaxBalance`. It is **opt-in via
`ExperimentalCashStacking`** and risky (see `ModStackSizes.cs` notes above).

The native constant `CashInstance.MAX_BALANCE = 1000` cannot be changed at runtime. Instead, every place
that reads it for clamping is overridden by a postfix patch that raises the ceiling to `CashMaxBalance`.

### Static helper: `CashStackingConfig`

**Drag-amount state (`DragAmount`, `DragActive`)**
The drag amount is tracked in `CashStackingConfig.DragAmount`, NOT read back from
`ItemUIManager.draggedCashAmount`. Reason: the native `UpdateCashDragAmount` re-clamps `draggedCashAmount`
to $1,000 on every frame, so reading it back would immediately corrupt the value. By tracking state
independently and re-asserting it each frame, the per-frame clamp is neutralised.

**Scroll-acceleration de-duplication (`BeginScrollTick` / `_lastStepFrame`)**
The game fires `AddCashAmount`/`SubtractCashAmount` twice per scroll notch (duplicate UI manager instances
in the same frame). Only the first call per frame should advance the step; subsequent calls in the same
frame return `false` and skip the step calculation, preventing a single scroll notch from jumping a large
amount due to an apparent dt ≈ 0.

**"Nice number" step ladder (`Ladder`, `BuildLadder`, `SnapToLadder`)**
The scroll step is always snapped to a "nice number" ladder: `1, 5, 10, 20, 50, 100, 200, 500, 1000,
2000, 5000, ...` (powers of 10 × 1, 2, 5; note: 2 itself is skipped, first entry after 5 is 10).
The ladder is pre-built up to 100,000,000. `SnapToLadder` returns the largest ladder entry ≤ the
requested value (floor to nearest nice number), never below the smallest entry (1).

**`AcceleratedStep` — scroll speed → step size**
The step size depends only on scroll *speed* (time between notches), never on a minimum magnitude floor,
so scrolling slowly always steps by $1 and every value is reachable.
- `dt` = time since the previous scroll tick (floored to 0.0001 s).
- `speed01` = `Clamp01((0.18 - dt) / 0.16)`: 0 when slow (≥ 0.18 s/tick), 1 when fast (≤ 0.02 s/tick).
- `curve = speed01³`: cubic ramp keeps the step near $1 longer, only shooting up when really fast.
- `maxStep = max(5, amount × 0.33)`: the fastest possible single-tick step scales with the current drag
  amount so larger stacks accelerate to bigger jumps.
- Final step = `SnapToLadder(Lerp(1, maxStep, curve))`.

**`SourceMax`**
Returns `min(cap, source.Balance)` — the drag amount can never exceed what is actually in the source
stack, preventing duplication.

### Patches

#### `CashDragStartPatch` — `ItemUIManager.StartDragCash` (Postfix)
Captures the initial drag amount (rounded, min 1) into `CashStackingConfig.DragAmount` and sets
`DragActive = true`.

#### `CashDragEndPatch` — `ItemUIManager.EndCashDrag` (Prefix + Postfix)
**Prefix**: before the native drag-end, captures the target and source `CashInstance`s and their current
balances plus the dragged amount into `CashCombineState` (passed via Harmony `__state`).

**Postfix**: clears `DragActive`. Then handles the case where the native merge was limited by the $1,000
cap: the native transfer moves `min(dragged, 1000 - target)`, which is 0 if the target is already at
$1,000. The postfix computes how much the native actually moved (`nativeMoved = Target.Balance -
TargetBefore`), computes `extra = desired - nativeMoved` (capped to available room up to `cap` and to
`SourceBefore`), and if `extra > 0.001` calls `ChangeBalance(+extra)` on target and `ChangeBalance(-extra)`
on source — keeping the total money conserved.

#### `CashDragAddPatch` — `ItemUIManager.AddCashAmount` (Postfix)
On a scroll-up tick (guarded by `BeginScrollTick`), advances `DragAmount` by `AcceleratedStep`, clamped
to `SourceMax`. Snaps to the exact source balance at the ceiling so the full stack can always be grabbed
without orphaning fractional cents. Writes back to `draggedCashAmount`.

#### `CashDragSubtractPatch` — `ItemUIManager.SubtractCashAmount` (Postfix)
On a scroll-down tick (guarded by `BeginScrollTick`), decreases `DragAmount` by `AcceleratedStep`,
floored to 1. Writes back to `draggedCashAmount`.

#### `CashDragUpdatePatch` — `ItemUIManager.UpdateCashDragAmount` (Postfix)
Called every frame while dragging. Re-asserts `DragAmount` back into `draggedCashAmount` after the native
method clamps it to $1,000. Also clamps `DragAmount` down if the source balance has shrunk.

#### `CashSetBalancePatch` — `CashInstance.SetBalance` (Postfix)
If `SetBalance` was clamped by the native $1,000 cap (i.e. `instance.Balance < desired`), raises
`Balance` to `min(requested, cap)`. Instance-level write; no static-field access violation risk.

#### `CashChangeBalancePatch` — `CashInstance.ChangeBalance` (Prefix + Postfix)
**Prefix**: captures `Balance` before the native call into `__state`.
**Postfix**: if the result was clamped by the native cap, raises `Balance` to `min(before + delta, cap)`.

#### `CashCanDragIntoSlotPatch` — `ItemUIManager.CanCashBeDraggedIntoSlot` (Postfix)
The native method returns `false` for a slot already at the $1,000 cap. This postfix overrides that: if
the slot already holds cash (join case) or is an empty cash-capable slot, it allows the drop (`__result =
true`). Does not override the "this slot cannot hold cash at all" rule — only runs when the native already
returned false.

#### `CashSlotCapacityPatch` — `ItemSlot.GetCapacityForItem` (Postfix)
`GetCapacityForItem` is used by shift-click/quick-transfer and drag-combine to size the merge. The native
returns remaining capacity based on the $1,000 cap. The postfix raises the return value to
`floor(cap - existing)` for cash-compatible slots (those already holding cash, or empty cash-capable
slots). Uses the same "stored != null OR (empty AND CanSlotAcceptCash)" rule to avoid overriding
non-cash-capable slots. The actual money movement still flows through the game's own transfer logic, so
totals stay balanced.

#### `CashCanStackPatch` — `ItemInstance.CanStackWith` (Postfix)
Cash has `StackLimit = 1`, so the native quantity-based stacking check returns `false` for a non-empty
cash slot — this caused dragging a $200 stack onto a $1,000 stack to *swap* rather than merge. The
postfix: if both `__instance` and the argument are `CashInstance`s, force `__result = true` so the drag
path treats them as stackable and triggers a combine instead of a swap.
