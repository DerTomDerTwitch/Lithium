# Util/ — Notes

## WeightedNormalizer.cs

Continuous weighted interpolation: maps a normalized input `n` in [0, 1] to a float output by interpolating across a list of `(weight, value)` pairs.

- Pairs are added via `Add(weight, value)`. Weights must be non-negative.
- On the first `Evaluate` call after any `Add`, `Initialize()` builds a cumulative distribution function (CDF) by dividing each weight by the total weight and accumulating.
- Edge cases: if total weight is 0, the CDF degenerates to a single entry of 1.0 (all inputs return `_values[0]`). If `n <= 0` returns first value; if `n >= 1` returns last value.
- `Evaluate(n)` finds the CDF bucket containing `n` and linearly interpolates between the two bracketing values, so the result is a smooth function of `n` rather than a discrete step.
- Used by `ModPlants` for yield multiplier and quality offset (both described in the module catalog as "interpolated `WeightedNormalizer`s").

## WeightedPicker.cs

Discrete weighted random selection from a `Dictionary<T, float>` of item → weight mappings.

- `Pick()` — selects an item randomly, proportional to weight. Internally converts to integer centile precision (`(int)(_totalWeight * 100)`) to drive `Random.Next`.
- `PickManually(value)` — selects the item at a specific weight position; useful for deterministic/reproducible picks.
- `_totalWeight` and `_hasChanged` flag: the total weight is recalculated lazily via `Rebuild()` only when the dictionary has changed, avoiding redundant summation.
- Throws `PickerEmptyException` (a custom `Exception` subclass) if picked from an empty dictionary.
- Constructor accepts an optional `Random` instance; default uses `Guid.NewGuid().GetHashCode()` as seed.
- Supports `Add`, `AddRange`, `Remove`, `Clear`, and index-access (`this[key]`).

## SuccessChanceCalculator.cs

Computes the float [0, 1] probability that a customer accepts a sample offering. Parameters:

- `desires` / `effects` — the customer's desired effect names and the product's actual effect names. Acceptance starts as `coveredEffects / desires.Length` (or 1.0 if no desires). If `requireEffectMatch` is true and no desire is covered, immediately returns 0.
- `qualityLevelModifier` / `standard` / `maxQualityOverDeliveryLevels` — quality difference between the product and the customer's standard is multiplied by `qualityLevelModifier` and added to acceptance. Over-delivery is capped at `maxQualityOverDeliveryLevels` levels so a Heavenly sample to a Trash-standard NPC adds only a little and cannot rescue a poorly-covered sample; under-delivery keeps its full penalty.
- `includeDrugPreference` / `affinities` / `drugAffinitySharpness` — if enabled, the product's `EDrugType` is looked up in `affinities.ProductAffinities`. Affinity is signed (-1..1): non-positive affinity produces a factor of 0 (customer rejects drug type); positive affinity is curved via `Mathf.Pow(affinity, sharpness)` so acceptance climbs quickly when sharpness < 1. The factor multiplies acceptance.
- `baseAcceptance` — flat additive bonus applied after all modifiers.
- Final result is clamped to [0, 1] via `Mathf.Clamp01`.

## DeliveryUtils.cs

Applies `ModShops` delivery configuration to in-game `DeliveryShop` objects. Called from `ModShops.Apply()`.

- Discovers all `DeliveryShop` objects in the scene (including inactive ones) and indexes them by `ShopName`.
- For each entry in `config.Deliveries`:
  - `Unchanged` — re-evaluates the vanilla relationship-based availability for the three named suppliers (Albert, Shirley, Salvador) by checking `RelationData.RelationDelta > Supplier.DELIVERY_RELATIONSHIP_REQUIREMENT`. Does not change availability for unknown shop names.
  - `Never` / `Always` — forces availability off/on unconditionally.
  - `AfterReachingXP` — gates on `LevelManager.Instance.TotalXP >= entry.Value.XPRequirement`.
- Important: the game replaced the runtime-settable `DeliveryShop.IsAvailable` with `AvailableByDefault`, and the delivery fee is now computed by `GetDeliveryFee()` (no setter). Availability is driven via `AvailableByDefault` + `GameObject.SetActive`. The fee override is applied through `DeliveryShopFeePatch` on `GetDeliveryFee()` — not here.
