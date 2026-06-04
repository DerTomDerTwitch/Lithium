# MixingStations Module Notes

## ModMixingStations.cs

Configuration split into two independent tiers — standard `MixingStation` and `MixingStationMk2`:

- `InputCapacity` / `Mk2InputCapacity` — how many items the station accepts per mix batch.
- `MixStepsPerSecond` / `Mk2MixStepsPerSecond` — speed multiplier for mix operations.

Both tiers default to `InputCapacity = 20` and `MixStepsPerSecond = 1` (vanilla-equivalent).

`Apply()` is a no-op beyond the enabled guard; all tuning happens at runtime via patches.

---

## Patches/MixingStationCapacityPatch.cs

**Patched method:** `MixingStation.Start` (postfix)

Sets `__instance.MaxMixQuantity` after the station initialises.

**Why `TryCast` is needed:** `MixingStationMk2` derives from `MixingStation` and inherits `Start`, so this postfix fires for both tiers. `TryCast<MixingStationMk2>()` returns non-null only for the Mk II instance, allowing the correct capacity config value to be applied per tier.

---

## Patches/MixingStationSpeedPatch.cs

**Patched method:** `MixingStation.GetMixTimeForCurrentOperation` (postfix, `ref int __result`)

Divides the vanilla mix-time target by the configured speed factor, clamped to a minimum of 1.

**Design history / why this approach:** An earlier version of this patch fully reimplemented `MixingStation.OnMinPass` (advancing `CurrentMixTime` by `MixStepsPerSecond` and duplicating completion/clock/light logic). That approach was fragile across game updates. The current approach instead shrinks the *target* that vanilla's own timer counts toward: vanilla advances `CurrentMixTime` by 1 per game-minute and completes once it reaches `GetMixTimeForCurrentOperation()`, so dividing that target by the speed factor makes operations finish proportionally faster while leaving all vanilla logic intact. This mirrors the LabOven cook-duration patch strategy.

**Magic number / math:** `Mathf.Max(1, Mathf.CeilToInt(__result / (float)speed))` — both the speed and the result are floored to 1 to prevent division-by-zero and zero-duration operations.

**Why `TryCast` is needed (same as capacity patch):** `MixingStationMk2` inherits `GetMixTimeForCurrentOperation` from `MixingStation`, so the postfix fires for both tiers; `TryCast<MixingStationMk2>()` selects the correct speed config value.
