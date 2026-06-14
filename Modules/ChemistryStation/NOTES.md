# ChemistryStation Module Notes

## ModChemistryStation.cs

`ModChemistryStationConfiguration` exposes a single `CookDurationMinutes` float (default `60`): the **total
in-game minutes a full cook should take** when the module is enabled, regardless of the recipe's vanilla
`CookTime_Mins`. `Apply()` is a no-op beyond the enabled guard; all runtime behaviour lives in the patch.

> Replaces the previous `Speed` multiplier. Existing `ChemistryStation.json` files keep working: the orphaned
> `Speed` key is dropped on first load and `CookDurationMinutes` is written with its default.

---

## Patches/ChemistryStationDurationPatch.cs

**Patches:** `ChemistryStation.OnTimePass` (prefix, `ref int minutes`).

### What it does

Scales the per-tick minute count so the cook reaches its (unchanged) threshold in exactly
`CookDurationMinutes`. Vanilla completes when `CurrentTime >= Recipe.CookTime_Mins`, advancing
`CurrentTime += minutes` each tick, so `speed = Recipe.CookTime_Mins / CookDurationMinutes` and `minutes` is
multiplied by it. The base duration is read live from `CurrentCookOperation.Recipe.CookTime_Mins`.

### Why this patch point

`OnTimePass` is the un-inlinable chokepoint (delegate-bound to `onTimeSkip`, reached per-minute via
`OnMinPass → OnTimePass(1)`) and it calls `CurrentCookOperation.Progress(minutes)`. Earlier the duration was
scaled inside `ChemistryCookOperation.Progress`, but that 3-line helper is inlined by the IL2CPP build, so the
patch silently never ran — `OnTimePass` is reached on both the awake and sleep-skip paths.

### Fractional carry

`CurrentTime` is an integer; a slow cook (`speed < 1`) would repeatedly floor to zero and stall. A
per-station carry, keyed by `__instance.Pointer` (the stable native IL2CPP pointer), accumulates the
fractional remainder and adds it back before flooring. The original `OnTimePass` still runs and handles all
completion/state logic itself — the prefix only rewrites `minutes`.

### Freeze interplay

When a higher-priority freeze prefix (EndOfDay / ElectricBill power-cut) returns false, the prefix sees
`__runOriginal == false` and returns without accruing carry against a tick that won't apply.

### Note on the clock

The station's alarm display shows the vanilla recipe time counting down (`Recipe.CookTime_Mins - CurrentTime`),
so it counts from the recipe's base time over the configured real duration rather than from
`CookDurationMinutes`. This matches how the old speed multiplier behaved.
