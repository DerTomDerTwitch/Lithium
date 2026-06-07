using HarmonyLib;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.Trash;

namespace Lithium.Modules.TrashGrabber.Patches
{
    /// <summary>
    /// Reimplements the trash-pickup gate so the custom grabber capacity is actually honoured.
    ///
    /// <see cref="TrashItem.Interacted"/> gates pickup on <c>Equippable_TrashGrabber.GetCapacity() &gt; 0</c>.
    /// That call site inlines <c>GetCapacity</c> (built-in cap of 20), so <c>EquippableTrashGrabberGetCapacityPatch</c>
    /// never affects whether a pickup is allowed. <c>Interacted</c> is an <c>onInteracted</c> UnityAction
    /// listener (un-inlinable), so this prefix replaces it and calls <c>GetCapacity()</c> through a fresh
    /// managed call, which routes through the wrapper and applies the capacity postfix. Disabled module
    /// defers to vanilla.
    /// </summary>
    [HarmonyPatch(typeof(TrashItem), nameof(TrashItem.Interacted))]
    public class TrashItemInteractedPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(TrashItem __instance)
        {
            if (!Core.Get<ModTrashGrabber>().Configuration.Enabled)
                return true;

            if (Equippable_TrashGrabber.IsEquipped && __instance.CanGoInContainer
                && Equippable_TrashGrabber.Instance.GetCapacity() > 0)
            {
                Equippable_TrashGrabber.Instance.PickupTrash(__instance);
            }

            return false;
        }
    }
}
