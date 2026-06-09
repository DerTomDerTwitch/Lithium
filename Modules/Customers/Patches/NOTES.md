# Modules/Customers/Patches — Developer Notes

All notes extracted from inline comments prior to comment-stripping. One section per file.

---

## CustomerContractGenerationPatch.cs

**Patches:** `Customer.TryGenerateContract` — **Postfix**

### Purpose
Replaces the game's default contract generation with effect-coverage-based product selection, budget-sized order quantities, and order-pattern support.

### Idempotency guard (`_lastWritten` dictionary)
`TryGenerateContract`'s postfix fires more than once for the same pending contract: the game reprocesses/re-offers the customer's offer, which caused the bulk multiplier to compound (e.g. 7 → 35 → 185) because each re-fire read the already-scaled quantity as the new base.

`ContractInfo` is `[Serializable]` and is re-offered through the networked `SetOfferedContract` RPC (and round-trips through save/load), so each re-offer hands a freshly-deserialized object with a **new native pointer** but the same scaled quantity — a pointer-keyed guard misses it and re-scales. The guard is instead keyed on the **customer name** (a customer has exactly one pending offer). On entry the incoming contract is compared to the stored fingerprint: an exact match means we are seeing our own already-scaled output again (whatever its pointer), so it is left untouched; a mismatch is a genuine fresh roll and is scaled normally. Pointer-independent, so it survives the RPC round-trip and re-offers.

### Fingerprint format
Stable signature of product ID + quantity + quality for each entry. Payment is deliberately excluded: it is a float that may not survive the `SetOfferedContract` RPC / save round-trip bit-for-bit, and the integer product quantities alone already separate a fresh (unscaled) roll from scaled output — the only distinction the guard needs to make.

### Exception wrapper
A managed exception escaping a Harmony postfix into IL2CPP native code can escalate into a process-killing native crash (no managed stack trace). The outer `try/catch` logs and leaves the game's own contract untouched so play continues.

### Dealer vs. player suppression
When `dealer == null` and `__instance.AssignedDealer != null`, the offer is suppressed (`__result = null`). A non-null `dealer` parameter means the game is generating the contract the dealer will fulfil; null means the offer is being presented to the **player**. Without this guard the dealer branch composed the player's offer from the dealer's stock (wrong product).

### XP gate
If `LevelManager.Instance.TotalXP < config.Contracts.XPRequired`, the patch returns early and leaves the game's contract untouched.

### Retry day
If `config.Contracts.RetryNextDayOnRefusal` is enabled and `ContractRetryTracker.IsRetryDay` returns true, the customer re-attempts today even if it is not one of their normal order days. The flag is cleared once a fresh offer is handed to them.

### Budget-based order sizing (`ComputeOrderBudget`)
Instead of multiplying the game's rolled quantity (which compounded across re-offers), the order is sized from the customer's own wallet: relationship-adjusted weekly spend divided across the days their pattern actually orders on. A weekly customer (1 order day) spends the full week's budget in one order ("x7 intuition"), while a twice-weekly customer spends half per order. Unit count = budget / price, so order *value* tracks the budget regardless of product and cannot compound. `-1f` returned when order patterns are off → keeps the game's own quantity.

### `MaxOrderQuantity = 9999`
Hard ceiling on a single order's unit count — a backstop so a near-zero product price can't turn a budget into an absurd quantity. Real orders never approach this.

### Dealer stock cap
For dealer-fulfilled orders, available quantity is capped by what the dealer actually holds for each product. For direct (player) sales, available quantity is `int.MaxValue`.

### Effect-match requirement toggle (`RequireEffectMatch` / `DealerRequireEffectMatch`)
Decouples bulk/order-pattern sizing from the hard effect-coverage requirement. Both default `true` (original behaviour) and are independent — `RequireEffectMatch` governs player-listed contracts, `DealerRequireEffectMatch` governs dealer-fulfilled ones, so the requirement can be enforced on one path but not the other.

