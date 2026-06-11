using System;
using System.Collections.Generic;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Storage;

namespace Lithium.Modules.Employees.ProductionOrders
{
    // Reserves the shelf slots an order's ingredients live in, by applying the game's own slot lock owned by the
    // chemist. A lock (ApplyLock) marks the slot reserved, blocks other employees from contending for it, and
    // blocks additions — while the chemist itself can still consume it (removal isn't blocked, and the fetch
    // path finds slots regardless of lock). Station slots are deliberately NOT locked: an active ItemSlot lock
    // makes the game skip the slot for insertion/output, which would break the chemist's own loading and the
    // station's output creation. Locks release when the order finishes, is cancelled, or the chemist is fired.
    internal static class OrderSlotLocks
    {
        public const string Reason = "Reserved for a Lithium production order";

        // Locks any not-yet-locked shelf slot holding a needed ingredient. Idempotent and cheap: the IsLocked
        // skip means an ApplyLock (a networked ServerRpc) only fires for a newly stocked slot, never every tick.
        public static void Maintain(Chemist chemist, HashSet<string> neededIds, List<ITransitEntity> shelves)
        {
            if (neededIds == null || neededIds.Count == 0 || shelves == null)
                return;
            NetworkObject owner = OwnerOf(chemist);
            if (owner == null)
                return;

            foreach (ITransitEntity shelf in shelves)
            {
                StorageEntity storage = Storage(shelf);
                if (storage == null || storage.ItemSlots == null)
                    continue;

                var slots = storage.ItemSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    ItemSlot slot = slots[i];
                    if (slot == null || slot.IsLocked)
                        continue;
                    if (slot.ItemInstance == null || slot.Quantity <= 0)
                        continue;
                    string id = slot.ItemInstance.Definition != null ? slot.ItemInstance.Definition.ID : null;
                    if (id != null && neededIds.Contains(id))
                    {
                        try { slot.ApplyLock(owner, Reason); }
                        catch (Exception e) { Log.Warning($"[ChemistOrders] ApplyLock failed: {e.Message}"); }
                    }
                }
            }
        }

        // Removes every lock this chemist owns on the given shelves (RemoveSlotLocks only clears the asker's own).
        public static void Release(Chemist chemist, List<ITransitEntity> shelves)
        {
            if (shelves == null)
                return;
            NetworkObject owner = OwnerOf(chemist);
            if (owner == null)
                return;

            foreach (ITransitEntity shelf in shelves)
            {
                if (shelf == null)
                    continue;
                try { shelf.RemoveSlotLocks(owner); }
                catch (Exception e) { Log.Warning($"[ChemistOrders] RemoveSlotLocks failed: {e.Message}"); }
            }
        }

        private static NetworkObject OwnerOf(Chemist chemist)
        {
            if (chemist == null)
                return null;
            try { return chemist.NetworkObject; }
            catch { return null; }
        }

        private static StorageEntity Storage(ITransitEntity shelf)
        {
            if (shelf == null)
                return null;
            PlaceableStorageEntity pse = shelf.TryCast<PlaceableStorageEntity>();
            return pse != null ? pse.StorageEntity : null;
        }
    }
}
