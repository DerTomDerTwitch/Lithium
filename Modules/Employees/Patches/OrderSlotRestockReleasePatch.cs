using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.ItemFramework;
using Lithium.Modules.Employees.ProductionOrders;

namespace Lithium.Modules.Employees.Patches
{
    // A chemist production order reserves its ingredient shelf slots with the game's own ItemSlot lock
    // (OrderSlotLocks, reason = OrderSlotLocks.Reason). That lock blocks ADDITIONS to the slot — ItemSlot.
    // SetStoredItem/ChangeQuantity refuse while IsLocked — which is what reserves it, but it also blocks any
    // auto-refill once the chemist empties the slot. The SmartRestock mod patches this same ItemSlot.ChangeQuantity
    // and, on full depletion, buys a refill and AddItem()s it the next frame; while our lock is on the emptied
    // slot that AddItem is silently refused, so the shelf never restocks.
    //
    // Fix: the instant an order-locked slot is emptied, drop our lock here — synchronously, in a postfix on the
    // very ChangeQuantity SmartRestock hooks, so it runs THIS frame, before SmartRestock's next-frame insert. The
    // orchestrator re-applies the lock on its next tick (OrderSlotLocks.Maintain) once the slot holds a needed
    // ingredient again, so the reservation is preserved while there is stock and only released across the
    // empty -> refill gap. Host-only: order locks are applied on the server (the orchestrator is host-only) and
    // the refill runs on the host, so the unlock must (and need only) happen there.
    [HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.ChangeQuantity))]
    internal static class OrderSlotRestockReleasePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemSlot __instance, int change)
        {
            try
            {
                // Cheapest exits first: only removals can empty a slot, and only our reserved slots carry the lock.
                if (__instance == null || change >= 0)
                    return;
                ItemSlotLock activeLock = __instance.ActiveLock;
                if (activeLock == null || activeLock.LockReason != OrderSlotLocks.Reason)
                    return;
                if (!InstanceFinder.IsServer)
                    return;
                // Act only when the change actually drained the slot to empty.
                if (__instance.ItemInstance != null && __instance.Quantity > 0)
                    return;

                __instance.RemoveLock();
            }
            catch (Exception e)
            {
                Log.Warning($"[ChemistOrders] Failed to release emptied order lock for restock: {e.Message}");
            }
        }
    }
}
