# Customers Module — Notes

## Module overview

The Customers module is the largest in Lithium. It reworks customer demand and payment across several
interacting systems:

- **Effect-coverage product selection**: orders are weighted toward products that cover more of the
  customer's desired effects (coverage^Exponent weighting; zero-coverage products are never picked).
- **Sample-offering acceptance**: acceptance is gated by effect coverage, drug-type affinity, quality
  tier, and a base acceptance floor; all individually configurable.
- **Direct-sale matching**: in-person offers can be gated by effect coverage and by a minimum elapsed
  fraction of the bulk-order interval.
- **Contracts**: XP-gated system with reduced-price substitute deals, dealer/listed pricing, next-day
  retry on refusal/expiry, and extended acceptance windows.
- **Order cadence patterns**: each customer is assigned a deterministic archetype (Weekly,
  TwiceWeekly, EveryThreeDays) from their name-hash, producing fewer, larger (bulk) orders whose
  per-order size conserves the customer's intended weekly volume.
- **Scaled bulk rewards**: affection and XP for a bulk-order handover are topped up to
  multiplier × the base gain, so a weekly order earns as much as the several daily orders it replaced.
- **Offer acceptance windows + deadline texts**: per-order window extended by a per-unit bonus above
  a base quantity, then scaled by a global multiplier; customer texts the deadline. The extended window
  is persisted across save/reload.
- **Coverage-notification system**: repurposes an existing NPC's Messages conversation as a "Lithium"
  contact and sends startup overview, product-listing change coverage diff, dealer inventory
  coverage breakdown, and no-dealer customer coverage reports.
- **IBonusPaymentHandler extension point**: `ModCustomers` holds a list of handlers; the handover
  patch iterates them so new bonus types can be added without patch changes.
- **Save-slot persistence**: `SaveSlotStore<T>` backs contract-retry day and offer-deadline tracking,
  keyed to the loaded save's folder name + organisation name.

State cleared on every `Apply()` (scene load): `ProductCoverageNotifier`, `DealerCoverageNotifier`
no-dealer cache, `ContractRetryTracker`, `OfferDeadlineTracker`, `OrderPatternProfile` cache, and
`CustomerContractGenerationPatch` state all reset so a freshly loaded save starts clean.

---

## ModCustomers.cs

### Configuration classes

**`EffectMatchBonus`** — per-bucket bonus parameters: `FixedBonusMin/Max` (per unit) and
`PercentageBonusMin/Max` (share of contract payment). Used for 1, 2, and 3+ covered-effect tiers.

**`EffectBonus`** — gates the effect-coverage bonus; `AffectsDealers` controls whether dealer
contracts are included.

**`SampleOffering`**
- `RequireEffectMatch`: a sample covering none of the customer's desired effects is rejected outright
  — no quality/drug-affinity/base-acceptance fallback can override it.
- `MaxQualityOverDeliveryLevels`: caps how many quality tiers above the customer's standard still
  contribute an acceptance bonus. Over-delivering quality (e.g. Heavenly to someone who'd accept
  Trash) is deliberately not allowed to rescue a poorly-covered sample.
- `DrugAffinitySharpness`: exponent on drug-type affinity (0..1). Below 1 makes acceptance climb
  quickly for modest affinity (e.g. 0.5 = square root: affinity 0.25 → multiplier 0.5 instead of
  0.25). Affinity 0 or below still yields 0% (disliked types are always rejected).

**`DirectSales`**
- `RequireEffectMatch`: same hard requirement as contracts — no effect match, no direct sale.
- `MinIntervalFractionBeforeOffer`: anti-exploit gate for bulk-pattern customers. Prevents the player
  from selling daily to a customer who should only order weekly. The customer refuses an unscheduled
  in-person offer until this fraction of the wait until their next scheduled order has elapsed.
  0 = no gate (vanilla "buy anytime"). Only active for customers reshaped by order patterns.
- `PriceToleranceMultiplier`: scales the price the customer *perceives* when judging an in-person
  offer, shifting the vanilla acceptance curve along the price axis without changing the money paid.
  > 1 = more forgiving (the no-penalty ceiling moves up: 1.25 ≈ ~2.0× ceiling), < 1 = stricter,
  1.0 = vanilla. Applied only when the module is enabled.
