# Employees Module Notes

## ModEmployees.cs

`ModEmployees` is the module class; configuration is split into per-role sub-objects (`BotanistConfiguration`, `ChemistConfiguration`, etc.).

**`PreventWorkStoppage`** (config flag, default `false`): when enabled, employees never enter the "stuck / no-work" state caused by repeated pathing failures (the state normally cleared by punching them). They keep retrying instead of giving up. The 4 AM shift end is driven by the day/night schedule, not this flag. Implementation is in `EmployeeStuckPatch`.

**`TryBeginConfigure`** is a shared guard helper used by every per-role patch. It returns `true` (and the config object) only when: (a) the module is loaded and enabled, and (b) this particular `Employee` instance has not been configured yet (`ConfiguredEmployees.Add` returns `true`). This ensures each employee instance is tuned exactly once and replaces the identical preamble that would otherwise appear in every patch.

**`Apply()` is empty.** Per-employee tuning (wages, walk speed, instance caps) is applied lazily by the per-role `NetworkInitialize` patches.

**Why botanist timing fields are not configurable:** `SoilPourTime`, `WaterPourTime`, `AdditivePourTime`, `SeedSowTime`, and `IndividualHarvestTime` are IL2CPP static fields. Writing them via `il2cpp_field_static_set_value` crashes the game with an `AccessViolationException` in the installed build — the write is invalid regardless of when or where it runs. Their only consumers are native code, so there is no managed read-site to patch instead. The corresponding config fields were removed.

---

## Patches/BotanistPatch.cs

Patches `Botanist.NetworkInitialize__Late`.

- `Botanist` does not declare a `Start` method — only `Awake` and `NetworkInitialize__*` — so the patch must target `NetworkInitialize__Late` (same as other employee patches) to ensure the configuration object is fully initialized.
- The timing field names changed from ALL_CAPS constants (`HARVEST_TIME`) to PascalCase properties (`IndividualHarvestTime`) at some point during game development.
- The assignable-pot cap lives directly on `Botanist.MaxAssignedPots` (not on a sub-list like `Chemist.configuration.Stations`), so only `MaxAssignedPots`, `WalkSpeed`, and `DailyWage` are set.
- Pour/sow/harvest timings are deliberately omitted — see `ModEmployees.cs` notes above.

---

## Patches/ChemistPatch.cs

Patches `Chemist.NetworkInitialize__Late`.

Sets `configuration.Stations.MaxItems`, `Movement.WalkSpeed`, and `DailyWage`. No special gotchas — the configuration object is reliably present by this lifecycle point.

---

## Patches/CleanerPatch.cs

Patches `Cleaner.NetworkInitialize___Early` (note: triple underscore — this is the `Early` variant, unlike chemist/botanist/packager which use `Late`).

Sets `configuration.Bins.MaxItems`, `Movement.WalkSpeed`, and `DailyWage`.

---

## Patches/PackagerPatch.cs

Patches `Packager.NetworkInitialize__Late`.

**Critical gotcha:** this runs inside the game's native `NetworkInitialize__Late` trampoline. Any unhandled exception escapes into native code and causes a "during invoking native->managed trampoline" crash. The `configuration` entity and its `Stations`/`Routes` sub-fields are not guaranteed to be built yet for every spawned Packager. Each dereference is individually null-guarded; on a null `configuration`, a warning is logged and the station/route caps are silently skipped (never throw). `Movement` is also null-guarded before setting `WalkSpeed`.

Sets `Stations.MaxItems`, `Routes.MaxRoutes`, `Movement.WalkSpeed`, `DailyWage`, and `PackagingSpeedMultiplier`.

---

## Patches/NPCInventoryAwakePatch.cs

Patches `NPCInventory.Awake` as a **prefix** (runs before the original).

Sets `SlotCount` on the inventory before the rest of `Awake` runs. Identifies the owning employee role by walking up the hierarchy with `GetComponentInParent<T>()` and reads the per-role `InventorySlotCount` from config. Covers Botanist, Chemist, Cleaner, Dealer, and Packager.

---

## Patches/StorageMenuRowPatch.cs

Patches `StorageMenu.Open(IItemSlotOwner, string, string)` as a **postfix**.

**History:** the original version reimplemented `StorageMenu.Open` entirely and referenced row-count fields (`Employees.*RowAmount` / `Extra.DealerRowAmount`) from an unrelated mod that does not exist in this project. It was rewritten as a postfix that simply overrides `SlotGridLayout.constraintCount` after the menu opens, using this module's per-role `InventoryRowCount` config.

The `IItemSlotOwner` argument is cast to `NPCInventory`; if it is not an NPC inventory the patch exits early. Role is identified the same way as `NPCInventoryAwakePatch` — `GetComponentInParent<T>()`.

---

## Patches/EmployeeStuckPatch.cs

Patches `Employee.WalkCallback` as a **prefix**.

**The "stuck employee" mechanic explained:** every failed walk attempt increments `Employee.consecutivePathingFailures`. Once it reaches `MAX_CONSECUTIVE_PATHING_FAILURES`, the employee gives up — it submits a no-work reason and goes idle until physically dislodged (punching the NPC shoves them off the stuck spot). By resetting `consecutivePathingFailures` to `0` in the prefix — before `WalkCallback` reads or increments it — a failed walk only ever bumps the counter back to 1, so the cap is never reached and the employee keeps retrying its route indefinitely.

`WalkCallback` is the sole site that increments this counter and is not overridden by any subclass (`Botanist`, `Chemist`, `Packager`, `Cleaner`, `Dealer` all inherit `Employee`), so this one patch covers every employee type.

Gated behind `PreventWorkStoppage` config flag (off by default). Does **not** affect the 4 AM shift end — that is driven by the day/night schedule.
