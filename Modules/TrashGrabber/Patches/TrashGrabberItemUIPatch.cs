using HarmonyLib;
using Il2CppScheduleOne.UI.Items;
using UnityEngine;

namespace Lithium.Modules.TrashGrabber.Patches
{
    /// <summary>
    /// Fixes the hotbar/inventory slot fill percentage so it reflects the custom capacity instead of the
    /// vanilla cap of 20.
    ///
    /// <see cref="TrashGrabberItemUI.UpdateUI"/> sets its label to
    /// <c>FloorToInt(Clamp01(GetTotalSize() / 20f) * 100) + "%"</c>, so with a larger
    /// <see cref="ModTrashGrabberConfiguration.CustomCapacity"/> the slot reads 100% once 20 size-units are
    /// collected and stays pinned there. <c>UpdateUI</c> is a <c>virtual</c> override the UI refresh dispatches
    /// through the vtable (reliably patchable), so this postfix recomputes the label against the configured
    /// capacity after the original runs. Disabled module defers to vanilla.
    /// </summary>
    [HarmonyPatch(typeof(TrashGrabberItemUI), nameof(TrashGrabberItemUI.UpdateUI))]
    public static class TrashGrabberItemUIPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TrashGrabberItemUI __instance)
        {
            ModTrashGrabberConfiguration config = Core.Get<ModTrashGrabber>().Configuration;
            if (!config.Enabled)
                return;

            if (__instance.Destroyed || __instance.trashGrabberInstance == null || __instance.ValueLabel == null)
                return;

            int capacity = config.CustomCapacity > 0 ? config.CustomCapacity : 20;
            int percent = Mathf.FloorToInt(
                Mathf.Clamp01((float)__instance.trashGrabberInstance.GetTotalSize() / capacity) * 100f);

            __instance.ValueLabel.text = percent + "%";
        }
    }
}
