# WateringCans Module Notes

## ModWateringCan.cs

Module class and configuration for the functional watering can.

**Config fields:**
- `DrainModifier` (float, default `1.0f`): A multiplier applied to each pour amount. Values below 1.0 make each watering action use less water (the can drains slower / lasts longer per fill); values above 1.0 increase per-pour consumption. At the default of 1.0 the vanilla behaviour is preserved.

`Apply()` is a no-op beyond the enabled guard — no prefab mutations needed.

## Patches/FunctionalWateringCanPourAmountPatch.cs

**Patched method:** `WaterContainerPourable.PourAmount` (prefix, by-ref `amount` parameter).

The prefix multiplies the incoming `amount` value by `DrainModifier` before the original method runs. Because `amount` is passed by ref and modified in-place, the original method receives the already-scaled value — this scales how much water is deducted from the can per watering action.

**Gotcha — prefix on a property or method with a ref parameter:** The patch signature takes `ref float amount`, matching the method's parameter. Modifying it in the prefix is the correct IL2CPP-interop approach; returning void (not bool) means the original still executes with the modified value.
