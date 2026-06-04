# EndOfDayFreeze — Module Notes

## What this module does

Closes the AFK time-freeze exploit in Schedule I. The game freezes the displayed clock at 4 AM at the end of each day but continues firing `OnMinPass` on all production objects. This means a player standing AFK at 4 AM can let chemistry stations, drying racks, plants, and mixing stations advance indefinitely with no in-game time cost. This module stops all those production timers for the duration of the freeze. Production resumes automatically when the player sleeps into a new day (the freeze lifts).

The module defaults `Enabled = true` because its sole purpose is to close this exploit. Setting `"Enabled": false` in `EndOfDayFreeze.json` restores vanilla behavior.

---

## ModEndOfDayFreeze.cs

- Configuration class `ModEndOfDayFreezeConfiguration`: sets `Enabled = true` by default (unlike most modules which default to `false`). The rationale: the whole point of the module is to close the 4 AM time-freeze exploit, so it should be active unless the user explicitly opts out.
- The `ModEndOfDayFreeze` class itself has an empty `Apply()` override; all work is done by the patches and `EndOfDayGate`.

---

## EndOfDayGate.cs

Central shared predicate used by all four freeze patches.

- `ShouldFreeze()` returns `true` only when both conditions hold:
  1. The module is enabled (`module.Configuration.Enabled`).
  2. `TimeManager.IsEndOfDay` is `true` (the game clock is currently frozen at 4 AM).
- `TimeManager.IsEndOfDay` flips on exactly when the clock freezes and flips off once the player sleeps into a new day, so production resumes automatically the next morning with no extra logic needed.
- Each patch returns `!ShouldFreeze()` from a `Priority.First` prefix, which skips the entire tick (including any other module's patch on the same method) when frozen.

---

## Patches/DryingRackFreezePatch.cs

- Patches: `DryingRack.OnMinPass`
- Priority: `Priority.First` (runs before all other prefixes on this method)
- `ModDryingRacks` also patches `DryingRack.OnMinPass` with a prefix that reimplements drying logic and returns `false`. By running first and returning `false` during the freeze, this patch skips that reimplementation entirely, so `DryingOperation.Time` never advances while the clock is frozen at 4 AM.

---

## Patches/PlantGrowthFreezePatch.cs

- Patches: `Pot.OnMinPass`
- `Pot.OnMinPass` is where plant growth and water drain advance (and where `ModPlants` applies its grow-speed modifier).
- Priority: `Priority.First` — guarantees this patch runs before the `ModPlants` prefix.
- Returning `false` during the freeze skips the whole tick, including the `ModPlants` prefix, so plants neither grow nor drain water at 4 AM.

---

## Patches/ChemistryStationFreezePatch.cs

- Patches: `ChemistryCookOperation.Progress`
- `ChemistryCookOperation.Progress` is the per-minute cook-advance method (also scaled by `ModChemistryStation`'s speed prefix).
- Priority: `Priority.First` — skips the speed prefix from `ModChemistryStation` too.
- Returning `false` during the freeze skips the advance entirely, so a cook makes no progress at 4 AM.
- Cook completion is driven from inside `Progress`, so skipping it cannot accidentally complete a cook while frozen.

---

## Patches/MixingStationFreezePatch.cs

- Patches: `MixingStation.OnMinPass` (Mk1) and `MixingStationMk2.OnMinPass` (Mk2), both of which advance `CurrentMixTime`.
- Uses `TargetMethods()` rather than a static `[HarmonyPatch(typeof(...))]` attribute because `MixingStationMk2` may inherit `OnMinPass` from `MixingStation` rather than declaring its own override.
- `AccessTools.DeclaredMethod` returns `null` when a type does not declare the method itself. The logic: always yield the Mk1 base method (which covers Mk2 via inheritance), and only yield a second target if Mk2 actually declares its own override. This avoids patching the same `MethodInfo` twice while still covering both stations.
- Priority: `Priority.First` on the prefix.
