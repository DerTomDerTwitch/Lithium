# Banking Module — Notes

## ModBanking.cs

### EFeeMode enum
Two modes for calculating the bank transfer fee:
- `Percent` — fee is a percentage of the transaction amount, with optional minimum (`MinFee`) and maximum (`MaxFee`) bounds (0 means no bound).
- `Fixed` — a flat fee regardless of transaction size.

### TransferFeeConfiguration
Fee charged on ATM transactions (deposits move cash → bank balance, withdrawals the reverse). The fee is always deducted from the **online (bank) balance**, never from cash. Config fields:
- `Percent`: integer-like float, e.g. `5` means 5%.
- `MinFee`: floor on the computed fee; `0` disables the floor.
- `MaxFee`: cap on the computed fee; `0` disables the cap.
- `ApplyToDeposits` / `ApplyToWithdrawals`: which directions are affected. Defaults: deposits off, withdrawals on.
- `Compute(amount)`: always clamps result to `[0, amount]` — fee can never exceed the transaction size.

### AtmConfiguration
- The vanilla game only enforces a **weekly deposit limit** (`ATM.WEEKLY_DEPOSIT_LIMIT`, default $10,000). Lithium also layers in a **per-day** deposit cap.
- A value of `-1` is the canonical "unlimited" sentinel for both limits. Any other negative number is normalised to `-1` in `Validate()`.
- `WeeklyLimited` / `DailyLimited` — convenience booleans derived from those sentinels.

### BusinessLaunderingConfiguration
Per-business money-laundering tuning. Key fields:
- `Capacity`: fixed maximum cash-in-flight for the business. `-1` means "leave the game's base value untouched". Any value `>= 0` overrides it. The first time a save loads, `DiscoverBusinesses()` captures the live in-game base capacity and writes it here if it was still `-1`, so the JSON always ends up holding a real number.
- `SpeedMultiplier`: multiplies completion time. `> 1` = finishes faster, `< 1` = slower, `1` = vanilla.
- `Cut`: percentage of the completed laundering job's gross amount skimmed off as a fee and charged to the player's bank balance. Expressed as a percent (0–100) to match `TransferFee.Percent`. `0` = no cut.

### LaunderingXpScalingConfiguration
Optional rank-based multiplier on top of per-business capacity and speed. The dictionaries map `ERank` member names (e.g. `"Hustler"`, `"Kingpin"`) to multipliers. The multiplier of the **highest rank the player has reached** applies — it is a step function, not interpolated. Valid rank names are the `ERank` enum members: `Street_Rat, Hoodlum, Peddler, Hustler, Bagman, Enforcer, Shot_Caller, Block_Boss, Underlord, Baron, Kingpin`.

