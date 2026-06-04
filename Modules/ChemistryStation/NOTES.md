# ChemistryStation Module Notes

## ModChemistryStation.cs

`ModChemistryStationConfiguration` exposes a single `Speed` float (default `1f`).
Semantics: `1` = vanilla speed, `>1` speeds the cook up, `<1` slows it down, `0` pauses it entirely.
`Apply()` is a no-op beyond the enabled guard; all runtime behaviour lives in the patch.

No other meaningful comments.

---

## Patches/ChemistryCookOperationProgressPatch.cs

**Patches:** `ChemistryCookOperation.Progress` (Harmony prefix + postfix).

### Why this approach

`ChemistryCookOperation` has no `GetCookDuration` method to scale (unlike `OvenCookOperation` or `MixingStation`), so a duration-scaling approach is not available.
Instead, the patch intercepts the `mins` parameter that vanilla passes to `Progress` and rescales it: `mins * Speed`.

### Fractional carry

`CurrentTime` is an integer; vanilla advances it by `mins` each call and completes once it reaches the recipe duration.
Naively truncating `mins * Speed` to an integer would cause slow-down values (`Speed < 1`) to repeatedly floor to zero and stall the cook entirely.
To prevent this, a per-operation fractional carry is accumulated in the `Carry` dictionary and added to the next scaled value before flooring. This keeps slow-down accurate, and speed-up / pause (`Speed == 0`) also work correctly — without duplicating any vanilla completion logic.

### Carry dictionary key

The dictionary is keyed by `__instance.Pointer` (the native IL2CPP object pointer) rather than the managed wrapper object. The pointer is stable for the lifetime of the operation. Entries are pruned in the `Postfix` when `IsComplete()` returns true, preventing unbounded growth.

### No vanilla logic duplication

The patch only rewrites `mins`; the prefix returns normally so the original `Progress` method still runs and handles all completion/state logic itself.
