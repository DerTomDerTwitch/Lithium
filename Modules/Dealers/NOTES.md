# Modules/Dealers — Notes

Extracted from code comments and XML doc comments before they were stripped. Covers all `.cs` files in this folder and its subfolders.

---

## ModDealers.cs

**What it does.** Four dealer-management features driven from a per-minute tick:

1. **Robbery immunity** — a dealer carrying a rank-appropriate weapon is protected from cartel robberies. Delegated to `DealerRobberyPatch`.
2. **Daily weapon alert** — once per in-game day, texts the player from any dealer who is unarmed or whose weapon has been out-ranked by something now purchasable.
3. **Shortage warning** — roughly `ShortageConfiguration.LeadHours` before a scheduled deal the dealer can't cover, sends a text naming the missing products and quantities. One warning per dealer per upcoming deal.
4. **Weekly sales report** — at each in-game week rollover, texts each dealer's "what I sold" summary and rolls their tally for the new week. Weekly stats persist per save slot via `DealerStatsStore`.

**Tick / rollover logic.**
`Tick()` is called every in-game minute (via `DealerTickPatch` on `TimeManager.PassMinute`). It tracks `_lastHour` and `_lastDay` to detect rollover:
- Hour rollover → `OnHourPass()` (shortage warnings).
- Day rollover → `OnDayPass()` (weapon alerts). If the day rollover also crosses a week boundary (`day / 7 != prevDay / 7`), `OnWeekPass(day / 7)` fires too. The integer-division week boundary check handles multi-day sleep jumps correctly.

**First-tick initialisation guard.** On the first tick after a load, `_initialised` is false; the code just records the current day/hour and returns without firing any handlers, because the world may only just have come up and time values would otherwise register as spurious rollovers.

**`CurrentTime` format.** `time.CurrentTime` is 24-hour HHMM (e.g. 1430 = 14:30). Dividing by 100 gives the hour.

**`Apply()` resets state.** On each new save/scene load, `DealerStatsStore.Unload()` drops the in-memory stats so the new save's file is re-read lazily, `_alertedShortages` is cleared, and the day/hour cursors are reset.

**`_alertedShortages` set.** Keys are `"dealerId@dealAbsMinute"`. Stale entries (deals now in the past) are pruned each hour to prevent unbounded growth.

**Shortage grouping.** Shortfalls are grouped by `DealAbsMinute` so one message covers all missing items for the same upcoming deal. Only one shortage warning per dealer per tick is sent (`break` after the first group within the window).

**Config defaults.** `OutdatedWeaponImmunityChance` is clamped to `[0, 1]`; `LeadHours` is clamped to ≥ 1 in `Validate()`.

---

## Architecture/DealerMessenger.cs

**What it does.** Sends a phone text that appears to come from the dealer themselves — their own in-game Messages conversation — so supply, weapon and weekly-report notices read as that dealer texting the player.