### LaunderingReportConfiguration
Daily laundering report texted via an NPC contact (repurposing an existing NPC's Messages conversation). Fields:
- `ContactNpcName`: full name of the NPC. Defaults to `"Herbert"` (the weapons merchant). Find exact names via the F7 NPC roster dump.
- `ContactDisplayName`: optional override for the contact's name in the Messages app. Empty leaves the NPC's own name untouched.

### LaunderingConfiguration
`Businesses` dictionary is seeded with the four vanilla laundering fronts (`Laundromat`, `Car Wash`, `Post Office`, `Taco Ticklers`) as default entries so the JSON is self-documenting out of the box. Actual business discovery happens at runtime in `DiscoverBusinesses()`.

### ModBankingConfiguration.Validate()
- Normalises any negative capacity to `-1`.
- Clamps `SpeedMultiplier` to at least `0.01` (avoids division-by-zero or instant laundering).
- Clamps `Cut` to `[0, 100]`.

### ModBanking class
- `DailyDepositSum` (static): running total of deposits made at ATMs today. Reset in `Apply()` on save load and in `AtmDayPassPatch` each game day.
- `Tallies` (static): per-business laundering gross + cut since the last daily report. Reset in `Apply()` and after each report.
- `_lastElapsedDay` / `_initialised`: protect the daily-tick logic. On the **first tick after load**, `DiscoverBusinesses()` is called again (catches capacities that were not yet available at `Apply()`) and the day counter is anchored **without** emitting a report (save state may only just have loaded).
- `GetRankMultiplier()`: step function — finds the entry in `byRank` with the highest rank index that does not exceed the player's current rank. Returns `1` if scaling is disabled, unavailable, or nothing matches. A negative multiplier is treated as `1` (safety guard).
- `RecordLaundering()`: called from `LaunderCutPatch` after each completed job. Accumulates totals per-business name; defaults unknown name to `"Unknown"`.
- `Tick()`: hooked to `TimeManager.PassMinute` (see `BankingDailyTickPatch`). Only acts when `ElapsedDays` changes; handles multi-day sleep jumps correctly because it anchors to the new day each time.
- `EmitDailyReport()`: skips the report if `DailyReport.Enabled` is false or there were no laundering jobs. Formats a multi-line message via `StringBuilder` and sends it via `BankingContact.Send()`.

---

## BankingContact.cs

Utility class for sending the daily laundering report through an existing NPC's Messages conversation. The NPC is resolved **by name every call** so a freshly loaded save's new NPC instance is used rather than a stale reference. Key design decisions:
- **Two-pass resolution**: first an exact `fullName` match (case-insensitive); if that fails, falls back to a prefix match so a configured first name (e.g. `"Herbert"`) resolves even if the exact last name is not known.
- `SetIsKnown(true)` ensures the NPC surfaces in the Messages app; without this, new conversations do not appear in the inbox.
- The contact display name (`conv.contactName`) is only overridden if the config actually provides a non-empty value.
- The entire `Send` call is wrapped in try/catch; failures are logged as warnings and silently dropped rather than crashing the tick.

---

## Patches/AtmDepositLimitPatch.cs

**Patches:** `ATM.Awake` (postfix)

Sets two vanilla static fields on every ATM when it wakes up:
- `ATM.DepositLimitEnabled` — set to `true` whenever either the weekly or daily cap is active. Must stay `true` even if only the daily cap is configured, so the UI still clamps deposits (the daily cap is enforced via `AtmRemainingDepositPatch` which relies on `DepositLimitEnabled` already being on).
- `ATM.WEEKLY_DEPOSIT_LIMIT` — set to the configured weekly limit, or the `Unlimited` sentinel (`1_000_000_000f`) when only the daily cap is wanted. The sentinel keeps the limiter enabled without actually restricting weekly deposits.

`Unlimited = 1_000_000_000f` is a large stand-in value; it is not `float.MaxValue` to avoid any potential overflow in the game's arithmetic.

---

## Patches/AtmRemainingDepositPatch.cs

**Patches:** `ATMInterface.remainingAllowedDeposit` getter (postfix)

The ATM UI calls this getter to determine how much more the player can still deposit. The vanilla implementation returns weekly headroom. This postfix **clamps the result to the remaining daily allowance** (`DailyDepositLimit - DailyDepositSum`), so both limits are respected simultaneously. If the daily cap is not configured the patch is a no-op.

---

## Patches/AtmDayPassPatch.cs

**Patches:** `ATM.DayPass` (postfix)

Resets `ModBanking.DailyDepositSum = 0f` each game day. `DayPass` fires once per day per ATM instance; the reset is idempotent (resetting an already-zero value is safe and multiple ATMs in the world would all reset it, which is fine). The vanilla weekly reset is handled by the game itself in `ATM.WeekPass` — no patch needed for that.

---

## Patches/AtmTransactionPatch.cs

**Patches:** `ATMInterface.ProcessTransaction(float amount, bool depositing)` (prefix)

Runs before the transaction processes, doing two things:

1. **Daily deposit tracking**: if the player is depositing and the daily cap is active, accumulates `amount` into `ModBanking.DailyDepositSum`.

2. **Transfer fee**: computes the fee via `TransferFeeConfiguration.Compute(amount)`, then charges it to the online balance via `moneyManager.CreateOnlineTransaction`. The fee is taken from the online balance regardless of direction (deposits and withdrawals both hit the bank).

**Critical overdraw prevention**: for a withdrawal, the transaction will simultaneously reduce `onlineBalance` by `amount`. So the fee must not exceed `(onlineBalance - amount)` — if it did, the fee alone would drive the account negative. The available balance for fee purposes is therefore `onlineBalance - (depositing ? 0 : amount)`. This was a real bug: withdrawing the entire balance would compute a fee against the pre-withdrawal balance and then drive the account below zero.

---

## Patches/BankingDailyTickPatch.cs

**Patches:** `TimeManager.PassMinute` (postfix)

Drives `ModBanking.Tick()` every in-game minute. The module's own `Tick()` only does meaningful work when `ElapsedDays` increments. Wrapped in try/catch to prevent any exception from disrupting the game's minute-tick pipeline. Other modules also hook `PassMinute`; that pattern is established convention in this codebase.

---

## Patches/LaunderCapacityPatch.cs

**Patches:** `Business.appliedLaunderLimit` getter (postfix)

`appliedLaunderLimit` is the effective ceiling on cash-in-flight checked by the laundering UI and operation-start logic. Patching the getter is the **single chokepoint** for all capacity enforcement. The patch:
1. If the configured `Capacity >= 0`, overrides `__result` with it (otherwise leaves the game's base value from the getter's original return).
2. Multiplies `__result` by the rank-based capacity multiplier (even if the capacity was left at the game base).

The rank multiplier is applied unconditionally (both to overridden and game-base capacities), so "scale laundered amount with XP" is purely a capacity multiplier that grows the cap as the player ranks up.

---

## Patches/LaunderCutPatch.cs

**Patches:** `Business.CompleteOperation(LaunderingOperation op)` (postfix)

Called when a laundering job finishes. Skims a configured percentage of the gross laundered amount (`op.amount`) off the player's online (bank) balance as a laundering fee. Key behaviours:
- `op.amount` is the gross amount laundered — the cut is a percentage of that.
- The cut is capped at `max(0, onlineBalance)` to prevent the account going negative.
- If `MoneyManager` is unavailable, the cut is zeroed out (so `RecordLaundering` still gets called but with `cut = 0`).
- Calls `ModBanking.RecordLaundering(name, laundered, cut)` to accumulate totals for the daily report regardless of whether a cut was taken.

---

## Patches/LaunderSpeedPatch.cs

**Patches:** `LaunderingOperation` constructor `(Business, float, int)` (postfix)

`completionTime_Minutes` is set in the constructor and determines how long the job takes. Patching the constructor is the **single chokepoint** for every laundering job regardless of how it was started. The effective speed multiplier is:
  `perBusinessSpeedMultiplier × rankSpeedMultiplier`

A higher multiplier means a shorter completion time: `completionTime_Minutes = RoundToInt(original / multiplier)`. Clamped to at least 1 minute. The patch is a no-op (early return) when the combined multiplier is `≤ 0` or approximately `1.0` (using `Mathf.Approximately`).
