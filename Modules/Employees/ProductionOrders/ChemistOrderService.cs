using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppFishNet;
using Il2CppScheduleOne;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Il2CppChemist = Il2CppScheduleOne.Employees.Chemist;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Employees.ProductionOrders
{
    // The production-order feature's host-authoritative state + API, owned by the Employees module (gated by
    // ModEmployees.Configuration.ChemistOrders). Static because the order store, history and orchestrator are
    // process-global game state; ModEmployees.Apply() forwards Reset() per save load. Lets a chemist fulfil a
    // "produce N of product Y" order — fetch base + mixers from assigned shelves, mix step-by-step across its
    // mixing stations (intermediates fed back in), deposit the result at each station's destination.
    internal static class ChemistOrderService
    {
        internal static readonly SaveSlotStore<ChemistOrderState> Store = new("ChemistOrders", "chemist orders");
        private static readonly SaveSlotStore<List<OrderHistoryEntry>> HistoryStore = new("ChemistOrders", "chemist order history");
        private const string HistoryKey = "__history__";
        private const int HistoryCap = 12;

        private static readonly ChemistOrderOrchestrator Orchestrator = new();

        // Gated by the Employees module's ChemistOrders sub-config (independent of the employee-tuning Enabled).
        internal static ChemistOrdersConfiguration Config => Core.Get<ModEmployees>()?.Configuration?.ChemistOrders;
        internal static bool IsEnabled => Config != null && Config.Enabled;

        // A read-only product option for the order UI: id, name, drug family, listed-for-sale flag, icon, steps.
        public sealed class ProductOption
        {
            public string Id;
            public string Name;
            public EDrugType DrugType;
            public bool Listed;
            public Sprite Icon;
            public int Steps;
        }

        public sealed class ShelfOption
        {
            public string Guid;
            public string Name;
            public int ItemCount;
        }

        // Per-save state: drop cached routes / progress counters and re-read the order stores for this save.
        public static void Reset()
        {
            Orchestrator.Reset();
            Store.Unload();
            HistoryStore.Unload();
        }

        // Called from the Chemist.UpdateBehaviour prefix on every chemist tick. Returns true when an active
        // order took over the chemist's decision tick (the patch then skips the vanilla logic).
        public static bool RunOrder(Il2CppChemist chemist)
        {
            if (!IsEnabled)
                return false;
            return Orchestrator.Run(chemist);
        }

        // -------------------------------------------------------------------------------------------------
        //  Public order API (host-authoritative)
        // -------------------------------------------------------------------------------------------------

        public static ChemistOrderState GetOrder(Il2CppChemist chemist)
        {
            if (chemist == null)
                return null;
            try
            {
                return Store.TryGet(chemist.GUID.ToString(), out ChemistOrderState s) ? s : null;
            }
            catch { return null; }
        }

        public static bool TrySetOrder(Il2CppChemist chemist, string targetProductId, int quantity,
            List<string> shelfGuids, out string error)
        {
            error = "";
            if (!IsEnabled)
            {
                error = "Chemist production orders are disabled.";
                return false;
            }
            if (!InstanceFinder.IsServer)
            {
                error = "Only the host can set production orders.";
                return false;
            }
            if (chemist == null)
            {
                error = "No chemist selected.";
                return false;
            }
            if (string.IsNullOrEmpty(targetProductId))
            {
                error = "Pick a product to produce.";
                return false;
            }
            if (quantity <= 0)
            {
                error = "Quantity must be at least 1.";
                return false;
            }
            if (shelfGuids == null || shelfGuids.Count == 0)
            {
                error = "Assign at least one shelf for ingredients.";
                return false;
            }

            HashSet<string> available = AvailableIds(shelfGuids);
            List<OrderStep> chain = RecipeChainResolver.Resolve(targetProductId, available);
            if (chain == null || chain.Count == 0)
            {
                error = "That product can't be mixed from any recipe you've discovered.";
                return false;
            }

            ChemistOrderState order = new()
            {
                TargetProductId = targetProductId,
                TargetName = NameOf(targetProductId),
                Goal = quantity,
                Started = 0,
                ShelfGuids = new List<string>(shelfGuids),
                Chain = chain,
                Active = true,
            };

            string key;
            try { key = chemist.GUID.ToString(); }
            catch { error = "Couldn't identify the chemist."; return false; }

            Store.Set(key, order);
            Orchestrator.Reset(); // re-seed progress counting cleanly for the new order
            RecordHistory(order);

            // Reserve the shelf slots holding this order's ingredients immediately on assign.
            OrderSlotLocks.Maintain(chemist, NeededIds(order), ResolveShelfEntities(order.ShelfGuids));

            Log.Info($"[ChemistOrders] Order set for {SafeName(chemist)}: {quantity}x {order.TargetName} " +
                     $"({chain.Count}-step chain, {shelfGuids.Count} shelf/shelves).");
            return true;
        }

        public static void ClearOrder(Il2CppChemist chemist)
        {
            if (chemist == null)
                return;
            try
            {
                string key = chemist.GUID.ToString();
                if (Store.TryGet(key, out ChemistOrderState order) && order != null)
                    OrderSlotLocks.Release(chemist, ResolveShelfEntities(order.ShelfGuids));
                Store.Remove(key);
                Orchestrator.Reset();
                Log.Info($"[ChemistOrders] Order cleared for {SafeName(chemist)}.");
            }
            catch { /* ignore */ }
        }

        internal static void CompleteOrder(Il2CppChemist chemist, ChemistOrderState order, string key)
        {
            order.Active = false;
            OrderSlotLocks.Release(chemist, ResolveShelfEntities(order.ShelfGuids));
            Store.Remove(key);
            Log.Info($"[ChemistOrders] {SafeName(chemist)} finished producing {order.Goal}x " +
                     $"{(string.IsNullOrEmpty(order.TargetName) ? order.TargetProductId : order.TargetName)}.");
        }

        // Called by the orchestrator when a chemist with an order is fired/despawning, so the reserved shelf
        // locks don't linger with a dead owner.
        internal static void ReleaseAndClear(Il2CppChemist chemist, ChemistOrderState order, string key)
        {
            OrderSlotLocks.Release(chemist, ResolveShelfEntities(order.ShelfGuids));
            Store.Remove(key);
        }

        // Ingredient ids fetched from shelves for an order: the base product plus every mixer (intermediates are
        // produced in-station, never fetched).
        internal static HashSet<string> NeededIds(ChemistOrderState order)
        {
            HashSet<string> ids = new();
            if (order?.Chain == null || order.Chain.Count == 0)
                return ids;
            if (!string.IsNullOrEmpty(order.Chain[0].InputId))
                ids.Add(order.Chain[0].InputId);
            foreach (OrderStep step in order.Chain)
                if (!string.IsNullOrEmpty(step.MixerId))
                    ids.Add(step.MixerId);
            return ids;
        }

        internal static List<ITransitEntity> ResolveShelfEntities(IEnumerable<string> guids)
        {
            List<ITransitEntity> result = new();
            if (guids == null)
                return result;
            foreach (string guid in guids)
            {
                PlaceableStorageEntity shelf = ResolveShelf(guid);
                ITransitEntity entity = shelf != null ? shelf.TryCast<ITransitEntity>() : null;
                if (entity != null)
                    result.Add(entity);
            }
            return result;
        }

        // -------------------------------------------------------------------------------------------------
        //  Lookups used by the order UI
        // -------------------------------------------------------------------------------------------------

        // Non-empty shelves on the chemist's assigned property, with their item count, for the source picker.
        public static List<ShelfOption> GetNonEmptyShelves(Il2CppChemist chemist)
        {
            List<ShelfOption> result = new();
            if (chemist == null)
                return result;

            Property property = chemist.AssignedProperty;
            if (property == null || property.BuildableItems == null)
                return result;

            var items = property.BuildableItems;
            for (int i = 0; i < items.Count; i++)
            {
                BuildableItem item = items[i];
                if (item == null)
                    continue;
                PlaceableStorageEntity shelf = item.TryCast<PlaceableStorageEntity>();
                if (shelf == null || shelf.StorageEntity == null)
                    continue;

                int count = ShelfItemCount(shelf.StorageEntity);
                if (count <= 0)
                    continue; // only show shelves that actually contain something

                string guid;
                try { guid = shelf.GUID.ToString(); }
                catch { continue; }
                result.Add(new ShelfOption { Guid = guid, Name = ShelfName(shelf), ItemCount = count });
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // Every discovered product with a resolvable mix chain, enriched with drug family / listed flag / icon
        // for the product picker. Sorted by name.
        public static List<ProductOption> GetOrderableProductInfos()
        {
            List<ProductOption> result = new();
            var discovered = ProductManager.DiscoveredProducts;
            if (discovered == null)
                return result;

            HashSet<string> listed = new();
            var listedProducts = ProductManager.ListedProducts;
            if (listedProducts != null)
            {
                for (int i = 0; i < listedProducts.Count; i++)
                {
                    ProductDefinition lp = listedProducts[i];
                    if (lp != null && !string.IsNullOrEmpty(lp.ID))
                        listed.Add(lp.ID);
                }
            }

            foreach (ProductDefinition product in discovered)
            {
                if (product == null || string.IsNullOrEmpty(product.ID))
                    continue;
                List<OrderStep> chain = RecipeChainResolver.Resolve(product.ID, null);
                if (chain == null || chain.Count == 0)
                    continue;

                EDrugType drug = EDrugType.Marijuana;
                try { drug = product.DrugType; } catch { /* default */ }

                result.Add(new ProductOption
                {
                    Id = product.ID,
                    Name = product.Name,
                    DrugType = drug,
                    Listed = listed.Contains(product.ID),
                    Icon = SafeIcon(product),
                    Steps = chain.Count,
                });
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // -------------------------------------------------------------------------------------------------
        //  Order history
        // -------------------------------------------------------------------------------------------------

        // Past orders still valid for this chemist: every shelf still resolves and the product still has a chain.
        public static List<OrderHistoryEntry> GetHistory(Il2CppChemist chemist)
        {
            List<OrderHistoryEntry> valid = new();
            if (chemist == null)
                return valid;
            if (!HistoryStore.TryGet(HistoryKey, out List<OrderHistoryEntry> all) || all == null)
                return valid;

            bool hasStations = false;
            try { hasStations = chemist.configuration != null && chemist.configuration.MixStations != null && chemist.configuration.MixStations.Count > 0; }
            catch { hasStations = false; }
            if (!hasStations)
                return valid;

            foreach (OrderHistoryEntry e in all)
            {
                if (e == null || string.IsNullOrEmpty(e.TargetProductId) || e.ShelfGuids == null || e.ShelfGuids.Count == 0)
                    continue;
                bool shelvesValid = true;
                foreach (string g in e.ShelfGuids)
                {
                    if (ResolveShelf(g) == null) { shelvesValid = false; break; }
                }
                if (!shelvesValid)
                    continue;
                if (RecipeChainResolver.Resolve(e.TargetProductId, null) == null)
                    continue;
                valid.Add(e);
            }
            return valid;
        }

        private static void RecordHistory(ChemistOrderState order)
        {
            try
            {
                List<OrderHistoryEntry> all = HistoryStore.TryGet(HistoryKey, out List<OrderHistoryEntry> stored) && stored != null
                    ? stored : new List<OrderHistoryEntry>();

                OrderHistoryEntry entry = new(order);
                string key = entry.Key();
                all.RemoveAll((OrderHistoryEntry e) => e != null && e.Key() == key); // de-dup, move to front
                all.Insert(0, entry);
                while (all.Count > HistoryCap)
                    all.RemoveAt(all.Count - 1);

                HistoryStore.Set(HistoryKey, all);
            }
            catch (Exception e) { Log.Warning($"[ChemistOrders] Failed to record order history: {e.Message}"); }
        }

        // -------------------------------------------------------------------------------------------------
        //  Helpers
        // -------------------------------------------------------------------------------------------------

        internal static PlaceableStorageEntity ResolveShelf(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;
            try
            {
                BuildableItem item = GUIDManager.GetObject<BuildableItem>(new Il2CppSystem.Guid(guid));
                return item != null ? item.TryCast<PlaceableStorageEntity>() : null;
            }
            catch { return null; }
        }

        private static HashSet<string> AvailableIds(List<string> shelfGuids)
        {
            HashSet<string> ids = new();
            foreach (string guid in shelfGuids)
            {
                PlaceableStorageEntity shelf = ResolveShelf(guid);
                StorageEntity storage = shelf != null ? shelf.StorageEntity : null;
                if (storage == null || storage.ItemSlots == null)
                    continue;

                var slots = storage.ItemSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    ItemSlot slot = slots[i];
                    if (slot == null || slot.ItemInstance == null || slot.Quantity <= 0)
                        continue;
                    ItemDefinition def = slot.ItemInstance.Definition;
                    if (def != null && !string.IsNullOrEmpty(def.ID))
                        ids.Add(def.ID);
                }
            }
            return ids;
        }

        private static int ShelfItemCount(StorageEntity storage)
        {
            if (storage == null || storage.ItemSlots == null)
                return 0;
            int n = 0;
            var slots = storage.ItemSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                ItemSlot slot = slots[i];
                if (slot != null && slot.ItemInstance != null && slot.Quantity > 0)
                    n++;
            }
            return n;
        }

        private static Sprite SafeIcon(ProductDefinition product)
        {
            try { return product.Icon; }
            catch { return null; }
        }

        private static string NameOf(string productId)
        {
            try
            {
                ItemDefinition def = Registry.GetItem(productId);
                return def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : productId;
            }
            catch { return productId; }
        }

        private static string ShelfName(PlaceableStorageEntity shelf)
        {
            try
            {
                if (shelf.StorageEntity != null && !string.IsNullOrEmpty(shelf.StorageEntity.StorageEntityName))
                    return shelf.StorageEntity.StorageEntityName;
            }
            catch { /* fall through */ }
            return "Shelf";
        }

        private static string SafeName(Il2CppChemist chemist)
        {
            try { return chemist.fullName; }
            catch { return "chemist"; }
        }
    }
}