**How it works.** Mirrors `Customers.LithiumContact` (which sends through a repurposed "Lithium" NPC's conversation), but keyed to the dealer's own NPC id. Before sending, calls `conv.SetIsKnown(true)` to ensure the dealer's conversation is visible in the player's phone before the message lands in it.

---

## Architecture/DealerWeaponInspector.cs

**What it does.** Classifies a dealer's weapon status as `None`, `Outdated`, or `Adequate`:
- **None** — no weapon in inventory; fully vulnerable (vanilla robbery behaviour).
- **Outdated** — has a weapon, but a higher-rank weapon is now purchasable at the player's current rank — only partial immunity.
- **Adequate** — weapon meets or exceeds the best the player can currently buy — full robbery immunity.

**Weapon definition.** A "weapon" is any `StorableItemDefinition` in the dealer's inventory that is `EItemCategory.Equipment` with a positive `CombatUtility` value.

**Rank ladder (`_weaponRankLadder`).** A `List<float>` of the rank-requirement floats (via `FullRank.ToFloat()`) for every buyable weapon in the game. Built once from `Registry.GetAllItems()` and cached for the whole session; the weapon roster is static per build so this is safe. If the `Registry` isn't up yet when first accessed, the method returns early and will retry on the next access.

**Adequacy comparison.** Uses a small epsilon (0.0001f) so an exact-match weapon ranks as adequate despite float rounding.

**Fail-safe for empty ladder.** `BestBuyableWeaponRank()` returns `0f` if the ladder is empty or the `LevelManager` isn't available yet, which means any held weapon reads as adequate — erring on the side of not robbing.

**Rank-zero weapons.** Weapons with no level requirement (e.g. a basic melee item, `RequiresLevelToPurchase == false`) are treated as rank `0f` — the lowest rung of the ladder, buyable from the start.

---

## Architecture/DealerShortageCalculator.cs

**What it does.** Computes which products a dealer will run short of for upcoming scheduled deals, accounting for bulk ordering (multiple pending contracts).

**Algorithm.**
1. Snapshot current stock from all dealer inventory slots (product id → quantity).
2. Collect all active contracts paired with their next due time (absolute in-game minute), sorted earliest-first.
3. Walk contracts in order, subtracting each deal's product requirements from the running stock. The first time a product goes negative is its shortfall, reported with its deficit and due time. Each product is flagged once (first shortfall only) via `flagged` `HashSet`.
4. Results are sorted by `DealAbsMinute` (soonest deal first).

**Due-minute calculation (`DueAbsMinute`).** Computes the next occurrence of a contract's delivery-window start as an absolute minute from now using a minutes-of-day delta. If the window start has already passed today, adds 1440 (minutes per day) for the next day's occurrence. This never assumes a particular game-day length.

**`ResolveName`.** Public helper used by `DealerSaleTrackingPatch` too — resolves a product id to its display name via `Registry.GetItem`, falling back to the raw id on any failure.

---

## Architecture/DealerStatsStore.cs

**What it does.** Stores and retrieves per-dealer weekly sales tallies, persisted per save slot via the shared `SaveSlotStore<T>` (survives save/load), keyed by dealer NPC id.

**`DealerWeeklyStats` fields.**
- `CurrentWeek` — `Dictionary<string, int>` accumulating product-name → units as deals complete during the current week.
- `LastWeek` — snapshot of the previous week's tally (kept for reference; not currently used by the report, which receives the snapshot from `RollWeek`).
- `WeekIndex` — the in-game week number at last roll.

**`Unload()`** — called from `ModDealers.Apply()` on each scene load so the next access re-reads the freshly loaded save's file (not stale in-memory data from a previous session).

**`RollWeek()`** — snapshots `CurrentWeek` into `LastWeek`, clears `CurrentWeek`, stamps the new week index, persists, and returns the snapshot (what the dealer sold this week) for the weekly report message.

---

## Patches/DealerTickPatch.cs

**What it does.** Postfix on `TimeManager.PassMinute` — fires every in-game minute, calls `ModDealers.Tick()`. Same hook used by the Banking and Rent modules for their daily work.

**Error handling.** Exceptions are caught and logged as warnings (not errors) to avoid disrupting the game's minute-tick loop.

---

## Patches/DealerSaleTrackingPatch.cs

**What it does.** Postfix on `Dealer.RemoveContractItems`. This method runs when a dealer pulls a delivered contract's products out of its own inventory to hand to the customer, making it the right hook for "the dealer just sold this."

**What it records.** Walks `contract.ProductList.entries` and calls `DealerStatsStore.Record` for each entry with the dealer's id, the resolved product name, and quantity. Reuses `DealerShortageCalculator.ResolveName` to get the display name.

**Guard.** Only active when both the module and `WeeklyReport.Enabled` are true.

---

## Patches/DealerRobberyPatch.cs

**What it does.** Prefix on `Dealer.TryRobDealer`. Conditionally cancels the robbery depending on the dealer's weapon status:
- **Adequate** — prefix returns `false` (skips vanilla robbery entirely).
- **Outdated** — rolls `Random.value` against `OutdatedWeaponImmunityChance`; if lucky, returns `false`; otherwise falls through to vanilla.
- **None** (unarmed) — returns `true`, vanilla robbery runs.

**Weapon mechanics.** The weapon is a real item the player physically drops into the dealer's inventory — not a flag or config value. The adequacy threshold is the best weapon the player can currently buy at their rank.

**Guard.** Falls back to vanilla (`return true`) when the module is disabled or `PreventWhenArmed` is false.
