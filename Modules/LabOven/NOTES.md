# LabOven Module — Notes

## ModLabOven.cs

**What it does:** Defines the `ModLabOven` module and its configuration. The only tuneable is
`CookDurationMinutes` (float, default `60`), the **total in-game minutes a full cook should take** when the
module is enabled, regardless of the ingredient's vanilla `CookableModule.CookTime`. `Apply()` is a no-op
beyond the enabled guard — all runtime behaviour is handled through a Harmony patch.

> Replaces the previous `Speed` multiplier. Existing `LabOven.json` files keep working: the orphaned `Speed`
> key is dropped on first load and `CookDurationMinutes` is written with its default.

---

## Patches/LabOvenDurationPatch.cs

**Patched method:** `LabOven.OnTimePass` (prefix, `ref int minutes`)

**What it does:** Scales the per-tick minute count so the cook reaches its (unchanged) completion threshold
in exactly `CookDurationMinutes`. Vanilla completes when `CookProgress >= GetCookDuration()` advancing one
minute per tick, so `speed = vanillaDuration / CookDurationMinutes` and `minutes` is multiplied by it. The
live operation's base duration is read from `CurrentOperation.GetCookDuration()`.

**Why patch `OnTimePass`, not `GetCookDuration`:** `GetCookDuration` is inlined at the completion sites in the
IL2CPP build, so a patch on it silently never affects when a cook finishes. `OnTimePass` is the un-inlinable
chokepoint (delegate-bound to `onTimeSkip`, reached per-minute via `OnUncappedMinPass → OnTimePass(1)`), and
it advances `CurrentOperation.UpdateCookProgress(minutes)` — so scaling `minutes` works on both the awake and
sleep-skip paths.

**Fractional carry:** `CookProgress` is an integer; a slow cook (`speed < 1`) would repeatedly floor to zero
and stall. A per-oven carry (keyed by `__instance.Pointer`) accumulates the fractional remainder and adds it
back before flooring.

**Freeze interplay:** When a higher-priority freeze prefix (ElectricBill power-cut) returns false, the prefix
sees `__runOriginal == false` and returns without accruing carry against a tick that won't apply.

**Note on the clock:** The oven's on-screen timer shows the vanilla cook duration counting down (it reads
`GetCookDuration() - CookProgress`), so it counts from the ingredient's base time over the configured real
duration rather than from `CookDurationMinutes`. This matches how the old speed multiplier behaved.
