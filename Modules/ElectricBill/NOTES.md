# ElectricBill Module Notes

A weekly electricity bill metered by the player's built appliances, auto-deducted from the online bank
balance, with a power-shutoff enforcement when unpaid. The RV is exempt.

## Lifecycle (`ModElectricBill.cs`)

- **`Apply()`** — when enabled, unloads the per-save store and clears in-memory state. All real work is
  driven from `Tick()`.
- **`Tick()`** — called every in-game minute from `ElectricBillTickPatch` (postfix on
  `TimeManager.PassMinute`). First tick after a load: refresh the appliance cache, rebuild the cut set
  from persisted state, record the day. Every minute thereafter: accrue energy. On day rollover: refresh
  cache, reconcile cut set, process billing, persist.

## Metering

- `RefreshApplianceCache()` enumerates `FindObjectsOfType<BuildableItem>(true)`, keeps only buildables
  whose `ItemInstance.ID` is a configured appliance and whose `ParentProperty` is an **owned, non-RV**
  property, grouped by `PropertyCode`. Refreshed only on load and day rollover (not per minute) — new
  builds are picked up at the next refresh.
- `AccrueMinute()` adds, per property, `Σ (IsActive ? InUseWatts : StandbyWatts)` to
  `AccruedWattMinutes` (shared `SumActiveWatts` helper). A powered-off property accrues nothing and
  instead re-forces any re-toggled lights off (`EnforceCut`). State is flushed to disk hourly (every 60
  ticks) and at rollover.
- **4 AM end-of-day stall:** `PassMinute` keeps firing while the clock is frozen at 4 AM without time
  advancing. Accrual is skipped there **only when `EndOfDayFreeze` is enabled** (production is frozen, so
  nothing to bill). When `EndOfDayFreeze` is off, machines keep producing (the AFK exploit) so metering
  continues — you pay for what runs. (`EndOfDayFreezeActive()` checks `Core.Get<ModEndOfDayFreeze>()`.)
- **Time-skip billing:** `TimeSkipBillingPatch` (prefix on `TimeManager.OnTimeSkip_Client`) bills a sleep
  / story skip via `AccrueTimeSkip(minutes)`, where `minutes = |minSum(newTime) − minSum(oldTime)|` —
  the exact count the game advances each station's cook by (`onTimeSkip`). Appliance state is sampled
  pre-skip (what was left running when you slept). So overnight grow lights and overnight cooks are
  billed. Powered-off properties accrue nothing.
- `ApplianceStateResolver` maps each item ID to an `IsActive` predicate (and, for lights only, a
  ForceOff/Restore). Lights: `ToggleableItem`/`ToggleableSurfaceItem.IsOn`, `GrowLight.Light.isOn`.
  Machines: current-operation non-null (`ChemistryStation.CurrentCookOperation`,
  `LabOven.CurrentOperation`, `MixingStation.CurrentMixOperation`), `Cauldron.isCooking`, packaging
  user-object non-null, laundering `business.currentLaunderTotal > 0`. Unknown IDs = always standby.
  - Note: `ChemistryStation`/`LabOven` collide with the `Lithium.Modules.ChemistryStation`/`.LabOven`
    namespaces, so they are imported under `using` aliases (`ChemStation`/`Oven`). A same-name alias is
    **not** enough — it fails in `typeof()`/attribute context — so a distinct alias name is required.

## Weekly billing (`ProcessDayRollover` → `BillOnce`)

- Cadence anchored on first sight (`LastBilledDay`); the partial first week isn't billed.
- `kWh = AccruedWattMinutes / 60 / 1000`; `bill = kWh * RatePerKwh`; meter resets.
- `due = bill + OutstandingBill`. `TryDeduct` reads `MoneyManager.onlineBalance`; if it covers `due`,
  `CreateOnlineTransaction("Electricity", -due, 1, …)` deducts and any cut is restored. Otherwise `due`
  becomes `OutstandingBill` and the property is cut.
- While cut, each day rollover retries the auto-pay (`TryAutoPay`) so power returns as soon as funds
  exist.

## Power shutoff

- `CutPower` adds the `PropertyCode` to the in-memory `PowerCutCodes` set, captures each light's on/off
  state into `LightStatesAtCutoff` (keyed by buildable GUID), and force-offs lights. Machines are not
  touched directly — they stop because their freeze patch now gates on the cut code.
- Freeze patches (`Patches/*FreezePatch.cs`) are prefixes returning `false` when
  `ElectricBillGate.IsCut(__instance)` (module enabled **and** the buildable's property is cut). Hooks:
  `ChemistryStation`/`MixingStation`(+Mk2)/`Cauldron`/`LabOven` **`OnTimePass(int)`** — the chokepoint
  for *both* the per-minute path (`OnMinPass → OnTimePass(1)`) and the sleep-skip path
  (`onTimeSkip → OnTimePass(n)`), so a cut property's cooks freeze across sleep too;
  `PackagingStation.StartTask`/`PackSingleInstance` (+Mk2 `StartTask`); `Business.MinPass` (laundering —
  `Business` is a `Property`); `Sprinkler.Water` (momentary, so the activation is blocked).
- `RestorePower` removes the code, restores lights that were on at cutoff, clears the captured map.
  Machines resume automatically once the gate clears.
- `PowerCutCodes` is in memory (rebuilt from persisted `PoweredOff` on load via `RebuildCutFromState`)
  so the high-frequency freeze patches never touch disk — mirrors `ModRent.LockedCodes`.

## Persistence

`SaveSlotStore<ElectricBillState>("ElectricBill", …)` keyed by `PropertyCode`. `ElectricBillState`:
`AccruedWattMinutes`, `LastBilledDay`, `OutstandingBill`, `PoweredOff`, `LightStatesAtCutoff`.

## Authoring / debug

**F9** (`BuildablesDebug.Dump`, wired in `Core.OnUpdate`) writes `UserData/Lithium/Buildables.txt` —
every player-built buildable grouped by property with item IDs — to author the `Appliances` table.

## Known limitations

- A sleep `TimeSkipped` advances laundering via `Business.MinsPass(n)` (not `MinPass`), so laundering
  isn't frozen across a sleep while cut. Minor.
- If a save is loaded while a property was cut and ownership isn't restored by the first tick, the cut
  set re-reconciles at the next day rollover (self-healing).
