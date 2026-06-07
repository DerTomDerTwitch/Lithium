using HarmonyLib;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.Trash;

namespace Lithium.Modules.TrashGrabber.Patches
{
    /// <summary>
    /// Hover-label counterpart to <see cref="TrashItemInteractedPatch"/>. <see cref="TrashItem.Hovered"/>
    /// shows "Pick up" vs "Bin is full" from an inlined <c>GetCapacity() &gt; 0</c> check, so the hover text
    /// would otherwise report "full" at the vanilla cap even when the custom capacity allows more.
    /// <c>Hovered</c> is an <c>onHovered</c> UnityAction listener (un-inlinable); this prefix reimplements
    /// it with a fresh <c>GetCapacity()</c> call so the label matches the actual (custom) capacity. Disabled
    /// module defers to vanilla.
    /// </summary>
    [HarmonyPatch(typeof(TrashItem), nameof(TrashItem.Hovered))]
    public class TrashItemHoveredPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(TrashItem __instance)
        {
            if (!Core.Get<ModTrashGrabber>().Configuration.Enabled)
                return true;

            if (Equippable_TrashGrabber.IsEquipped && __instance.CanGoInContainer)
            {
                InteractableObject intObj = __instance.Draggable.IntObj;
                if (Equippable_TrashGrabber.Instance.GetCapacity() > 0)
                {
                    intObj.SetMessage("Pick up");
                    intObj.SetInteractableState(InteractableObject.EInteractableState.Default);
                }
                else
                {
                    intObj.SetMessage("Bin is full");
                    intObj.SetInteractableState(InteractableObject.EInteractableState.Invalid);
                }
            }

            return false;
        }
    }
}
