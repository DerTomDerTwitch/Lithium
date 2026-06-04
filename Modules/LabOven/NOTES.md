# LabOven Module — Notes

## ModLabOven.cs

**What it does:** Defines the `ModLabOven` module and its configuration. The only tuneable is `Speed` (float, default `1.0`), a cook-speed multiplier applied to the lab oven. The `Apply()` method is a no-op beyond the enabled guard — all runtime behaviour is handled entirely through Harmony patches.

**No meaningful comments in source.**

---

## Patches/OvenCookOperationGetCookDurationPatch.cs

**Patched method:** `OvenCookOperation.GetCookDuration` (postfix)

**What it does:** Divides the game's raw cook-duration result by `config.Speed`, effectively scaling how long a cook takes. A `Speed` value greater than 1.0 shortens cook time; less than 1.0 lengthens it. Uses `Mathf.FloorToInt` to keep the result an integer (matching the game's `int` return type).

**Non-obvious reasoning:** The duration is shortened by *dividing* (not multiplying), so the config field is named as a speed multiplier from the player's perspective — doubling `Speed` halves the duration.

**No meaningful comments in source.**

---

## Patches/OvenCookOperationIsReadyPatch.cs

**Patched method:** `OvenCookOperation.IsReady` (postfix)

**What it does:** Overrides the `IsReady` result by recomputing it as `CookProgress >= GetCookDuration()`. This ensures the readiness check always uses the (already-patched) `GetCookDuration()` value rather than whatever the base game's `IsReady` computed — keeping the two patches consistent with each other.

**Gotcha:** Without this patch, the base `IsReady` logic might cache or compute duration independently of `GetCookDurationPatch`, causing the oven to never finish (or finish instantly) at non-default `Speed` values. This postfix guarantees correctness.

**No meaningful comments in source.**
