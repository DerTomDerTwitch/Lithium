# Rent Module Notes

Captured from source comments. Covers every `.cs` file under `Modules/Rent/`.

---

## ModRent.cs

### What it does

Weekly rent per location, paid by dropping cash at an assigned dead drop. Each location has a
`RentLocationConfiguration` entry: whether it is enabled, the `WeeklyRent` amount, the `DeadDropName`
and optional `DeadDropGUID` (used in preference to name when two drops share a name), and the
`ContactNpcName` NPC who texts the player.

### Configuration

**`RentLocationConfiguration`** — per-location POCO:
- `Enabled`: auto-seeded entries start disabled so they are safely editable before turning on.
- `WeeklyRent`: charged every `RentIntervalDays` in-game days.
- `DeadDropName` / `DeadDropGUID`: find via the F8 in-game dump (`RentDebug.Dump`). GUID wins over name.
- `ContactNpcName`: full NPC display name; defaults to `"Fixer"`.

**`ModRentConfiguration`** — module-level:
- `RentIntervalDays` (default 7): the **rent period** — in-game days between charges. Each location's
  cadence is anchored to the day it was first seen owned, and the **first period is always free** (the
  first charge lands one full period later, never on the spot). Lower this (e.g. to `1`) to test the
  overdue texts / lockout quickly.
- `DaysUntilLockout` (default 2): days after rent becomes due before the property is locked; 0 = lock
  immediately.
- `SendFinalWarning` (default true): if true, a final-warning text is sent the day *before* lockout.
- `Locations`: pre-seeded dictionary with default dead-drop and contact assignments for every known
  in-game property/business; any unlisted location is auto-discovered (disabled, $0) the first time a
  save loads.

### Persistence

State is keyed by the property's stable `PropertyCode` and stored via `SaveSlotStore<RentLocationState>`
(named `"Rent"`, label `"rent state"`). The store is unloaded on every `Apply()` so each save starts
fresh from its own file.

### In-memory locked set (`LockedCodes`)

`LockedCodes` is a `HashSet<string>` of property codes currently locked for non-payment. It is kept in
memory so the door-access patch (called very frequently, once per minute at minimum) never touches disk.
It is rebuilt from the persisted state on the first tick after a save loads (`RebuildLockedFromState`).

### First period free (anchoring)

On first sight of a location — whether it was already owned when the save loaded or bought during play —
the cadence anchor (`LastChargedDay`) is set to **today**, so the first charge lands exactly one
`RentIntervalDays` later and the first period is free. The anchor is persisted, so the choice is made
once per location and never re-evaluated on later loads. (Earlier logic backdated the anchor and billed
pre-existing properties on first sight; that made a property read "overdue" before `ProcessDay` had run
the intervening days, so the displayed status and the lockout enforcement disagreed.)

A baseline sentinel (`__lithium_rent_baseline_v2__`) is written once per save the first time the mod
runs, recording that the established-vs-fresh decision has been made. Any owned location still lacking
state afterwards is therefore a fresh purchase (gets the free first period), never a pre-existing one —
which is what lets a freshly-bought property survive a reload/Alt+F4 without being billed on the spot.

### DebugToggleLockout (F12 testing hotkey)

`DebugToggleLockout()` (host-only) force-locks every enabled, owned rent location immediately — seeding a
token debt if none is owed — and texts the contact, so the lockout enforcement (e.g. the motel's exterior
door) and the messaging path can both be verified without waiting for the cadence. A second press clears
the lockout and the test debt, restoring access. Wired to **F12** in `Core.OnUpdate`, gated by
`HotkeyF12RentLockoutTest` in `Lithium.json` (default off).

### Apply() and startup flow

`Apply()` always runs `DiscoverLocations()` even when the module is disabled, so every owned
property/business appears in `Rent.json` and is configurable before the module is turned on.

When enabled, `Apply()`:
1. Unloads the store and clears in-memory state.
2. Starts `LoadReminderRoutine()` as a MelonCoroutine.

