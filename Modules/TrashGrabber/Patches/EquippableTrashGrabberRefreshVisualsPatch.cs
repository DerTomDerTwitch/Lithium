using HarmonyLib;
using Il2CppScheduleOne.Equipping;
using UnityEngine;

namespace Lithium.Modules.TrashGrabber.Patches
{
    /// <summary>
    /// Fixes the bin fill indicator so it reflects the custom capacity rather than the vanilla cap of 20.
    ///
    /// <see cref="Equippable_TrashGrabber.RefreshVisuals"/> lerps the visible trash level by
    /// <c>GetTotalSize() / 20f</c> (clamped to 0..1). With a larger <see cref="ModTrashGrabberConfiguration.CustomCapacity"/>
    /// the bin would scale up like vanilla and then sit pinned at 100% from 20 items until the real capacity is full.
    /// <c>RefreshVisuals</c> is bound to the item's <c>onDataChanged</c> event (un-inlinable), so this prefix
    /// reimplements it dividing by the configured capacity instead. Disabled module defers to vanilla.
    /// </summary>
    [HarmonyPatch(typeof(Equippable_TrashGrabber), nameof(Equippable_TrashGrabber.RefreshVisuals))]
    public static class EquippableTrashGrabberRefreshVisualsPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Equippable_TrashGrabber __instance)
        {
            ModTrashGrabberConfiguration config = Core.Get<ModTrashGrabber>().Configuration;
            if (!config.Enabled)
                return true;

            int capacity = config.CustomCapacity > 0 ? config.CustomCapacity : 20;
            float num = Mathf.Clamp01((float)__instance.trashGrabberInstance.GetTotalSize() / capacity);

            __instance.TrashContent.localPosition = Vector3.Lerp(
                __instance.TrashContent_Min.localPosition, __instance.TrashContent_Max.localPosition, num);
            __instance.TrashContent.localScale = Vector3.Lerp(
                __instance.TrashContent_Min.localScale, __instance.TrashContent_Max.localScale, num);
            __instance.TrashContent.gameObject.SetActive(num > 0f);

            return false;
        }
    }
}
