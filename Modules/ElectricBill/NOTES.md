# ElectricBill Module Notes

A weekly electricity bill metered by the player's built appliances, settled in **cash at the property's
rent dead drop** (no bank auto-deduct), with a power-shutoff enforcement when unpaid. The RV is exempt.

## Lifecycle (`ModElectricBill.cs`)

- **`Apply()`** — when enabled, unloads the per-save store and clears in-memory state (incl. the metering
  baseline). All real work is driven from `DriveUpdate()`.
- **`DriveUpdate()`** — forwarded every frame from `Core.OnUpdate`, **host-only**. Replaces the former
  postfix on `TimeManager.PassMinute`: `PassMinute()` is a one-line private forwarder
  (`PassMinute_Client(CurrentTime)`) that the IL2CPP build can inline into `TimeLoop`, so a postfix on it
  is not reliably invoked — when it didn't fire, metering never ran and the bill stayed $0. (Same
  fragility that moved the Rent module onto `OnUpdate`.) Metering is now derived from the in-game minute
  counter: each poll bills `GetTotalMinSum() − _lastMinSum` in-game minutes, so it advances exactly with
  game time and pauses with the clock. First poll after a load: refresh the appliance cache, rebuild the
  cut set, record day + minute baseline. Each poll: accrue the minute delta. On day rollover: refresh
  cache, reconcile cut set, process billing, persist.

## Metering

- `RefreshApplianceCache()` enumerates `FindObjectsOfType<BuildableItem>(true)`, keeps only buildables
  whose `ItemInstance.ID` is a configured appliance and whose `ParentProperty` is an **owned, non-RV**
  property, grouped by `PropertyCode`. Refreshed only on load and day rollover (not per minute) — new
  builds are picked up at the next refresh.
- `AccrueMinutes(mins)` adds, per property, `mins · Σ (IsActive ? InUseWatts : StandbyWatts)` to
  `AccruedWattMinutes` (`mins` = the in-game minute delta since the last poll). A powered-off property
  accrues nothing and instead re-forces any re-toggled lights off (`EnforceCut`). State is flushed to disk
  hourly (every 60 accrued minutes) and at rollover.
- **4 AM end-of-day stall:** the clock freezes at 4 AM (`ShouldMinutePass` false), so the minute counter
  stops advancing — the minute-delta naturally accrues nothing there. **Only when `EndOfDayFreeze` is
  off** does `AccrueEndOfDayRealtime()` keep metering, converting real seconds → in-game minutes at the
  game's cadence (`MinuteDuration`, scaled by the time multiplier/`timeScale`) so the AFK exploit (machines
  still producing) is still billed. With `EndOfDayFreeze` on, production is frozen so nothing is billed.
  (`EndOfDayFreezeActive()` checks `Core.Get<ModEndOfDayFreeze>()`.)
- **Time-skip billing:** `TimeSkipBillingPatch` (prefix on `TimeManager.OnTimeSkip_Client`) bills a sleep
  / story skip via `AccrueTimeSkip(minutes)`, where `minutes = |minSum(newTime) − minSum(oldTime)|` —
  the exact count the game advances each station's cook by (`onTimeSkip`). Appliance state is sampled
  pre-skip (what was left running when you slept). So overnight grow lights and overnight cooks are
  billed. Powered-off properties accrue nothing. `AccrueTimeSkip` then flags `_pendingSkipResync` so the
  next `DriveUpdate` poll re-baselines `_lastMinSum` across the skip jump **without** re-billing those
  minutes (they were just billed here).
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
- **No bank auto-deduct.** Electricity is billed as a *cash debt* the player settles at the property's
  **rent dead drop**, together with rent — `BillOnce` just adds `bill` to `OutstandingBill` and notifies
  "Power bill due … pay at the dead drop". The drop sweep (`ModRent.CreditFromDrop` → this module's
  `ApplyCashPayment`) is the sole settlement path. (Was previously a `MoneyManager.onlineBalance`
  auto-deduct; changed so cash-at-drop is the primary way to pay both rent and electricity.)
- **One interval of grace before a power cut:** `BillOnce` cuts power only if the *previous* bill was
  still unpaid (`OutstandingBill > 0`) when the new one lands. A player who keeps the drop funded is never
  cut (the rent sweep clears `OutstandingBill` each host tick, so `hadUnpaid` is false next cycle).
- `ApplyCashPayment(prop, amount, announce=true)` reduces `OutstandingBill`, restores power when it
  clears, and returns a `CashPaymentResult` (`Paid`/`Remaining`/`Cleared`/`PowerRestored`). The drop path
  passes `announce:false` so the outcome is reported in the *combined* landlord message rather than a
  separate power-bill notification.

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
