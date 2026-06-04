# EffectCombos Module Notes

## ModEffectCombos.cs

### Purpose
Implements the EffectCombos module, which auto-generates named effect combos (e.g. "Golden Tiger") and registers an `EffectComboBonus` bonus-payment handler with `ModCustomers`. When a customer's order contains a product whose effects include all effects in a matching combo, that customer receives a bonus payment.

### Defaults itself on
`OnBeforeConfigurationLoaded` sets `Configuration.Enabled = true` before the JSON is merged, so the module is active by default (unlike most modules that default off). The JSON config can still override this to false.

### Apply() flow
- Auto-generation runs regardless of `Enabled`, so the config file is populated with combos the first time a save loads (even if the module is toggled off). This means the player can see and tune combos before enabling the module.
- The `EffectComboBonus` handler is registered with `ModCustomers` only when `Enabled` is true.
- Generation is skipped if `AutoGenerateCount == 0` or if `Combos` is already non-empty.

### ComboGenerationRanges — auto-generation knobs only
These ranges (min/max for fixed and percentage bonuses, three-effect chance) shape the **first** auto-generation only. Once combos are written to the `Combos` array in the JSON, those values are used directly — the ranges are not re-applied on subsequent loads unless `Combos` is cleared.

### Two-vs-three effects
- `ThreeEffectChance` (default 0.5) is a probability in [0,1]: 0.5 = even split.
- Three-effect combos are intended to be rarer and pay more than two-effect combos.
- Default bonus ranges: two-effect fixed 5–15, three-effect fixed 15–30; two-effect % 3–8, three-effect % 8–15.

### rng.Next upper-bound is exclusive
When rolling `FixedBonus`, `rng.Next(min, max + 1)` is used so that the configured `Max` is **inclusive** (standard `System.Random.Next` upper bound is exclusive).

### Name generation
Combo names are assembled as `{Adjective} {Noun}` (e.g. "Golden Tiger") drawn from two static word lists: 24 adjectives × 24 nouns = 576 unique combinations. `GenerateName` tries up to 100 times per combo before giving up (returns null, breaking generation).

### Deduplication
Both combo names and effect-set signatures are deduplicated. The effect-set signature is a sorted, `+`-joined string of effect names (case-insensitive), so the same effects in a different order don't produce two combos.

### Max generation attempts
`maxAttempts = count * 100` guards against infinite loops when the pool of unique combos is nearly exhausted.

### FallbackEffects
A hard-coded list of 34 effect name strings used only if `Resources.FindObjectsOfTypeAll<Effect>()` fails or returns fewer than 2 effects at runtime (e.g. game types not yet loaded). Under normal conditions the live game data is used.

### GetAvailableEffectNames exception handling
The call to `Resources.FindObjectsOfTypeAll<Effect>()` is wrapped in a try/catch; on failure it logs a warning and falls back to `FallbackEffects` rather than crashing.

---

## BonusPayments/EffectComboBonus.cs

### Purpose
Implements `IBonusPaymentHandler`. Called by `CustomerProcessHandoverPatch` during payment processing for each customer order. Iterates each item in the delivery and awards a combo bonus if the item's product has all effects of a matching combo.

### AffectsDealers guard
If `AffectsDealers` is false and the contract has a non-null `Dealer`, the handler returns false immediately (no bonus applied to dealer contracts).

### Matching logic
Effect names are compared case-insensitively (both the product properties and combo effects are lowercased). A combo matches an item only when **all** of the combo's effects are present in the item's product properties (subset match, not exact match — extra effects on the product are ignored).

### Best-only, no stacking
For each item, only the **single highest-paying combo** is awarded. Multiple combos matching the same item do not stack. "Highest paying" is measured as `fixedBonus * quantity + contractPayment * (percentageBonus / 100) * deliveryShare`.

### deliveryShare
`deliveryShare = item.Quantity / totalUnits` (in [0,1]) — the item's proportion of the total delivery. The percentage part of the bonus is scaled by this share so multi-item orders distribute the percentage bonus proportionally across items.

### Bonus label
The bonus entry is labeled `"\"<ComboName>\" Combo Bonus"` (combo name wrapped in escaped quotes), which appears in the customer payment UI.

### No notes
No non-obvious comments beyond the above.