**LoadReminderRoutine**: Waits (capped at 30 s) for the messaging system and NPC registry to come up
after a load (mirrors `ModCustomers`' startup routine). After the readiness check it waits a further 2 s
so property ownership has settled, then calls `SendLoadReminders()`. This delay is necessary: on a fresh
load the NPC registry and messaging aren't populated yet, so a message sent immediately would be silently
dropped (the contact NPC won't resolve).

### Daily tick (`Tick`)

Called every in-game minute by `RentDailyTickPatch` (postfix on `TimeManager.PassMinute`). Does real
work only when the day rolls over (compares `ElapsedDays`). On the first tick after a load it runs
discovery, captures established locations, rebuilds the locked set, **reconciles lockouts against the
current overdue count** (`ReconcileLockoutsOnLoad`, see below), and records the current day — but does
not process a day (properties/save may have only just become available). Multi-day jumps from sleeping
are handled because the loop in `ApplyDueCharges` advances by `RentIntervalDays` increments until caught
up.

### Lockout evaluation runs on every overdue path, not just `ProcessDay` (`ReconcileOverdue`)

The warning/lockout transition is `ReconcileOverdue(prop, loc, code, state, today)` — a pure function of
`(today, state)` that sets `WarningSent` / `LockedOut` (+ `LockedCodes`) and returns the transition
message, or null. It is called from **three** paths: `ProcessDay` (live day rollover), the `Tick` init
block via `ReconcileLockoutsOnLoad` (on load), and `SendLoadReminders` (so its message matches the
freshly-applied lockout).

**Why this matters — the bug it fixes.** `ApplyDueCharges` (which advances charges and sets `DueSinceDay`)
runs in three places, but the lockout/warning evaluation used to run in **only** `ProcessDay`, which fires
only on a *live in-game day rollover*. The `Tick` init block resets `_lastElapsedDay = today` on every
load, so when the threshold day was crossed across a load boundary — or charges were caught up at load by
`SendLoadReminders` (which never locked) — `ProcessDay` never "saw" the threshold day and the property
accrued overdue days while staying unlocked and access-open. Symptom seen in the wild: a Motel reading
"8 days overdue" with `LockedOut: false` **and** `WarningSent: false` (impossible if `ProcessDay` had run
on any overdue day) — the door stayed unlocked. This is the same path-coverage trap noted in the root
`CLAUDE.md`: the mutation (charging) ran on more code paths than the enforcement (locking).
`ReconcileLockoutsOnLoad` closes the gap; an existing already-overdue save self-heals on the next load.

### ProcessDay logic

For each enabled, owned location:
1. If **locked out**: freeze the anchor (advance `LastChargedDay` to today so no back-rent accrues),
   send a "still locked" daily message, persist, and continue.
2. Otherwise: call `ApplyDueCharges` to advance the cadence and accumulate `Owed`.
3. If newly charged: send a due-notice message.
4. Call `ReconcileOverdue` (shared warning/lockout evaluation):
   - If `overdue >= DaysUntilLockout`: lock out, add to `LockedCodes`, send lockout message.
   - Else if `DaysUntilLockout >= 1` and `overdue == DaysUntilLockout - 1`: send the final warning (once
     per overdue spell, tracked by `WarningSent`).
5. If still owed and **not** locked and nothing else was sent today: send a daily reminder nudge.
6. Persist the state.

### Payment processing (`ProcessPayment`)

Called from `DeadDropClosePatch` when any storage UI closes. Matches the closed storage to a dead drop,
then to a rent location (GUID wins over name). Takes cash up to `Owed` from the storage slots (leftover
cash stays). On full payment: clears `Owed`, `DueSinceDay`, `WarningSent`, `LockedOut`, removes from
`LockedCodes`, texts confirmation. On partial payment: deducts and texts remaining balance. Best-effort —
never throws into the game's close path.

After rent, `CreditFromDrop` also settles the **same property's outstanding power bill** from any cash
left in the drop: it reads `ModElectricBill.GetOutstandingBill(code)` and, if positive, takes that much
cash and calls `ModElectricBill.ApplyCashPayment(prop, cash, announce:false)`, capturing the returned
`CashPaymentResult`. The phone Property tab shows rent and electricity together, and electricity is paid
**only in cash here** (no bank auto-deduct), so the rent dead drop is the settlement point for both.

**One combined message.** Rather than texting separately for rent and electricity, `CreditFromDrop`
accumulates the outcome of both (amount paid, cleared/partial, access/power restored) and sends a single
landlord message built by `ComposeDropMessage` — e.g. *"Received $1,200 for Motel Room: $1,000 rent (paid
up), $200 electricity (paid up). Your access is restored. Power is back on."* This is why
`ApplyCashPayment` is called with `announce:false` (it would otherwise send its own power-bill text).

### Auto-discovery (`DiscoverLocations`)

Lists every property/business (owned or not) using `Property.Properties` and `Business.Businesses`
(not just the owned lists), because scene property objects are present at load while ownership is
restored from the save a moment later, and pre-buy configuration is useful. Any unlisted name gets a
disabled `$0` entry and the config is saved. The charging logic filters to owned+enabled, so unowned
entries never bill.

### AllOwned / AllProperties / Deduplicate

`Business` is a subclass of `Property`, so both lists can overlap. `Deduplicate` iterates properties
first, then businesses, deduplicating by `PropertyCode` — so a location appearing in both lists is
yielded only once.

### SendLoadReminders

Once the world is ready, brings every enabled, owned location's rent up to date for the current day
(first-sight billing charges the current week), **reconciles the lockout/warning state against the
current overdue count** (`ReconcileOverdue`, so the status message below is accurate), and texts the
player the status of any location with outstanding rent. Persists the freshly billed state so it survives
the next save/load. Everything owned at this point is treated as established (not fresh-buy grace).

### Default location table

Pre-seeded defaults with dead-drop GUIDs and contact NPCs for all known properties:

| Location         | Weekly Rent | Dead Drop                         | GUID                                   | Contact NPC      |
|------------------|-------------|-----------------------------------|----------------------------------------|------------------|
| Sweatshop        | $200        | North arcade wall                 | dd3d22f1-da56-4673-9203-640eeaf915fc   | Mrs. Ming        |
| Motel Room       | $75         | Behind motel office               | d66b3fd6-7b7f-4e98-b000-6d5a197f7437   | Donna Martin     |
| Laundromat       | $250        | Alleyway behind the laundromat    | 555e565d-2f65-4882-9edb-7167168b2e00   | Doris Lubbin     |
| Storage Unit     | $500        | Behind Casino                     | f5614c42-2c74-42d5-bee2-025e84718792   | Geraldine Poon   |
| Taco Ticklers    | $1000       | Taco Ticklers exterior wall       | 79200bb6-7d39-44d9-ab22-c63f136d8cdf   | Dean Webster     |
| Bungalow         | $300        | Brown apartment block             | b77ed1a0-c729-41d5-b8a2-81b480ca971f   | Hank Stevenson   |
| Barn             | $750        | Behind fire station               | 86835825-f5f8-454d-9c74-c31e257f9cc2   | Harold Colt      |
| Post Office      | $850        | Central canal                     | 4ac50ff1-ad3c-415b-aa77-c80249dfa473   | Bruce Norton     |
| Car Wash         | $1000       | Behind auto shop                  | aaea12a7-ee38-47ba-aeb8-fb0d45a03957   | Kelly Reynolds   |
| Docks Warehouse  | $2500       | Behind Randy's bait & tackle      | baf08ceb-a0e7-4e4a-baa8-6b4cc992cb15   | Carl Bundy       |
| RV               | disabled    | (none)                            | —                                      | —                |
| Hyland Manor     | disabled    | (none)                            | —                                      | —                |
| Sewer Office     | disabled    | (none)                            | —                                      | —                |

---

## RentLocationState.cs

### What it does

Per-save, per-location runtime state. Plain POCO serialized with Newtonsoft.Json, persisted via
`SaveSlotStore<RentLocationState>` keyed by the property's stable `PropertyCode`.

### Fields

- `Owed` (float): outstanding rent currently owed.
- `LastChargedDay` (int, default -1): `ElapsedDays` at which the most recent charge was applied (the
  cadence anchor). Next charge due at `LastChargedDay + RentIntervalDays`. -1 = not yet initialised;
  anchored to the current day the first time the location is processed, so the first period is free.
- `DueSinceDay` (int, default -1): `ElapsedDays` on which the current outstanding debt first became due,
  used to time the warning and lockout. -1 when nothing is owed.
- `WarningSent` (bool): true once the pre-lockout final warning has been sent for the current overdue
  spell. Reset when debt is cleared.
- `LockedOut` (bool): true while the property is locked to the player for non-payment (rent charges are
  frozen while locked, but the owed balance persists and daily reminders continue).

---

## RentMessenger.cs

### What it does

Sends rent text messages through an existing in-game NPC's `MSGConversation`. The NPC is resolved by
full name every time (`NPCManager.NPCRegistry`), so a reloaded save's fresh NPC instance is used rather
than a stale reference. The conversation is marked `SetIsKnown(true)` so it shows up in the player's
message list. The conversation's own name is deliberately left untouched, so repurposing an NPC for
rent notifications never interferes with their normal dialog. Different properties can use different
contact NPCs (configured per location). Best-effort — drops and logs a warning if the NPC can't be
resolved.

---

## RentDebug.cs

### What it does

F8 debug dump (triggered from `Core.OnUpdate`). Writes `UserData/Lithium/RentLocations.txt` with:
- All dead drops: name, GUID, region, world position.
- All properties and businesses (owned marked with `*`): display name, code, position, nearest dead drop
  and distance.
- All available contact NPCs: flagged `**` if they have a `MSGConversation` and are **not** a customer
  (texting a customer for rent can interfere with their normal deal flow).

The output is always printed as `Log.Warning` (not `.Info`) so it is visible without enabling Debug mode
— this is a manually triggered user action.

### Gotcha: avoid customers as contacts

The dump explicitly flags whether each NPC is a customer. Sending rent messages through a customer NPC
can interfere with their normal deal flow, so `**` candidates are non-customer NPCs with a conversation.

---

## Patches/RentDailyTickPatch.cs

### Patched method

`TimeManager.PassMinute` (postfix)

### Why

`PassMinute` fires every in-game minute. The module compares `ElapsedDays` against a stored value and
only does real work when the day rolls over, so sleeping (which advances the clock by multiple minutes)
is handled correctly — it detects multi-day jumps. Other modules already hang per-minute work off this
method. All logic is delegated to `ModRent.Tick()`.

---

## Patches/PropertyDoorAccessPatch.cs

### Patched method

`PropertyDoorController.CanPlayerAccess(EDoorSide side, out string reason)` (prefix)

### Why / behaviour

Enforces the rent lock-out at a property's door. Only the `EDoorSide.Exterior` side is blocked —
`Interior` requests are passed through — so the player can still leave from the inside but cannot return
from outside until rent is paid. This is the same one-way behaviour the game uses when police are
pursuing the player. The lock-out check uses the in-memory `LockedCodes` set (rebuilt from persisted
state on load) so the patch never touches disk.

Lock-out message shown to player: `"The locks have been changed. Pay your overdue rent."`

The prefix returns `false` (skip original) only when denying entry; returns `true` (run original) for
all other cases, including exceptions.

---

## Patches/DeadDropClosePatch.cs

### Patched method

`StorageMenu.CloseMenu` (prefix)

### Why

Runs as a **prefix** so that `OpenedStorageEntity` is still set on the `StorageMenu` instance at the
time the patch runs (it would be cleared during or after the original method). Reads the entity and
delegates all matching and payment logic to `ModRent.ProcessPayment`. Best-effort — exceptions are
caught and logged rather than propagated into the game's close path.
