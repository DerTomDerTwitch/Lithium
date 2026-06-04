# Modules/Storyline — Notes

## ModStoryline.cs

No comments in source. The module exposes a single config flag `PreventRVExplosion` (defaults `true`). The `Apply()` override is a stub that only guards on `Enabled`; all real behaviour is in the patches.

## Patches/QuestWelcomeToHylandPointPatch.cs

No comments in source.

Patches `Quest_WelcomeToHylandPoint.BlowupRV`. This was the original intercept for the RV explosion story beat — when both `Enabled` and `PreventRVExplosion` are true the prefix returns `false`, fully replacing (suppressing) `BlowupRV`. If the config is off it returns `true` to let the original run.

This patch targets the **quest-side** trigger. The separate `RVSetExplodedPatch.cs` targets the **property-side** triggers (see below).

## Patches/RVSetExplodedPatch.cs

### Context / why this file exists

The game reworked the RV destruction flow at some point: the old single method `RV.SetExploded()` was split into two separate methods:
- `RV.BlowUp()` — plays the explosion sequence (visual/audio).
- `RV.SetDestroyed()` — sets the destroyed persistent state on the property.

Both are now intercepted so that regardless of which code path fires, the explosion is skipped and the wrecked-RV model is swapped in silently.

### Patches

- `RVBlowUpPatch` — `[HarmonyPrefix]` on `RV.BlowUp`. Delegates to `RVExplosionPrevention.Handle`.
- `RVSetDestroyedPatch` — `[HarmonyPrefix]` on `RV.SetDestroyed`. Also delegates to `RVExplosionPrevention.Handle`.

Both patches share a single helper class to avoid duplication.

### RVExplosionPrevention.Handle

Returns `true` (run original) when the module/flag is disabled. When active it performs the "quiet wreckage swap":
1. Finds child `"Destroyed RV"` on the RV GameObject.
2. Activates it, and also activates the `"CartelNote"` child inside it (so story props still appear).
3. **Deactivates** the nested `"destroyed rv"` child (lower-case) — this child appears to be a sub-model or debris that should stay hidden in the quiet-swap path.
4. Returns `false` to suppress the original method entirely.

If `"Destroyed RV"` is not found, logs an info message and still returns `false` (suppresses the explosion).

### Gotcha: child name casing

Two distinct GameObjects are manipulated: `"Destroyed RV"` (the outer wrapper, activated) and `"destroyed rv"` (a nested child, deactivated). The names differ only in case — care needed if these are ever referenced by string elsewhere.
