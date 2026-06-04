# Helper/ — Notes

## CollectionConverter.cs

Bridges IL2CPP and managed collections. The game uses `Il2CppSystem.Collections.Generic.List<T>`, which does not support LINQ directly.

- `ToArray<T>` / `ToList<T>` — convert an IL2CPP list to a managed array / `List<T>`. Return empty collection (not null) when the source is null.
- `ToIL2CPPList<T>` — convert a managed `List<T>` (nullable) back to an `Il2CppSystem.Collections.Generic.List<T>`. Used whenever game APIs require an IL2CPP list.

## ConfigValidator.cs

Small helpers for sanity-checking loaded configuration values. Each method clamps a value into a sensible range and logs a one-line warning (warnings are always shown regardless of the `Debug` flag) describing what was wrong and what was used instead. The intent is that a typo'd config — e.g. a negative plant growth modifier or a min above its max — fails loudly and safely rather than silently breaking gameplay. These should be called from a config's `Validate()` override.

- `AtLeast(config, field, value, min)` — clamps `value` to `min`; overloads for `float` and `int`.
- `InRange(config, field, value, min, max)` — clamps `value` to `[min, max]`; overloads for `float` and `int`.
- `EnsureOrdered(config, minField, maxField, ref min, ref max)` — if `min > max`, swaps them and warns; overloads for `float` and `int`.

## ProductHelper.cs

Customer/product query helpers extracted from repeated patterns in the Customers module.

- `DealerHasSuitableProduct(customer, out dealerItems)` — checks whether the customer's assigned dealer stocks at least one product matching the customer's `PreferredProperties`. Uses `FirstOrDefault` (not `Single`) when resolving items against `DiscoveredProducts` because a dealer can hold items not yet in that list — `Single` would throw "Sequence contains no matching element" in that case. Returns `true` (and an empty list) when the customer has no desires.
- `GetDealerStockedEffects(dealer)` — distinct effect names across every product currently in the dealer's inventory. Mirrors the product→effect resolution in `DealerHasSuitableProduct`.
- `IsServeable(customer)` — a customer we can reason about: present, with customer data and a backing NPC. Replaces the repeated `c != null && c.CustomerData != null && c.NPC != null` guard in the coverage notifiers.
- `GetDesireNames(customerData, toLower)` — the customer's desired-effect names. Pass `toLower: true` when comparing against already lower-cased effect names (e.g. `EffectCoverageBonus`). Replaces the repeated `PreferredProperties.ToList().Select(p => p.Name).ToList()` chain.
- `GetTotalQuantity(products)` — total units across a contract's product list via the game's own `ProductList.GetTotalQuantity()`. Null-safe (returns 0). Replaces the repeated `Products.entries.ToList().Sum(e => e.Quantity)`.
- `ProductMatchesDesires(pd, desires)` — true if any of the product's properties intersect the desire list.
- `CoveredEffectCount(pd, desires)` — how many of the customer's desired effects this product carries (0 = covers none).
- `FormatDesires(customerData)` — formats desired-effect names via `SmartJoin`.
- `GetMatchCount(pd, desires)` — count of discovered products that match the desire list.

## RandomExtensions.cs

- `PickRandom<T>(source)` — returns a uniformly-random element (or default if the source is empty), using `UnityEngine.Random`. Replaces the repeated `OrderBy(_ => Random.value).FirstOrDefault()` idiom used to pick message templates.

## StableHash.cs

- `Compute(string)` — FNV-1a 32-bit hash. Unlike `string.GetHashCode()` (which is per-process randomized in .NET), this is stable across processes and save reloads, so the same name always yields the same `Random` seed. Used wherever a deterministic seed derived from a name is needed.

## StringExtensions.cs

- `SmartJoin<T>(source, glue, lastGlue)` — joins a sequence with `glue` between all elements and `lastGlue` before the last one (e.g. `"a, b or c"`). Returns empty string for empty sequences, the single element's `ToString()` for a singleton. No notes beyond what the code itself shows.
