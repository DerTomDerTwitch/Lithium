using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Handover;
using UnityEngine;
using Il2CppAction = Il2CppSystem.Action;

namespace Lithium.Modules.Customers.Patches
{
    /// <summary>
    /// Expands the customer handover screen beyond its hardcoded 4 product slots.
    ///
    /// The slot count is NOT enforced by the <c>HandoverScreen.CUSTOMER_SLOT_COUNT = 4</c> const (that
    /// const is inlined and never read). The real count comes from two things set up in
    /// <c>HandoverScreen.Start</c>: the <c>CustomerSlots</c> backing array (<c>new ItemSlot[4]</c>) and
    /// the <c>ItemSlotUI</c> children discovered under <c>CustomerSlotContainer</c>. Every consumer
    /// (Close, ClearCustomerSlots, GetCustomerItems(Count/Value), GetError/GetWarning) iterates
    /// <c>CustomerSlots.Length</c>, so growing the array + adding matching slot UIs is sufficient — the
    /// whole screen and the server-side <c>Customer.ProcessHandoverServerSide</c> (which just receives the
    /// resulting item list) adapt automatically.
    ///
    /// We postfix <c>Start</c> — a MonoBehaviour lifecycle method, so reliably patchable in IL2CPP (unlike
    /// the inlinable private helpers). For each extra slot we clone the last existing slot UI, create a
    /// fresh <c>ItemSlot</c>, point its <c>onItemDataChanged</c> at the screen's private
    /// <c>CustomerItemsChanged</c> (so the Done button / success-chance / fair-price UI recomputes when a
    /// new slot changes) and THEN call <c>AssignSlot</c>, which combines the UI's own refresh handler on
    /// top — letting the game do the IL2CPP delegate-combine for us. Finally both arrays are swapped for
    /// the grown copies.
    ///
    /// This lever is independent of the Customers module's <c>Enabled</c>: it acts purely on
    /// <c>HandoverSlotCount</c> (4 = vanilla, no-op), so extra slots can be turned on without enabling the
    /// rest of the customer rework. The screen is local UI; no networking concern (a longer item list
    /// rides the existing handover RPCs).
    /// </summary>
    [HarmonyPatch(typeof(HandoverScreen), nameof(HandoverScreen.Start))]
    public static class HandoverSlotCountPatch
    {
        private const int VanillaSlotCount = 4;
        private const int MaxSlotCount = 12;

        // The screen's private per-change callback (updates Done button / success label / fair price).
        private static readonly MethodInfo CustomerItemsChanged =
            AccessTools.Method(typeof(HandoverScreen), "CustomerItemsChanged");

        // Roots the managed callbacks so IL2CPP interop can't GC the delegates it converts from them.
        private static readonly List<Action> RootedHandlers = new();

        [HarmonyPostfix]
        public static void Postfix(HandoverScreen __instance)
        {
            try
            {
                ModCustomers mod = Core.Get<ModCustomers>();
                if (mod == null)
                    return;

                int target = Mathf.Clamp(mod.Configuration.HandoverSlotCount, VanillaSlotCount, MaxSlotCount);

                Il2CppReferenceArray<ItemSlot> slots = __instance.CustomerSlots;
                Il2CppReferenceArray<ItemSlotUI> slotUIs = __instance.CustomerSlotUIs;
                if (slots == null || slotUIs == null)
                    return;

                int current = slots.Length;
                // Nothing to do (vanilla count), or the prefab gave us fewer UIs than slots — bail safely.
                if (target <= current || slotUIs.Length < current)
                    return;

                ItemSlotUI template = slotUIs[current - 1];
                Transform container = __instance.CustomerSlotContainer;
                if (template == null || container == null)
                    return;

                var newSlots = new Il2CppReferenceArray<ItemSlot>(target);
                var newSlotUIs = new Il2CppReferenceArray<ItemSlotUI>(target);
                for (int i = 0; i < current; i++)
                {
                    newSlots[i] = slots[i];
                    newSlotUIs[i] = slotUIs[i];
                }

                for (int i = current; i < target; i++)
                {
                    GameObject go = UnityEngine.Object.Instantiate(template.gameObject, container);
                    go.name = "CustomerSlot_Lithium_" + i;
                    go.transform.localScale = Vector3.one;
                    go.SetActive(true);

                    ItemSlotUI ui = go.GetComponent<ItemSlotUI>();
                    if (ui == null)
                    {
                        UnityEngine.Object.Destroy(go);
                        return;
                    }

                    ItemSlot slot = new ItemSlot();
                    // Wire the screen handler first; AssignSlot then combines the UI refresh handler on top.
                    slot.onItemDataChanged = MakeScreenHandler(__instance);
                    ui.AssignSlot(slot);

                    newSlots[i] = slot;
                    newSlotUIs[i] = ui;
                }

                __instance.CustomerSlots = newSlots;
                __instance.CustomerSlotUIs = newSlotUIs;

                Log.Info($"[Lithium] Handover screen expanded from {current} to {target} customer slots.");
            }
            catch (Exception e)
            {
                Log.Error($"[Lithium] HandoverSlotCountPatch failed: {e}");
            }
        }

        // Builds an IL2CPP Action that re-invokes the screen's CustomerItemsChanged for a given screen
        // instance, keeping the managed source delegate rooted against GC.
        private static Il2CppAction MakeScreenHandler(HandoverScreen screen)
        {
            if (CustomerItemsChanged == null)
                return null;

            Action handler = () =>
            {
                try { CustomerItemsChanged.Invoke(screen, null); }
                catch (Exception e) { Log.Error($"[Lithium] handover CustomerItemsChanged failed: {e}"); }
            };
            RootedHandlers.Add(handler);
            return DelegateSupport.ConvertDelegate<Il2CppAction>(handler);
        }
    }
}
