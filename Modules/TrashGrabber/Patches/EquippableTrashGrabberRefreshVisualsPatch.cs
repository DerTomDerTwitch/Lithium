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
    /// the bin scales up like vanilla and then sits pinned at 100% from 20 items until the real capacity is full.
    ///
    /// We do NOT patch <c>RefreshVisuals</c> itself: it is a <c>private</c> method whose only live caller in the
    /// AOT build is the <c>onDataChanged</c> delegate (<c>[CallerCount(1)]</c>), and that delegate-dispatch path
    /// does not route through the Harmony detour, so a prefix there silently never runs (the visual keeps using
    /// the vanilla <c>/20f</c>). Instead we postfix <see cref="Equippable_TrashGrabber.Update"/> — a
    /// <c>MonoBehaviour</c> lifecycle method that is reliably patchable and runs every frame while the grabber is
    /// equipped — and re-drive the same TrashContent lerp using the configured capacity. Disabled module defers to
    /// vanilla.
    /// </summary>
    [HarmonyPatch(typeof(Equippable_TrashGrabber), nameof(Equippable_TrashGrabber.Update))]
    public static class EquippableTrashGrabberRefreshVisualsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Equippable_TrashGrabber __instance)
        {
            ModTrashGrabberConfiguration config = Core.Get<ModTrashGrabber>().Configuration;
            if (!config.Enabled)
                return;

            if (__instance.trashGrabberInstance == null || __instance.TrashContent == null)
                return;

            int capacity = config.CustomCapacity > 0 ? config.CustomCapacity : 20;
            float num = Mathf.Clamp01((float)__instance.trashGrabberInstance.GetTotalSize() / capacity);

            __instance.TrashContent.localPosition = Vector3.Lerp(
                __instance.TrashContent_Min.localPosition, __instance.TrashContent_Max.localPosition, num);
            __instance.TrashContent.localScale = Vector3.Lerp(
                __instance.TrashContent_Min.localScale, __instance.TrashContent_Max.localScale, num);
            __instance.TrashContent.gameObject.SetActive(num > 0f);
        }
    }
}
