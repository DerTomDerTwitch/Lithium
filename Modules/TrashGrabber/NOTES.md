# TrashGrabber Module Notes

## ModTrashGrabber.cs

Module class and configuration for the equippable trash grabber.

**Config fields:**
- `CustomCapacity` (int, default `20`): The total number of trash items the grabber can hold. The vanilla default is lower; raising this lets the player pick up more trash before emptying.

`Apply()` is a no-op beyond the enabled guard — no prefab mutations are needed at scene load. All runtime behaviour is handled by the patch.

## Patches/EquippableTrashGrabberGetCapacityPatch.cs

**Patched method:** `Equippable_TrashGrabber.GetCapacity()` (postfix).

`GetCapacity` returns how many more items the grabber can still accept (i.e. remaining space, not total capacity). The postfix overrides `__result` with `CustomCapacity - __instance.trashGrabberInstance.GetTotalSize()`, replicating the same "remaining = max - current" arithmetic the original method uses but substituting the configured maximum.

**Gotcha:** `GetTotalSize()` is called on the live `trashGrabberInstance` (the runtime inventory object attached to the player's equipped item), not on the prefab. This means the override is applied per-use at pick-up time, so it correctly reflects items already in the grabber.