- `PriceToleranceJitter`: day-to-day per-customer randomness on `PriceToleranceMultiplier` as a
  ± fraction. The roll is deterministic within a day for a given customer so the displayed success
  chance matches the one used. 0 = no variation; 0.2 = ±20%.

**`Contracts`**
- `NotificationWindowStartHour` / `NotificationWindowEndHour`: hour-of-day range in which the daily
  "you don't stock my effects" complaint texts are sent. Each customer's exact minute is derived
  deterministically from their name and spread across the window so texts don't arrive all at once.
  Must be daytime hours to survive the sleep fast-forward (night minutes are skipped). Start < End
  enforced by `Validate()`; reverts to 8–22 if invalid.
- `ReducedDealPriceMultiplier`: when neither player nor dealer has a product matching the customer's
  desired effects, the customer still buys a substitute at this fraction of the product's *default*
  market value (not the listed price). 0.75 = 25% below default value.
- `DealerSellAtListedPrice`: when a dealer fills a matching deal, the customer pays the player's set
  `ProductManager` listed price rather than the game's standard per-unit market value.
- `SellAtListedPrice`: same for the player filling a direct matching order.
- `RetryNextDayOnRefusal`: if the player refuses a contract or it expires unanswered, the customer
  re-attempts the next day instead of waiting for their next scheduled order day.
