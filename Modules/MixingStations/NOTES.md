# MixingStations Module Notes

## ModMixingStations.cs

Configuration split into two independent tiers — standard `MixingStation` and `MixingStationMk2`:

- `InputCapacity` / `Mk2InputCapacity` — how many items the station accepts per mix batch.
- `MixTimePerItem` / `Mk2MixTimePerItem` — minutes spent **per item** in the batch. The game computes total
  mix time as `MixTimePerItem * Quantity`, so a 10-item batch at `15` takes 150 in-game minutes.

Both tiers default to `InputCapacity = 20` and `MixTimePerItem = 15` (vanilla-equivalent).

`Apply()` is a no-op beyond the enabled guard; all tuning happens at runtime via patches.

> `MixTimePerItem` replaces the previous `MixStepsPerSecond` speed multiplier. Existing `MixingStation.json`
> files keep working: the orphaned `MixStepsPerSecond` keys are dropped on first load and the new keys are
> written with their defaults.

---

## Patches/MixingStationCapacityPatch.cs

**Patched method:** `MixingStation.Start` (postfix)

Sets `__instance.MaxMixQuantity` after the station initialises.

**Why `TryCast` is needed:** `MixingStationMk2` derives from `MixingStation` and inherits `Start`, so this
postfix fires for both tiers. `TryCast<MixingStationMk2>()` returns non-null only for the Mk II instance,
allowing the correct capacity config value to be applied per tier.

---

## Patches/MixingStationDurationPatch.cs

**Patched method:** `MixingStation.Start` (postfix)

Sets `__instance.MixTimePerItem` after the station initialises, so the game's own timer counts toward
`MixTimePerItem * Quantity`.

**Why set the field directly (vs. scaling the tick):** `MixTimePerItem` is a plain instance field — not an
inlined method or a `const` — so writing it is honoured everywhere it's read: the completion threshold
(`GetMixTimeForCurrentOperation`) **and** the on-screen clock (`GetMixTimeForCurrentOperation - CurrentMixTime`,
and the Mk II screen). That means the clock shows the **real** remaining minutes, unlike the old approach of
multiplying the per-minute tick. No fractional carry is needed because the threshold itself moves.

**Design history:** Earlier versions scaled the per-minute advance (an `OnTimePass`/`GetMixTimeForCurrentOperation`
patch) with a `MixStepsPerSecond` multiplier. Setting the duration field directly is simpler, gives an accurate
clock, and matches how `MixingStationCapacityPatch` already configures the station at `Start`.

**Why `TryCast` is needed (same as capacity patch):** `MixingStationMk2` inherits `Start`, so the postfix fires
for both tiers; `TryCast<MixingStationMk2>()` selects the correct per-item config value.

**Caveat:** Because the field is set at `Start`, a live config reload only affects stations that (re)start
afterwards — identical to the `MaxMixQuantity` capacity override.