- **`true`** (hard requirement): the candidate pool is only the products whose effects intersect the customer's desires; if that set is empty the order falls back to the reduced-price substitute deal (see below).
- **`false`** (soft preference): the candidate pool is the *entire* available stock (all listed products / all the dealer's products) and `ComposeMatchingOrder` runs on it directly at full price — no reduced-substitute path, so no `ReducedDealPriceMultiplier` discount and no "bought reduced" notification. Coverage still steers the pick via `PickWeightedByCoverage` (covered-effect weighting plus drug-type bias), so matching products are still strongly preferred; they are just no longer mandatory. This lets `Contracts.Enabled` (and thus budget-based bulk sizing) run without forcing the substitute-discount behaviour.

### Reduced-deal path
When nothing in the available stock matches the customer's desired effects (and the relevant `RequireEffectMatch` flag is on), a random substitute product is chosen and `ApplyReducedDeal` sizes the order and sets payment. The unit price is the player's **listed price** (`ProductManager.GetPrice`) when the relevant `SellAtListedPrice` / `DealerSellAtListedPrice` flag is set, falling back to `MarketValue` otherwise — matching the matched-order path so the dealer's cut isn't undervalued. Quantity is sized off that full unit price (to avoid ballooning the count from the discount), then `config.Contracts.ReducedDealPriceMultiplier` is applied exactly once to the unit price to get the reduced payment. The chosen product, final quantity, reduced per-unit price and total are handed to `CustomerNotifier` so the substitute-purchase message can spell out what was bought and for how much. Bonus handlers still run at handover, but the effect-coverage bonus is naturally zero.

### `ComposeMatchingOrder`
Picks a primary product weighted by coverage^`CoverageBiasExponent`, optionally a different secondary product taking a configured share of the order (`SecondProductQuantityShare`). Each product's quantity is sized from its own slice of the per-order budget at its own unit price. If the secondary quantity or primary quantity comes out ≤ 0 (e.g. no stock for the secondary), falls back to a single-product order. Payment is computed at the same per-unit prices used to size the order.

### `PickWeightedByCoverage`
Each candidate's weight is `(coveredEffects + 1)^CoverageBiasExponent * drugTypeFactor^DrugTypeBiasExponent`. The `+1` base keeps a zero-coverage product selectable — required when `RequireEffectMatch` is off and the pool is the full stock — while higher coverage is still strongly favoured. `drugTypeFactor` is the customer's affinity for the product's drug type (`ProductHelper.DrugTypeAffinity`, roughly [-1,1]) mapped to [0,1] via `(affinity + 1) / 2`, floored at 0.0001 so a disliked drug type is deprioritised but never strictly impossible; `DrugTypeBiasExponent = 0` neutralises the drug-type dimension entirely. If total weight ≤ 0, falls back to uniform random. `customerData` is threaded in from `Generate` to read the affinities.

### `_lastOrderDay` dictionary — waiting-phase guard
Keyed by customer name, stores the absolute `TimeManager.ElapsedDays` value on which the customer was last issued an order. An order-pattern customer takes one bulk order per scheduled order day then waits. The guard sits after the reprocess guard (same-fingerprint check), so a hit here is a genuinely new roll the native cadence kicked off right after the previous deal completed (the deal cooldown is far shorter than the compressed weekly order day). In that case `__result` is set to `null` to defer the customer until their next order day. After writing a new offer, the current elapsed day is stamped into `_lastOrderDay` so subsequent same-day rolls are deferred.

### `RememberWritten` map bound
One entry per customer; naturally bounded by the roster. The `> 4096` backstop is a safety ceiling. Cleared outright on save unload via `ResetState()`.

### `UnitPrice`
Returns the player's listed price (via `ProductManager.GetPrice`) when `useListedPrice` is true, otherwise the game's per-unit roll (its total payment divided by the quantity it rolled). Floored at $0.01.

---

## CustomerOfferDeadlinePatch.cs

**Patches:** `Customer.SetOfferedContract` — **Postfix**

### Purpose
Gives larger orders a longer acceptance window. `ExpiresAfter` is what the game's `UpdateOfferExpiry` counts against; extending it makes deal-acceptance expiry honour the bigger window. The deadline shown to the player is handled by `CustomerOfferDeadlineMessagePatch`.

### Anchor to NOW, not `OfferedContractTime`
For weekly/scheduled orders, `OfferedContractTime` is stamped when the order is *scheduled* (days before it surfaces), not when it is shown. Adding the window to that stale timestamp collapsed it — e.g. a Sunday order whose `OfferedContractTime` was the previous Monday got a deadline of (Monday + 7 days) ≈ this Monday, so the guard let it expire on Monday rollover even though the text promised "Sunday (7 days)". Anchoring to `now` is also reload-safe: if a save load re-runs `SetOfferedContract`, the deadline is recorded relative to the restored "now", which only ever lands at or after the original promise.

### Why `ExpiresAfter` is NOT widened
`ContractInfo.ExpiresAfter` is read by the phone's deal-acceptance flow to build the `DealWindowSelector` ("schedule a time"). With an inflated (multi-hour/day) `ExpiresAfter` the accept handler silently refuses to open the time picker — bisected in-game: vanilla `ExpiresAfter` → picker opens; widened → dead button. The extended window is enforced solely by `OfferDeadlineTracker` + `CustomerExpireOfferGuardPatch`.

### Re-anchoring `OfferedContractTime`
Setting `OfferedContractTime = TimeManager.Instance.GetDateTime()` (same as `CustomerLoadOfferDeadlinePatch` does for save-restored offers) keeps the native deadline (`OfferedContractTime + ExpiresAfter`) in the future, preventing the deal picker from finding no valid windows.

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true.

---

## CustomerExpireOfferGuardPatch.cs

**Patches:** `Customer.ExpireOffer` — **Prefix**

### Purpose
Authoritative enforcement of the acceptance window. The game's native offer-expiry check was cancelling large bulk offers on the vanilla window even though the customer texted a later deadline. Rather than guess which native field that check reads, `ExpireOffer` itself is gated: while the `OfferDeadlineTracker` deadline hasn't elapsed, expiry (and its "nvm" text) is suppressed.

### `LastCallBlocked` static flag
Set by the prefix so `CustomerExpireOfferPatch`'s postfix does not flag a next-day retry for a call that was suppressed (Harmony still runs postfixes when a prefix returns `false`). Prefixes always run before postfixes, so the flag is current by the time that postfix reads it.

### Why `Customer.OFFER_EXPIRY_TIME_MINS` is NOT modified
A former patch modified that global as a redundant safety net, but the cartel-beta `DealerManagementApp.AssignCustomer` reads it and **hard-crashes** (native, no managed trace) on the inflated value. This guard plus per-contract tracking enforces the window without touching the global.

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true. Offers/expiry state is server-authoritative; clients receive the result via RPC.

---

## CustomerMinPassOfferExpiryPatch.cs

**Patches:** `Customer.OnMinPass` — **Prefix** (see the file's XML doc for the inlining rationale)

### Stale-entry sweep (no pending offer)
When a customer has no live expiring offer but a tracked entry whose deadline has lapsed, the entry is removed. This is the universal cleanup for consumption paths with no reliable patch chokepoint — found in audit: a **customer-refused counter-offer** (`RpcLogic___ProcessCounterOfferServerSide`, rejected branch) nulls `OfferedContractInfo` inline with no `ExpireOffer`/`ContractRejected` call; also covers an accept/reject postfix the native build might inline past, and entries left by pre-fix saves. `OnMinPass` is un-inlinable, so every leak is removed within one game minute of its deadline passing — always before the customer's next offer could inherit it (the instant-cancel bug).

### Why only LAPSED entries are swept
An entry with a still-future deadline and no offer is exactly the signature `GhostOfferRegenerationPatch` uses to detect a ghost and re-issue the offer when the player opens the conversation — sweeping it would silently disable ghost repair. A lapsed entry is unambiguously dead in both interpretations (a ghost past its window is dropped, not regenerated), so lapse is the safe sweep criterion. Residual: a stale *future* entry (e.g. accept-clear inlined past) lives until its deadline passes; the worst it can do meanwhile is lend a later offer a longer-than-promised window — it can never instant-cancel.

### Tracker entry lifecycle (audit summary)
Entry **created**: `SetOfferedContract` postfix (inline-prone), `NotifyPlayerOfContract` announce repair, `OnMinPass` self-heal, `Customer.Load` restore. Entry **cleared**: deadline expiry through the `ExpireOffer` guard, player accept (`ContractAccepted` postfix), player reject (`ContractRejected` postfix), ghost-conversation cleanup, the lapsed-entry sweep here, and `Unload()` on save unload. Counter-offer accepted keeps the same pending offer (entry stays valid until [Schedule Deal] → accept). `DealerManagementApp.AssignCustomer`'s direct `ExpireOffer()` is blocked by the guard while the deadline is live — the offer (and entry) then run to their normal deadline.

---

## CustomerLoadOfferDeadlinePatch.cs

**Patches:** `Customer.Load(CustomerData)` — **Postfix**

### Purpose
Preserves a pending contract offer across a savegame load.

### Problem it solves
`Customer.Load` writes `OfferedContractInfo` / `OfferedContractTime` straight from save data **without** going through `SetOfferedContract`, so `CustomerOfferDeadlinePatch` never runs on load and no deadline is recorded for the session. The restored `OfferedContractTime` is also stale (for weekly/scheduled orders it is the moment the order was *scheduled*, days before it surfaced), so the native expiry check fires `ExpireOffer` the instant the save loads. With no tracked deadline, the guard cannot recognise the offer, so the deal is cancelled with the "nvm" text — the punishing loss of weekly orders.

### Fix
After `Load` restores an expiring offer:
- If a deadline already survived the reload, honour it exactly — **never extend on reload** so a player cannot farm a fresh window by quitting and loading.
- Otherwise (common case — restored without `SetOfferedContract`), grant the full promised window from the load moment.
- Then re-anchor `OfferedContractTime` to "now" (but do NOT widen `ContractInfo.ExpiresAfter`) so the native expiry check fires at the real deadline rather than immediately.

### Why `ExpiresAfter` is NOT widened here
Same reason as `CustomerOfferDeadlinePatch`: inflated `ExpiresAfter` makes the deal-acceptance time picker silently refuse to open (bisected in-game). Re-anchoring `OfferedContractTime` alone already keeps the native deadline in the future; the longer window is held open by `OfferDeadlineTracker` + `ExpireOffer` guard.

### Grace window for lapsed restored deadlines (`GraceMinutes = 180`)
A save can carry a live offer whose tracked deadline already lies in the past: the wake-save race (`RpcLogic___OnTimeSkip_Client` fires the wake `onMinutePass` staggered, and `SaveManager.Save()` can run before the customer's tick expires the offer), or simply a save from before `OfferDeadlineTimeSkipPatch` existed. Honoring such a deadline "exactly" meant the session's first minute tick expired the offer with a bare "nvm" — at day start, with the offer text buried in the previous day's history (the second Kyle report). Instead, a short grace (180 game-minutes, capped at the promised window) is granted so the player can respond. Deliberately far below the full window, preserving the anti-farm intent of the never-extend-on-reload rule.

---

## OfferDeadlineTimeSkipPatch.cs

**Patches:** `TimeManager.OnTimeSkip_Client(int, int)` — **Prefix**

### Purpose
Freezes acceptance windows across time skips (sleep, story skips). The window measures the player's *decision time*; a sleep jumps the clock straight past any deadline in the slept-over night. `RpcLogic___OnTimeSkip_Client` sets the new time, increments the day, then fires `onMinutePass` once — so the first wake tick of `CustomerMinPassOfferExpiryPatch` saw `now >= deadline` and expired the offer, delivering a bare "nvm" as the first message of the day (second Kyle report: evening offer, "tomorrow 3 AM" deadline, cancelled at day start).

### Mechanism
Prefix (runs before the native body, hence before the wake `onMinutePass`) shifts every live tracked deadline forward by the skipped minutes. Deadlines already lapsed *before* the skip are left alone — they expired fairly while the player was awake. The shift equals exactly the skipped time, so there is no free extension. Same chokepoint as ElectricBill's `TimeSkipBillingPatch` (proven patchable, not inlined).

### Modular skip count, not the game's
The skipped count is `((minSum(new) - minSum(old)) % 1440 + 1440) % 1440` — the game's own `Mathf.Abs` diff overstates a wrapped skip (22:00 → 7:00 gives 900 instead of the real 540), and tracker deadlines are absolute min-sums (`elapsedDays * 1440 + minute-of-day`).

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true (the wrapper only executes on the host; pure clients go through the RPC reader, which this patch does not touch — tracker state is server-owned anyway).

---

## CustomerExpireOfferPatch.cs

**Patches:** `Customer.ExpireOffer` — **Postfix**

### Purpose
A contract offer expired without the player responding ("did not go through"). Treats it like a refusal: flags the customer to re-attempt an order the next day via `ContractRetryTracker.FlagForRetry`.

### Guard against suppressed calls
Checks `CustomerExpireOfferGuardPatch.LastCallBlocked` before acting. If the guard suppressed the expiry (acceptance window hasn't elapsed), the offer is still live and must not be treated as a refusal/retry.

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true. Scheduling is server-authoritative; only the server owns the retry bookkeeping.

---

## CustomerBulkRewardPatch.cs

**Patches:** `Customer.ProcessHandoverServerSide` — **Prefix + Postfix**

### Purpose
Bulk orders consolidate several normal orders into a single delivery. Vanilla grants the same per-deal affection and XP regardless of order size, so a weekly order worth ~7 daily orders would pay out like one deal. This patch measures the affection and XP the game awards for this handover and adds the remainder, so the total scales with the order's quantity multiplier (a 7x-volume order → ~7x reward).

### Why "measure + top up" rather than granting a constant
Measuring the game's own award keeps the bonus exactly multiplier-times the real reward whatever the game's internal formula is, and makes a duplicate `ProcessHandoverServerSide` fire harmless — the second pass measures a ~0 gain and tops up nothing.

### `_armed` flag
Prefix sets `_armed = true` only when all conditions pass (outcome == `Finalize`, server, order patterns enabled, XP gate passed, multiplier > 1). Postfix is a no-op if `_armed` is false.

### `extra = _effectiveMultiplier - 1f`
The game already paid one order's worth of reward. "extra" covers the remaining orders the bulk delivery stands in for, so `base + base * extra = base * multiplier`.

### Multiplier cap
`_effectiveMultiplier` is clamped to `bulk.MaxRewardMultiplier` (configured ceiling).

### Condition to run
Gated by `config.OrderPatterns.Enabled` and `LevelManager.Instance.TotalXP >= config.Contracts.XPRequired`, identical to the gate `CustomerContractGenerationPatch` and `CustomerGetOrderDaysPatch` use, so the reward scaling only activates when the order size reshaping is also active.

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true.

---

## CustomerContractRejectedPatch.cs

**Patches:** `Customer.ContractRejected` — **Postfix**

### Purpose
The player refused a contract offer (declined it in the Messages app). Clears the `OfferDeadlineTracker` entry (the offer is consumed — see the stale-deadline bug under `CustomerContractAcceptedPatch`), then flags the customer so they re-attempt an order the next day instead of waiting for their next scheduled order day, via `ContractRetryTracker.FlagForRetry` (only when `RetryNextDayOnRefusal` is on; the tracker clear is unconditional).

### Inline safety
`ContractRejected` is delegate-bound (it is registered as a message-response `callback`), so it cannot be inlined away — a reliable patch point.

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true. Scheduling is server-authoritative.

---

## CustomerContractAcceptedPatch.cs

**Patches:** `Customer.ContractAccepted(EDealWindow, bool, Dealer)` — **Postfix**

### Purpose
Clears the `OfferDeadlineTracker` entry once an offer is consumed by acceptance. Both the player accept path (`SendContractAccepted` RPC → `ContractAccepted`) and the dealer path route through this method.

### The stale-deadline bug this fixes
The tracker (persisted per save slot) was only ever cleared when an expiry actually passed through `CustomerExpireOfferGuardPatch` — never on accept/reject. A leftover entry from a consumed offer has a deadline in the past. When the customer's NEXT offer arrived days later and the inline-prone `Customer.SetOfferedContract` patch (`CustomerOfferDeadlinePatch`) failed to overwrite the entry, `CustomerMinPassOfferExpiryPatch` found the stale elapsed deadline on the next minute tick — its self-heal only engages when NO entry exists — and expired the brand-new offer itself; the guard saw `now >= deadline` and let it through. Symptom: the offer text and the "nvm" cancellation arrive in the same moment (reported with Kyle, offer ~3 PM, promised deadline next-day 3 AM).

### Defense in depth
Three legs close the hole: clear on accept (this patch), clear on reject (`CustomerContractRejectedPatch`), and announce-time repair (`CustomerOfferDeadlineMessagePatch.RepairTrackedDeadline`) — the last one fixes the instant-cancel even if this postfix is ever inlined past, since `NotifyPlayerOfContract` demonstrably fires (it renders the deadline text).

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true.

---

## CustomerDailyNotificationPatch.cs

**Patches:** `TimeManager.PassMinute` — **Postfix**

### Purpose
Sends each customer a daily complaint text when neither the player's listed products nor their assigned dealer offer any of the effects the customer wants — every day, not only on the days the game would have generated an order.

### Spread across daytime hours
The exact time of day is derived deterministically from the customer's name (the same `StableHash` used for order patterns), so the texts are spread across the active daytime hours instead of all arriving simultaneously. `PassMinute` fires once per in-game minute; each customer's slot is matched exactly, so it fires at most once per day. The window sits in daytime hours so sleeping (which skips minutes at night) does not cause missed days.

### Only nudges when nothing is available
Only triggers when there is nothing at all to sell. If something is available (even without the wanted effect), the customer does a reduced-substitute deal which sends its own "bought reduced" text from `CustomerContractGenerationPatch` — the daily nudge would otherwise contradict it.

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true (mirrors contract generation so the host doesn't double-send).

### XP gate
Same `XPRequired` gate as the contract system.

---

## CustomerSleepOfferGuardPatch.cs

**Patches:** `Customer.OnSleepStart` — **Prefix + Postfix**

### Purpose
Keeps a pending offer alive across sleep. The acceptance-window deadline is only enforced on the awake, per-minute path (`OnMinPass → UpdateOfferExpiry → ExpireOffer`, which `CustomerExpireOfferGuardPatch` suppresses). Sleeping never runs that loop: `TimeManager.StartSleep` stops the per-minute tick (`_stopMinPassWait`) and jumps the clock with `SkipForwardToTime`, so `OnSleepStart` withdraws the offer directly (clearing `OfferedContractInfo`) without ever calling the guarded `ExpireOffer` — which is why a deal the player still had "days" left on vanished overnight.

### Mechanism
Prefix snapshots a live offer whose promised deadline hasn't elapsed; postfix restores it if `OnSleepStart` cleared it. Purely additive — if a future code path withdraws via the (already guarded) `ExpireOffer`, `OfferedContractInfo` stays non-null and the postfix is a no-op.

### Thread-safety note on the static snapshot
`OnSleepStart` runs to completion per customer (prefix → original → postfix) on the main thread before the next customer's, so a single static snapshot is safe — mirrors `CustomerExpireOfferGuardPatch`.

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true.

---

## CustomerOfferDeadlineMessagePatch.cs

**Patches:** `Customer.NotifyPlayerOfContract` — **Prefix**

### Purpose
Appends the acceptance deadline as the final bubble of the contract offer message itself, so the player sees how long they have right inside the deal — after the details, not as a separate text.

### Why append to `MessageChain`, not send a new message
A separate message renders immediately and lands BEFORE the deferred deal chain. Also, `NotifyPlayerOfContract` can fire more than once per offer, which sent the text twice. Appending to the chain with an idempotency guard fixes both — the line travels with the deal and is added only once.

### Template selection
A deterministic template index (stable per offer) is computed so the duplicate guard can match the exact line on a repeated `NotifyPlayerOfContract` call.

### Deadline derivation
`contract.ExpiresAfter` is not populated yet when the offer message is built, so the acceptance window is derived from `Customer.OFFER_EXPIRY_TIME_MINS` and the same large-order extension logic as `CustomerOfferDeadlinePatch` (`OfferAcceptanceWindow.Extend`).

### `RepairTrackedDeadline` — announce-time tracker repair
`NotifyPlayerOfContract` is the one per-offer hook that demonstrably fires (the deadline text it appends shows up in-game), so it doubles as the authoritative repair point for the `OfferDeadlineTracker`: if no entry exists for the customer, or the existing entry has already elapsed (a stale leftover from a consumed offer — see `CustomerContractAcceptedPatch`), the freshly computed deadline is written. A live (future) entry is never touched, so multi-fires of `NotifyPlayerOfContract` cannot extend a window. This also keeps the promised text and the enforced deadline in agreement when `CustomerOfferDeadlinePatch` (inline-prone `SetOfferedContract`) didn't fire. Server-gated; runs even when `SendDeadlineMessage` is off (the message-only guards come after it).

### `EDay` convention
`EDay` is Monday = 0 .. Sunday = 6, matching the `DayNames` array.

### `FormatDeadline` rendering
- `dayDelta ≤ 0` → "today, {time}"
- `dayDelta == 1` → "tomorrow, {time}"
- `dayDelta < 7` → "{day name}, {time}"
- `dayDelta ≥ 7` → "{day name} ({dayDelta} days), {time}"

### `Format12Hour`
Input `hhmm` is a 24-hour HHMM integer (e.g. 1430 → "2:30 PM", 0 → "12:00 AM").

---

## CustomerProcessHandoverPatch.cs

**Patches:** `Customer.ProcessHandoverServerSide` — **Prefix**

### Purpose
Iterates all registered `IBonusPaymentHandler`s (e.g. `EffectCoverageBonus`, `EffectComboBonus`) and adds any bonus payment amounts to `totalPayment`.

### Server-authoritative
Bonus handlers roll random amounts. Only the server may apply them; pure clients skip and receive the networked payout. On a host, `IsServer` is true.

### Config gate
Gated by `config.EffectBonus.Enabled`. If `!handoverByPlayer && !config.EffectBonus.AffectsDealers`, dealers' handovers are skipped.

---

## CustomerSampleConsumedPatch.cs

**Patches:** `Customer.GetSampleSuccess` — **Prefix** (replaces original)

### Purpose
Replaces the game's sample-acceptance calculation with `SuccessChanceCalculator.CalculateSuccess`, which considers drug type, quality, quality-level modifier, customer standards, desired effects, product effects, drug affinity, base acceptance, effect-match requirement, max quality delivery levels, and drug affinity sharpness. Returns the average success chance across all offered items.

### No notes
No significant inline comments beyond the patch structure itself.

---

## CustomerOfferSuccessPatch.cs

**Patches:** `Customer.GetOfferSuccessChance` — **Prefix** (partially replaces)

### Purpose
Layers three additional checks over the native direct-sale acceptance maths:
1. **Off-schedule timing gate** (`OfferTimingGate.AcceptsOfferNow`): a bulk-pattern customer refuses unsolicited offers until enough of the wait for their next scheduled order has passed, preventing the player from selling extra product every day and bypassing the bulk cadence.
2. **Effect match**: if the customer wants effects and none of the offered products carry any of them, the offer is rejected outright (no quality/price fallback), matching the hard requirement contracts use.
3. **Price tolerance**: the perceived asking price is divided by `PriceToleranceMultiplier` (with optional per-day/per-customer jitter) before the native curve sees it. This shifts the ~1.6x-of-value "no drawback" ceiling. The actual money paid is unaffected; only this success-chance computation reads the adjusted price.

### `ApplyPriceTolerance`
Divides the perceived price by the effective tolerance. A tolerance > 1 makes the customer more forgiving of markup; < 1 is stricter. If `baseMultiplier ≤ 0`, the price is left untouched (misconfiguration safety).

### `DeterministicJitter`
FNV-1a hash over NPC id + elapsed-day count. Reproducible across sessions (unlike runtime-randomised `string.GetHashCode`). Converts the hash to a float in [-range, +range]:
- `(hash & 0xFFFFFF) / 0x1000000` → [0, 1)
- `unit * 2f - 1f) * range` → [-range, +range]

---

## ProductListingPatch.cs

**Patches:** `ProductManager.SetProductListed(string, bool)` — **Prefix + Postfix**

### Purpose
When the player lists or delists a product, recomputes customer coverage and texts the player (via the Lithium contact) which customers became covered/uncovered, plus the overall coverage percentage.

### Mechanism
Prefix snapshots coverage before the change (`ProductCoverageNotifier.EnsureBaseline`); postfix diffs against the post-change state (`ProductCoverageNotifier.ReportChange`). Also calls `DealerCoverageNotifier.ReportNoDealerChange` if `NotifyNoDealerCustomers` is configured.

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true.

---

## ContactsPanelOrderPatternPatch.cs

**Patches:** `ContactsDetailPanel.Open` — **Postfix**

### Purpose
Appends the customer's order pattern (days + cadence label) and drug-type affinities to the phone Contacts customer panel, right where desires/spending are listed. `Open()` repopulates labels each time it runs, so appending in a postfix is safe and never compounds — guarded by a `"\nOrders: "` marker string to prevent double-appending if `Open()` fires more than once.

### Affinity display
Per-drug-type affinity shown as an independent signed percentage (`affinity * 100`). Positive = liked, negative = disliked; values are not a distribution and don't sum to 100%. Deliberately kept to a single compact line: the detail panel body has a bounded height budget (the game itself caps its most-purchased list for the same reason), and adding one row per drug type pushes the body past that budget, shoving the name header out of the panel's visible area.

### Pattern visibility gate
The displayed pattern is only shown when order patterns are actually reshaping the schedule (XP gate, same as `CustomerGetOrderDaysPatch`). Also mirrors the desires section's visibility — does not reveal a pattern where the panel itself hides customer details (e.g. a locked customer).

### `EDay` convention
`EDay` is Monday = 0 .. Sunday = 6, matching the `DayAbbr` array.

---

## CustomerGetOrderDaysPatch.cs

**Patches:** `CustomerData.GetOrderDays` — **Postfix**

### Purpose
Rewrites the game's order-day schedule with the order-pattern profile for this customer (via `OrderPatternProfile.Create`), and injects the retry day for customers who had an offer refused/expired.

### Gating
Order-pattern reshaping is gated by `config.OrderPatterns.Enabled` **and** `LevelManager.Instance.TotalXP >= config.Contracts.XPRequired`, identical to the gate `CustomerContractGenerationPatch` uses. This ensures frequency reshaping and quantity scaling switch on together — sub-XP customers must not get fewer order days without the matching volume conservation.

### Retry-day injection
Runs independently of order patterns so the retry works regardless of whether patterns are enabled. `ContractRetryTracker.HasPendingRetry` returns the retry day if pending; it is added to the schedule if not already present. `CustomerContractGenerationPatch` clears the flag once the customer actually gets a fresh offer.

---

## DealerInventoryClosePatch.cs

**Patches:** `Dealer.TradeItemsDone` — **Postfix**

### Purpose
When the player closes a dealer's in-person inventory, texts (via the Lithium contact) which of that dealer's assigned customers' desired effects their stock fails to cover. `TradeItemsDone` is the dealer's "finished trading" callback, so the inventory is already updated by the time it runs.

### Server-authoritative
Only runs when `InstanceFinder.IsServer` is true.

---

## GhostOfferRegenerationPatch.cs

**Patches:** `MessagesApp.SetCurrentConversation` — **Postfix**

### Purpose
Repairs "ghost offers". A ghost is a customer whose pending offer object (`OfferedContractInfo`) was lost via an abnormal path (withdrawal / save-load edge case) BEFORE its acceptance deadline, while the conversation still renders dead Accept/Counter/Reject buttons and Lithium still holds a tracked deadline. Pressing Accept on such a ghost does nothing because there is no offer object behind the buttons (confirmed in-game: `OfferedContractInfo == null`, `CurrentContract == null`, but a future `OfferDeadlineTracker` entry, and the buttons still show).

### When the player opens a ghost customer's conversation
If the acceptance window has not elapsed: clear the stale tracker, re-issue a fresh offer via `customer.ForceDealOffer()` (the regenerated offer records its own deadline through `CustomerOfferDeadlinePatch`). If the window has elapsed: the offer lapsed legitimately — drop the stale tracker entry (leak cleanup) without fabricating a new offer.

### Double-dispatch dedup guard (`_lastName`, `_lastMin`)
`SetCurrentConversation` is invoked twice per open (host-side double dispatch). Without the guard, both passes would each force an offer. Keyed by customer name + in-game minute so a genuine later re-open is still allowed.

### `FindCustomerByNpc`
Resolves the `Customer` that owns a conversation from its sender NPC by matching IL2CPP native pointer (`npc.Pointer`), not by reference equality (IL2CPP wrapper objects for the same native object are not reference-equal).

### Exception wrapper
Wrapped in `try/catch` so a failure during regeneration does not crash the conversation open path.

---

## CustomerNextOrderMessagePatch.cs

**Patches:** `Customer.CurrentContractEnded` — **Postfix**

### Purpose
When a customer's contract is fulfilled, sends them a text message saying roughly when they will order again — the next order day from the bulk/order-pattern schedule.

### Why `CurrentContractEnded`, not `ProcessHandoverServerSide`
`ProcessHandoverServerSide` is the in-person handover-screen path and does **not** fire for dead-drop deliveries. `CurrentContractEnded` fires for every delivery method, and its `EQuestState` parameter distinguishes a real completion from a failure/expiry.

### Duplicate callback guard (`_lastAnnouncedMinSum`)
`CurrentContractEnded` can fire more than once for a single completed contract (same multi-fire behaviour as `NotifyPlayerOfContract`), which sent the next-order text twice. The guard keys on customer name + in-game minute. Two genuine completions for one customer in the same minute cannot happen.

### Dealer-assigned customers skipped
The contract that just completed was the dealer's, so the player should not receive a "next order" message for it — otherwise every dealer-completed deal pings the player's phone.

### Day phrase rendering
- `delta == 1` → "tomorrow"
- `today + delta < 7` → "on {day name}"
- `today + delta >= 7` → "next {day name}"

### `EDay` convention
`EDay` is Monday = 0 .. Sunday = 6, matching the `DayNames` array.

---

## DeliveryAppPatch.cs

**Patches:** `DeliveryApp.SetOpen` — **Prefix**

### Purpose
When the delivery app is opened, auto-registers any delivery shop not yet tracked in the config with default settings (preserving user edits), then applies all delivery overrides via `DeliveryUtils.ApplyDeliveryOverrides`. This fills the default (empty) config and picks up shops added by later game patches without clobbering user edits.

---