- Template strings support `##DESIRES##` (comma-joined desired effect names), `##DEALER##` (assigned
  dealer's first name), `##DEADLINE##` (formatted accept-by time), `##DAY##` (next order day text),
  `##QUANTITY##` (units ordered).

**`ProductSelection`**
- `CoverageBiasExponent`: a candidate's pick weight is coverage^exponent. 3 means a 3-effect match
  is 27× as likely as a 1-effect match. Products covering none of the customer's desired effects have
  weight 0 and are never picked.
- `EnableSecondProduct` / `SecondProductChance` / `SecondProductQuantityShare`: bulk matching orders
  can optionally include a second different product; the specified fraction of total quantity goes to
  it (e.g. 0.25 = a quarter of units).

**`BulkRewards`**
- The game's actual handover affection/XP gain is measured and then topped up so the total is
  multiplier × that base — avoids guessing reward constants.
- `MaxRewardMultiplier` caps the scale so an unusually large order can't produce a runaway bonus.
  Effective multiplier = min(orderMultiplier, MaxRewardMultiplier).

**`OrderPatternWeights`** — relative likelihoods of each cadence archetype. Do not need to sum to
100; any non-negative values are valid. No daily cadence is intentional: every customer orders at
most a few times per week.

**`OrderPatterns`**
- `BulkOrderSizeFactor`: player-balancing scale applied to the quantity multiplier at its source,
  so both order size and scaled affection/XP rewards move together. Clamped at 0.
- `AnnounceNextOrder`: after a direct order completes, the customer texts roughly when they'll next
  order.
- `ShowPatternInContactPanel`: displays order days + cadence in the phone Contacts customer panel,
  only when order patterns are active (Enabled + XP requirement met).

**`AcceptanceWindow`**
- Window = (game's default + size bonus) × DurationMultiplier, bounded by min(MaxWindowMinutes,
  AbsoluteMaxWindowMinutes).
- `BaseQuantity`: orders at or below this keep the default window (scaled by DurationMultiplier).
- `MinutesPerExtraUnit`: e.g. 50-unit order at BaseQuantity 10 with 60 min/unit → 40 × 60 = 2400
  extra minutes added before multiplication.
- `MaxWindowMinutes` default 8640 = 6 in-game days. Hard coded ceiling at 6 days (see
  `OfferAcceptanceWindow.AbsoluteMaxWindowMinutes`) to avoid the 7-day rollover that kills deals.
- `SendDeadlineMessage`: fires for every expiring offer (not just extended ones); works even if the
  window extension is disabled.

**`CoverageNotifications`**
- `ContactNpcName` / `ContactDisplayName`: an existing NPC is located by `fullName` each time
  (never cached), the conversation is renamed to `ContactDisplayName`, and messages are sent through
  it via `MessagingManager`. Default contact is "Manny Oakfield" renamed to "Lithium".
- `ListUncovered`: coverage texts additionally enumerate uncovered customer names.
- `NotifyDealerInventoryOnClose`: when a dealer's in-person inventory is closed, texts which of
  their assigned customers' effects their current stock fails to cover.
- `NotifyNoDealerCustomers`: texts coverage for customers with no assigned dealer at startup and
  again on every list/delist event.

**`ModCustomersConfiguration.Validate()`** clamps and cross-checks
`Contracts.NotificationWindowStartHour` (0–23) and `NotificationWindowEndHour` (1–24); reverts both
to 8 / 22 if start ≥ end.

### `ModCustomers` class

- Constructor: registers `CustomerNotificationState` with `ClassInjector.RegisterTypeInIl2Cpp<T>()`
  (must happen before any instance is attached to a GameObject) and adds `EffectCoverageBonus` as the
  default bonus-payment handler.
- `Apply()`: resets all cross-save caches (product coverage baseline, no-dealer cache, contract retry
  store, offer deadline store, order-pattern profile cache, contract-generation state), then starts
  the `StartupOverviewRoutine` coroutine if coverage notifications are enabled.
- `StartupOverviewRoutine()`: polls `LithiumStartupReport.WorldReady()` at 1-second intervals for up
  to 30 seconds; adds a 2-second buffer after the world is ready so listed products and dealer
  inventories have settled before snapshotting.
- `RegisterBonusPaymentHandler()`: guards against duplicate registrations by checking type.

---

## LithiumContact.cs

Repurposes an existing in-game NPC's Messages conversation as the "Lithium" contact. The NPC is
resolved by `fullName` each time `Send()` is called (no caching), so a save reload's fresh NPC
instance is always used. On each send: if the conversation's `contactName` differs from
`ContactDisplayName`, it is updated; `SetIsKnown(true)` ensures the conversation appears in the
player's Messages; the message is delivered via `MessagingManager.Instance.ReceiveMessage` with
`Message.ESenderType.Other`.

---

## CustomerNotifier.cs

Sends the "you don't stock an effect I want" complaint texts and reduced-deal notifications. Driven
daily (spread across the day) by `CustomerDailyNotificationPatch`. Four public entry points:
- `NotifyPlayerProductsNotSuitable` — player's listing doesn't cover effects
- `NotifyDealerNotSuitable` — dealer's listing doesn't cover effects
- `NotifyPlayerReducedDeal` — player sold a substitute at reduced price
- `NotifyDealerReducedDeal` — dealer sold a substitute at reduced price

**Cooldown logic (`ReadyToNotify`)**: uses the `CustomerNotificationState` MonoBehaviour attached to
the customer's GameObject. The cooldown is measured in real playtime seconds:
`TimeManager.Instance.Playtime - state.LastNotification < 60 * NotificationCooldownInMinutes`.
On first notification, `AddComponent` attaches the state. On each ready-to-notify call, the timestamp
is updated immediately so the duplicate-send problem cannot occur.

**Template filling**: `##DESIRES##` is replaced with `ProductHelper.FormatDesires(customer.CustomerData)`;
`##DEALER##` is replaced with `customer.AssignedDealer.FirstName` (only for dealer templates).

---

## DealerCoverageNotifier.cs

Texts coverage breakdowns via `LithiumContact` for two groups:

**`ReportForDealer(Dealer)`**: for each serveable assigned customer, computes which desired effects
the dealer's current stocked effects satisfy. A customer is "covered" if at least one desired effect
is stocked (or if they have no desires). Reports: `X/Y customers covered (Z%)`, then lists uncovered
customer names and the missing effects.

**No-dealer customers** (player serves directly, covered by `ProductManager.ListedProducts`):
- `_knownNoDealerCovered` snapshot prevents duplicate texts when `SetProductListed` fires twice on
  the host (local call + RPC path): `ReportNoDealerChange` only texts when the covered set actually
  changed.
- `ResetNoDealer()` drops the cached snapshot on save/scene load so it re-baselines for the new
  game state.
- `ReportNoDealerCustomers()` (startup): always sends the full picture and (re)baselines the snapshot.
- `ReportNoDealerChange()` (listing change): only texts if the covered set has changed.

---

## ProductCoverageNotifier.cs

Tracks which customers are "covered" (at least one listed product matches their desired effects) and
texts the player when a product listing change newly covers or uncovers customers.

**Deduplication**: `SetProductListed` fires twice on the host (local call + RPC path). A single
`_knownCovered` snapshot is kept rather than diffing per call: the second invocation sees no further
change and stays silent. Reset on save load via `Reset()`.

**`EnsureBaseline()`**: must be called before a listing change (by the listing patch) to snapshot
current coverage; uses `??=` so it only runs once per save.

**`ReportChange()`**: if no baseline exists (e.g. `EnsureBaseline` didn't run), silently adopts
current state. Otherwise computes newly covered and newly uncovered customer names, advances the
snapshot, and sends one text per direction (covered / uncovered). Overall coverage percentage and
optional uncovered-names suffix are included.

---

## LithiumStartupReport.cs

Sends a single compact "welcome back" text from the Lithium contact after a save loads.

**`WorldReady()`**: checks that `MessagingManager.Instance != null` and
`Customer.UnlockedCustomers.Count > 0` — the minimum needed to send messages and know who the
customers are.

**`Send()`**: server-only (`InstanceFinder.IsServer` guard). Counts: dealers on payroll (recruited),
unlocked customers, listed products. Computes effect coverage percentage against the coverable
population (customers with at least one `PreferredProperty`). Message format:
`"Welcome back. X dealers on payroll, Y customers unlocked, Z products listed. Effect coverage: P% — ..."`

---

## NpcRosterDebug.cs

Debug helper (triggered from `Core.OnUpdate` via F7). Dumps every NPC with messaging/customer
status to `UserData/Lithium/NpcRoster.txt` so a suitable "unused" NPC can be chosen to host the
Lithium coverage-update contact. Lines prefixed `**` are hijack candidates: not a customer, has a
`MSGConversation`, and has zero message history.

Output columns: `fullName`, `type` (Il2Cpp type name), `id`, `customer`, `conv`, `msgs`,
`important`, `region`.

---

## OrderPatternDebug.cs

Debug helper (triggered from `Core.OnUpdate` via F6). Dumps every customer's order-pattern profile to
`UserData/Lithium/OrderPatternsDump.txt`.

Header includes: module enabled flags, current XP vs. required, whether patterns are actually active
in-game, current day and elapsed days.

Per-customer block includes:
- `CustomerData.name` (the seed source for the stable hash), seed value
- `MinOrdersPerWeek / MaxOrdersPerWeek`, reference value (midpoint, clamped to 1–7)
- `WeeklySpend min/max`, `Standards`
- Desired effects, drug affinities (from `DefaultAffinityData.ProductAffinities`)
- Effect coverage of desires by the player's currently listed products: best-matching product name,
  how many desires it covers, and the resulting base acceptance percentage (before quality/drug
  adjustments) — the same coverage the sample-acceptance calc factors
- Archetype, order days, quantity multiplier, whether today is an order day
- Determinism check: creates the profile twice and confirms both `Archetype` and
  `string.Join(",", OrderDays)` match; logs "MISMATCH!" if they diverge

---

## Architecture/BonusPaymentHandler.cs

**`IBonusPaymentHandler`** interface — the extension point for custom bonus payments on contract
handovers. One method:

```csharp
bool TryCalculateBonus(Customer customer, Contract contract, List<ItemInstance> items,
                       out List<Contract.BonusPayment> boni);
```

Returns `true` (with `boni` populated) when the handler contributes a bonus, `false` (empty list)
otherwise. `CustomerProcessHandoverPatch` iterates all registered handlers and collects contributions.

---

## Architecture/OrderPatternProfile.cs

**Determinism requirement**: both `CustomerGetOrderDaysPatch` (frequency) and
`CustomerContractGenerationPatch` (quantity) rebuild this profile independently from the same inputs.
The `Create` method is deterministic: it consumes the seeded RNG in a fixed order and only reads
configured archetype weights (stable within a session), so both call sites always agree on the same
profile for a given customer name.

**Seed**: `StableHash.Compute(customerName)` → `new Random(seed)`.

**Reference orders per week**: midpoint of `MinOrdersPerWeek` and `MaxOrdersPerWeek`, clamped to
1–7.

**`PickArchetype`**: `WeightedPicker<OrderPatternArchetype>` with a fixed add order
(Weekly → TwiceWeekly → EveryThreeDays). Fixed add order is essential for determinism across call
sites — the pick consumes exactly one RNG draw regardless of weights. Negative weights are clamped
to 0.

**`BuildDays` per archetype**:
- `Weekly`: one random day in 0–6.
- `TwiceWeekly`: two well-separated days ~3–4 apart (`d1 = (d0 + 3 + rng.Next(0,2)) % 7`).
- `EveryThreeDays`: start in 0–2, then step by 3 within 0–6. Starting in 0–2 guarantees at least
  two days (start 2 → Wed/Sat, start 0 → Mon/Thu/Sun) so it never collapses into a weekly pattern.
- Fallback: if `BuildDays` returns empty (shouldn't happen), falls back to `(seed % 7)` as a single
  day.

**`QuantityMultiplier`**: `referenceOrdersPerWeek / orderDays.Count * BulkOrderSizeFactor`. Fewer
days → larger per-order quantity; conserves the customer's intended weekly volume. `BulkOrderSizeFactor`
is clamped at 0 at the call site.

**`DaysUntilNextOrder(today)`**: if today is the only order day, returns 7 (next week occurrence)
rather than 0 — they've just ordered.

**`IntervalFractionElapsed(weekPosition)`**: projects each order day across previous, current, and
next week (marks at `day - 7`, `day`, `day + 7`) to find the interval bracket containing
`weekPosition`. Used by `OfferTimingGate` for the off-schedule offer cooldown. `weekPosition` is a
continuous value: `(int)day + fractionOfDay` so the gate advances smoothly through the day.

**Cache**: keyed by `customerName`, cleared on save unload via `ModCustomers.Apply()`. Spans the
process lifetime between save loads so all call sites share the identical instance.

---

## Architecture/SaveSlotStore.cs

Generic `Dictionary<string, TValue>` persisted to
`UserData/Lithium/<folderName>/<saveKey>.json` via Newtonsoft.Json.

**Lazy load**: `EnsureLoaded()` resolves the save key via `SaveSlotKey.Resolve()` and reads the
file on first access. If `Resolve()` returns null (save folder not yet known), the load is deferred
to the next access without error. The save key is captured once and does not change mid-session.

**`Unload()`**: drops in-memory state on save load; does not touch disk, so other saves keep their
stored state. Called from `ModCustomers.Apply()` for both `ContractRetryTracker` and
`OfferDeadlineTracker`.

**`Persist()`**: writes on every `Set` and `Remove` call. Falls back to a fresh `Resolve()` if
`_currentSaveKey` is null.

Replaced two previously near-identical hand-rolled load/persist implementations.

---

## Architecture/SaveSlotKey.cs

Resolves a filename-safe, human-readable id for the currently loaded save: `"<slot> - <org>"`,
e.g. `"SaveGame_1 - Greenacre"`. Returns null until both `LoadManager.LoadedGameFolderPath` and
`ActiveSaveInfo.OrganisationName` are available, so the key never changes mid-session.
`Sanitize()` replaces `Path.GetInvalidFileNameChars()` characters with `_` since the organisation
name is player-entered and may contain `:`, `/`, etc.

---

## Architecture/ContractRetryTracker.cs

Remembers customers whose contract offer was refused or expired so they re-attempt an order the next
day instead of waiting for their next scheduled order day.

**Storage**: `SaveSlotStore<EDay>`, keyed by `customerName`. Stores the weekday of the retry (the
day after refusal), not an absolute date. A weekday is used because `GetOrderDays` (the schedule the
game consults) is itself weekday-based; the day is captured once at refusal time and stays put.

**`FlagForRetry`**: stores `(currentDay + 1) % 7`.

**`IsRetryDay`**: returns true only when the stored day equals today.

**`HasPendingRetry`**: returns the stored day without checking if it matches today.

State persisted to `UserData/Lithium/ContractRetries/<save>.json`.

---

## Architecture/OfferDeadlineTracker.cs

Remembers the absolute in-game minute (`GameDateTime.GetMinSum`) at which a pending contract offer
is allowed to expire — the deadline texted to the customer.

**Why persisted**: the native offer-expiry check is invisible in the IL2CPP proxy assemblies, so the
mod cannot rely on it honouring the extended window. `CustomerOfferDeadlinePatch` records the
deadline when the offer is made; the `ExpireOffer` guard uses it to keep the deal alive until the
deadline passes. Without persisting across save/reload, a reload would drop the deadline and the
restored offer would fall back to the game's shorter native expiry (the "promised Friday, cancelled
the same day" bug).

**Storage**: `SaveSlotStore<int>`, keyed by `customerName`, value is an absolute in-game minute.
Absolute minutes keep the same meaning across reloads of the same save.

State persisted to `UserData/Lithium/OfferDeadlines/<save>.json`.

---

## Architecture/OfferTimingGate.cs

Gates off-schedule, player-initiated in-person offers for bulk-pattern customers. Without this gate,
a player could walk up daily to a weekly-order customer and sell them fresh stock every day,
defeating the bulk-cadence design.

**`AcceptsOfferNow(customer, config)`**: returns true (no gating) when:
- `MinIntervalFractionBeforeOffer <= 0` (feature disabled)
- `Contracts.Enabled`, `OrderPatterns.Enabled`, or XP requirement are not met (mirrors the exact
  same gate that `CustomerGetOrderDaysPatch` and `CustomerContractGenerationPatch` use)
- Game state needed for timing is unavailable (null checks)

Otherwise builds the `OrderPatternProfile` and checks whether `IntervalFractionElapsed(weekPosition)
>= threshold`.

**`WeekPosition()`**: continuous position within the Mon–Sun week:
`(int)currentDay + minutesIntoDay / 1440f`, clamped to [0, 0.999]. Fractional so the gate advances
smoothly through the day rather than snapping at midnight.

---

## Architecture/OfferAcceptanceWindow.cs

Computes the offer acceptance window (in-game minutes) from the game's default window plus a
per-unit size bonus, scaled by `DurationMultiplier`.

**`AbsoluteMaxWindowMinutes = 6 * 1440`** (6 in-game days). The game retires offers that roll over
a full 7-day week; a 7-day window lands exactly on the rollover boundary and the deal is lost.
6 days is the safe ceiling.

**`Extend(currentWindowMinutes, quantity, config)`**:
1. Extra minutes = `max(0, (quantity - BaseQuantity) * MinutesPerExtraUnit)` (zero if at or below
   `BaseQuantity`).
2. Scaled = `round((currentWindowMinutes + extra) * DurationMultiplier)` — scales the whole window
   including any size bonus.
3. Cap = `min(MaxWindowMinutes, AbsoluteMaxWindowMinutes)`.
4. Result = `min(max(scaled, currentWindowMinutes), max(cap, currentWindowMinutes))` — never drops
   below the game's own default, never exceeds the cap (but cap is also not allowed to truncate
   below the game's default).

---

## Behaviours/CustomerNotificationState.cs

Minimal `MonoBehaviour` attached to a customer's `GameObject` to track the last notification
timestamp. Must be registered with `ClassInjector.RegisterTypeInIl2Cpp<CustomerNotificationState>()`
before use (done in `ModCustomers` constructor). `LastNotification` stores
`TimeManager.Instance.Playtime` at the time of the last outbound text. No notes beyond the above.

---

## BonusPayments/EffectCoverageBonus.cs

Default `IBonusPaymentHandler` implementation. Registered in `ModCustomers` constructor.

**Per-product-per-item logic**:
1. Resolves each `ItemInstance.ID` to a `ProductDefinition` in `DiscoveredProducts`.
2. Counts how many of the customer's desired effects (lowercased) appear in the product's `Properties`.
3. Selects the bonus bucket (`OneCoveredEffect`, `TwoCoveredEffects`, `ThreeCoveredEffects`) by
   match count (3+ all go to the three-effects bucket).
4. Fixed bonus = random from [FixedBonusMin, FixedBonusMax] × quantity.
5. Percent bonus = random % from [PercentageBonusMin, PercentageBonusMax] × contract.Payment ×
   (this item's quantity / total units across all items) — i.e. proportional delivery share of the
   contract payment.
6. Per-item bonuses sum into `totalBonusAmount`.

Returns a single `Contract.BonusPayment("Effect Match Bonus", totalBonusAmount)`.

`AffectsDealers = false` in config causes the handler to skip dealer contracts entirely.

If `desires.Count == 0` or no items match, returns false (no bonus).

IL2CPP note: `ProductManager.DiscoveredProducts.ToList()` is required before LINQ. `min/max` is
applied to the bucket's `Min/Max` fields before `Random.Range` to tolerate a user accidentally
reversing them in JSON.
