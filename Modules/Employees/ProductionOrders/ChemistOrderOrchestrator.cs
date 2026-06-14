using System;
using System.Collections.Generic;
using Il2CppFishNet;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Storage;

namespace Lithium.Modules.Employees.ProductionOrders
{
    // The host-side brain that fulfils a chemist's production order. Driven once per chemist per tick from the
    // Chemist.UpdateBehaviour prefix (host only). It reuses the vanilla NPC behaviour instances exactly as the
    // game does — there is no generic step-sequencer, so we re-derive the single next action from live world
    // state each tick (the same reactive pattern as Chemist.TryStartNewTask) and drive:
    //   - Employee.MoveItemBehaviour          : haul base/mixer from a shelf into a station, deliver target out
    //   - Chemist.StartMixingStationBehaviour : walk to the station and cook (loads + SendMixingOperation)
    // Intermediate outputs are pushed straight back into the station's product slot. Stations mix on their own
    // clock once started, so several run in parallel while the single chemist round-robins loading them.
    internal sealed class ChemistOrderOrchestrator
    {
        // Cached ad-hoc shelf->station routes, reused across ticks so we don't leak a TimeManager.onFixedUpdate
        // subscription per fetch (TransitRoute's ctor subscribes one; Destroy() unsubscribes).
        private readonly Dictionary<string, TransitRoute> _routes = new();

        // Per-chemist set of station GUIDs whose current final-stage mix has already been counted into
        // order.Started, so each final batch counts exactly once. Seeded on first sight / after load.
        private readonly Dictionary<string, HashSet<string>> _countedFinal = new();
        private readonly HashSet<string> _seeded = new();

        public void Reset()
        {
            foreach (TransitRoute route in _routes.Values)
            {
                try { route?.Destroy(); } catch { /* scene already torn down */ }
            }
            _routes.Clear();
            _countedFinal.Clear();
            _seeded.Clear();
        }

        // -------------------------------------------------------------------------------------------------
        //  Stop / drain (host-only)
        // -------------------------------------------------------------------------------------------------

        // Cleanly winds down an order the player stopped: disables any in-flight order behaviour, then deposits
        // the chemist's carried order items and every assigned station's loaded slots (output, then product, then
        // mixer) into the "designated output" — each station's configured destination route if set, otherwise an
        // assigned shelf. Items with nowhere to go are left in place rather than destroyed; a station that is
        // mid-cook is left to finish (its operation can't be cleanly extracted). The caller releases the reserved
        // shelf-slot locks BEFORE this runs so those slots can receive the drained ingredients.
        public void Drain(Chemist chemist, ChemistOrderState order, List<ITransitEntity> shelves)
        {
            if (chemist == null || order == null || !InstanceFinder.IsServer)
                return;
            try
            {
                DisableOrderBehaviours(chemist);

                var stations = chemist.configuration?.MixStations;

                // Prioritized deposit targets: each station's configured destination first, then the shelves.
                List<ITransitEntity> outputs = new();
                if (stations != null)
                {
                    for (int i = 0; i < stations.Count; i++)
                    {
                        ITransitEntity dest = StationDestination(stations[i]);
                        if (dest != null && !outputs.Contains(dest))
                            outputs.Add(dest);
                    }
                }
                if (shelves != null)
                    foreach (ITransitEntity sh in shelves)
                        if (sh != null && !outputs.Contains(sh))
                            outputs.Add(sh);

                // Empty each station's loaded slots into the outputs.
                if (stations != null)
                {
                    for (int i = 0; i < stations.Count; i++)
                    {
                        MixingStation station = stations[i];
                        if (station == null)
                            continue;
                        DepositSlot(chemist, station.OutputSlot, outputs);
                        DepositSlot(chemist, station.ProductSlot, outputs);
                        DepositSlot(chemist, station.MixerSlot, outputs);
                    }
                }

                // Empty the order items the chemist is carrying.
                DrainInventory(chemist, order, outputs);
            }
            catch (Exception e)
            {
                Log.Warning($"[ChemistOrders] Drain failed for {SafeName(chemist)}: {e.Message}");
            }
        }

        private static void DisableOrderBehaviours(Chemist chemist)
        {
            try
            {
                MoveItemBehaviour move = chemist.MoveItemBehaviour;
                if (move != null && (move.Active || move.Enabled))
                    move.Disable_Networked(null);
            }
            catch (Exception e) { Log.Warning($"[ChemistOrders] Disabling move behaviour failed: {e.Message}"); }
            try
            {
                StartMixingStationBehaviour mix = chemist.StartMixingStationBehaviour;
                if (mix != null && (mix.Active || mix.Enabled))
                    mix.Disable_Networked(null);
            }
            catch (Exception e) { Log.Warning($"[ChemistOrders] Disabling mix behaviour failed: {e.Message}"); }
        }

        private static ITransitEntity StationDestination(MixingStation station)
        {
            if (station == null)
                return null;
            try
            {
                MixingStationConfiguration cfg = station.Configuration?.TryCast<MixingStationConfiguration>();
                TransitRoute route = cfg != null ? cfg.DestinationRoute : null;
                if (route != null && route.AreEntitiesNonNull())
                    return route.Destination;
            }
            catch { /* no usable destination */ }
            return null;
        }

        private static void DrainInventory(Chemist chemist, ChemistOrderState order, List<ITransitEntity> outputs)
        {
            NPCInventory inv;
            try { inv = chemist.Inventory; }
            catch { return; }
            if (inv == null || inv.ItemSlots == null)
                return;

            HashSet<string> chainIds = ChainItemIds(order);
            var slots = inv.ItemSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                ItemSlot slot = slots[i];
                if (slot == null || slot.ItemInstance == null || slot.Quantity <= 0)
                    continue;
                // Only return items this order put in the chemist's hands; never touch unrelated carried items.
                string id = DefId(slot.ItemInstance);
                if (id == null || !chainIds.Contains(id))
                    continue;
                DepositSlot(chemist, slot, outputs);
            }
        }

        private static HashSet<string> ChainItemIds(ChemistOrderState order)
        {
            HashSet<string> ids = new();
            if (order == null || order.Chain == null)
                return ids;
            if (!string.IsNullOrEmpty(order.TargetProductId))
                ids.Add(order.TargetProductId);
            for (int i = 0; i < order.Chain.Count; i++)
            {
                OrderStep s = order.Chain[i];
                if (!string.IsNullOrEmpty(s.InputId)) ids.Add(s.InputId);
                if (!string.IsNullOrEmpty(s.MixerId)) ids.Add(s.MixerId);
                if (!string.IsNullOrEmpty(s.OutputId)) ids.Add(s.OutputId);
            }
            return ids;
        }

        // Moves a slot's contents into the first deposit target(s) with capacity, leaving any remainder in place.
        // Capacity is probed with asker=null (only unlocked, truly-free slots count), so the subsequent insert —
        // which skips locked slots — places exactly the amount we then deduct from the source.
        private static void DepositSlot(Chemist chemist, ItemSlot slot, List<ITransitEntity> outputs)
        {
            if (slot == null || outputs == null || outputs.Count == 0)
                return;

            int guard = 0;
            while (slot.ItemInstance != null && slot.Quantity > 0 && guard++ < 128)
            {
                int before = slot.Quantity;
                ItemInstance inst = slot.ItemInstance;

                foreach (ITransitEntity dest in outputs)
                {
                    if (dest == null)
                        continue;
                    int remaining = slot.Quantity;
                    if (remaining <= 0)
                        break;

                    int cap;
                    try { cap = dest.GetInputCapacityForItem(inst.GetCopy(remaining), null); }
                    catch { cap = 0; }
                    int put = Math.Min(remaining, cap);
                    if (put <= 0)
                        continue;

                    try
                    {
                        dest.InsertItemIntoInput(inst.GetCopy(put), chemist);
                        slot.ChangeQuantity(-put);
                    }
                    catch (Exception e) { Log.Warning($"[ChemistOrders] Deposit failed: {e.Message}"); }
                }

                if (slot.Quantity >= before) // nothing accepted this pass — no point looping further
                    break;
            }
        }

        // Returns true when this chemist has an active order and we took over its decision tick (the caller
        // then suppresses the vanilla logic). Returns false to let vanilla run (no order, or the chemist can't
        // work — vanilla still handles wait-outside / pay / end-of-day in that case).
        public bool Run(Chemist chemist)
        {
            if (chemist == null || !InstanceFinder.IsServer)
                return false;

            string key;
            try { key = chemist.GUID.ToString(); }
            catch { return false; }

            if (!ChemistOrderService.Store.TryGet(key, out ChemistOrderState order) || order == null || !order.Active)
                return false;

            // Fired: release the reserved shelf locks (before the chemist despawns and the owner goes stale) and
            // drop the order. Checked before CanWork(), which a fired chemist fails (its config was reset).
            bool fired;
            try { fired = chemist.Fired; }
            catch { fired = false; }
            if (fired)
            {
                ChemistOrderService.ReleaseAndClear(chemist, order, key);
                _seeded.Remove(key);
                _countedFinal.Remove(key);
                return false;
            }

            // Defer to vanilla for the non-working states (unpaid / no locker / 4AM). CanWork() == true means
            // home + paid + not end-of-day, so taking over here never starves pay collection or wait-outside.
            if (!chemist.CanWork())
                return false;

            try
            {
                UpdateProgress(chemist, order, key);

                if (order.Goal <= 0 || order.Chain == null || order.Chain.Count == 0)
                {
                    chemist.SubmitNoWorkReason("My production order is invalid.", string.Empty);
                    return true;
                }

                List<ITransitEntity> shelves = ResolveShelves(order);

                // Keep the reserved-slot locks fresh (picks up newly stocked ingredient slots; idempotent/cheap).
                OrderSlotLocks.Maintain(chemist, ChemistOrderService.NeededIds(order), shelves);

                if (DrainedAndComplete(chemist, order))
                {
                    ChemistOrderService.CompleteOrder(chemist, order, key);
                    return true;
                }

                if (IsBusy(chemist))
                {
                    chemist.MarkIsWorking();
                    return true;
                }

                PlanOneAction(chemist, order, shelves);
            }
            catch (Exception e)
            {
                Log.Warning($"[ChemistOrders] Orchestration failed for {SafeName(chemist)}: {e.Message}");
            }

            return true;
        }

        // True while one of the two behaviours we drive is enabled/active (the chemist is mid-action). Checking
        // Enabled as well as Active closes the one-tick gap between Enable_Networked and the scheduler activating
        // it, so we never dispatch a second action on top of a pending one.
        private static bool IsBusy(Chemist chemist)
        {
            MoveItemBehaviour move = chemist.MoveItemBehaviour;
            StartMixingStationBehaviour mix = chemist.StartMixingStationBehaviour;
            if (move != null && (move.Active || move.Enabled))
                return true;
            if (mix != null && (mix.Active || mix.Enabled))
                return true;
            return false;
        }

        // -------------------------------------------------------------------------------------------------
        //  Progress accounting
        // -------------------------------------------------------------------------------------------------

        private void UpdateProgress(Chemist chemist, ChemistOrderState order, string key)
        {
            if (!_countedFinal.TryGetValue(key, out HashSet<string> counted))
            {
                counted = new HashSet<string>();
                _countedFinal[key] = counted;
            }

            bool seeding = _seeded.Add(key);
            bool changed = false;

            var stations = chemist.configuration?.MixStations;
            if (stations == null)
                return;

            for (int i = 0; i < stations.Count; i++)
            {
                MixingStation station = stations[i];
                if (station == null)
                    continue;

                string sguid = SafeGuid(station);
                if (sguid == null)
                    continue;

                bool finalNow = IsFinalOp(station.CurrentMixOperation, order);

                if (finalNow)
                {
                    if (counted.Add(sguid) && !seeding)
                    {
                        order.Started += station.CurrentMixOperation.Quantity;
                        changed = true;
                    }
                }
                else
                {
                    counted.Remove(sguid);
                }
            }

            if (changed)
                ChemistOrderService.Store.Set(key, order);
        }

        // The order is finished once every target unit has been committed to a final mix AND no chain work is
        // still in flight on a station (cooking, an intermediate/target output, or a chain input loaded). The
        // planner keeps draining these (P1-P4 run regardless of Started vs Goal), so this terminates.
        private bool DrainedAndComplete(Chemist chemist, ChemistOrderState order)
        {
            if (order.Started < order.Goal)
                return false;

            var stations = chemist.configuration?.MixStations;
            if (stations == null)
                return true;

            for (int i = 0; i < stations.Count; i++)
            {
                MixingStation station = stations[i];
                if (station == null)
                    continue;

                if (station.CurrentMixOperation != null)
                    return false;
                if (station.OutputSlot != null && station.OutputSlot.Quantity > 0 &&
                    IsChainProduct(order, DefId(station.OutputSlot.ItemInstance)))
                    return false;
                if (station.ProductSlot != null && station.ProductSlot.Quantity > 0 &&
                    IsChainInput(order, DefId(station.ProductSlot.ItemInstance)))
                    return false;
            }
            return true;
        }

        private static bool IsChainProduct(ChemistOrderState order, string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            if (id == order.TargetProductId)
                return true;
            for (int i = 0; i < order.Chain.Count; i++)
                if (order.Chain[i].OutputId == id || order.Chain[i].InputId == id)
                    return true;
            return false;
        }

        private static bool IsChainInput(ChemistOrderState order, string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            for (int i = 0; i < order.Chain.Count; i++)
                if (order.Chain[i].InputId == id)
                    return true;
            return false;
        }

        // -------------------------------------------------------------------------------------------------
        //  Action planning (one action per tick, by priority)
        // -------------------------------------------------------------------------------------------------

        private void PlanOneAction(Chemist chemist, ChemistOrderState order, List<ITransitEntity> shelves)
        {
            var stations = chemist.configuration?.MixStations;
            if (stations == null || stations.Count == 0)
            {
                chemist.SubmitNoWorkReason("I haven't been assigned a mixing station.",
                    "Use your management clipboard to assign me a mixing station.");
                return;
            }

            List<StationView> views = new();
            for (int i = 0; i < stations.Count; i++)
            {
                StationView v = StationView.Build(stations[i]);
                if (v != null)
                    views.Add(v);
            }

            string target = order.TargetProductId;
            List<OrderStep> chain = order.Chain;

            // P1: start a station that is fully loaded for its stage (gets it cooking; output frees for parallel work).
            foreach (StationView v in views)
            {
                if (v.Op != null || v.OutQty > 0 || v.ProdQty <= 0)
                    continue;
                int stage = StageForInput(chain, v.ProdId);
                if (stage < 0)
                    continue;
                if (v.MixId == chain[stage].MixerId && v.MixQty >= v.ProdQty)
                {
                    StartMix(chemist, v);
                    return;
                }
            }

            // P2: advance a finished intermediate back into the product slot for its next stage (instant).
            foreach (StationView v in views)
            {
                if (v.Op != null || v.OutQty <= 0 || v.ProdQty > 0)
                    continue;
                if (v.OutId == target)
                    continue;
                int producedStage = OutputStage(chain, v.OutId);
                if (producedStage >= 0 && producedStage + 1 < chain.Count)
                {
                    AdvanceIntermediate(v);
                    return;
                }
            }

            // P3: deliver finished target output. Prefer the station's configured destination; if none is set,
            // fall back to depositing the product on the first assigned shelf so the order can still complete
            // (the target is never re-used as an input, so this never feeds back into production).
            foreach (StationView v in views)
            {
                if (v.Op != null || v.OutQty <= 0 || v.OutId != target)
                    continue;

                ItemInstance outputItem = v.Station.OutputSlot.ItemInstance;
                if (v.Cfg != null && v.Cfg.DestinationRoute != null && v.Cfg.DestinationRoute.AreEntitiesNonNull())
                {
                    if (Haul(chemist, v.Cfg.DestinationRoute, outputItem, -1))
                        return;
                }
                else if (shelves.Count > 0)
                {
                    ITransitEntity shelf = shelves[0];
                    TransitRoute route = GetRoute(v.Entity, shelf, v.Guid, ShelfGuid(shelf));
                    if (route != null && Haul(chemist, route, outputItem, -1))
                        return;
                }
            }

            // P4: top up the mixer for a station whose product is loaded but mixer is short.
            foreach (StationView v in views)
            {
                if (v.Op != null || v.OutQty > 0 || v.ProdQty <= 0)
                    continue;
                int stage = StageForInput(chain, v.ProdId);
                if (stage < 0)
                    continue;
                string mixerId = chain[stage].MixerId;
                if (v.MixId != null && v.MixId != mixerId)
                    continue; // foreign mixer in the slot — leave it for the player to clear
                if (v.MixQty >= v.ProdQty)
                    continue;
                if (FetchInto(chemist, shelves, v, mixerId, v.ProdQty - v.MixQty))
                    return;
            }

            // P5: begin a new batch on a fully free station, bounded so we don't overproduce.
            int committed = 0;
            foreach (StationView v in views)
                committed += InFlightUnits(v, order);

            int remainingToStart = order.Goal - order.Started - committed;
            if (remainingToStart > 0)
            {
                // Only begin a batch we can take all the way to the target: bound it by the full-chain ingredient
                // stock (base + every mixer, counting repeats). This guarantees each stage's mixer can match the
                // loaded product, so no batch ever strands a half-mixed intermediate.
                Dictionary<string, int> shelfStock = ShelfCounts(shelves);
                int affordable = MaxBatchByIngredients(order, shelfStock);

                foreach (StationView v in views)
                {
                    if (v.Op != null || v.OutQty > 0 || v.ProdQty > 0 || v.MixQty > 0)
                        continue;
                    string baseId = chain[0].InputId;
                    int batch = Math.Min(Math.Min(v.MaxBatch, remainingToStart), affordable);
                    if (batch <= 0)
                        break;
                    if (FetchInto(chemist, shelves, v, baseId, batch))
                        return;
                }
            }

            // Nothing actionable: out of ingredients, or every station is busy mixing. Surface a reason naming
            // exactly which chain ingredient is short, so the player knows what to stock (and we can tell a real
            // detection bug from an empty shelf).
            chemist.SubmitNoWorkReason(MissingReason(order, shelves), "Stock the assigned shelves with the ingredients.");
        }

        // Units of this station's batch that will still become NEW target output (not yet committed to a final
        // mix, so not yet in order.Started). Used to bound how many new batches we start.
        private int InFlightUnits(StationView v, ChemistOrderState order)
        {
            if (v.Op != null)
                return IsFinalOp(v.Op, order) ? 0 : v.Op.Quantity;
            if (v.OutQty > 0)
                return v.OutId == order.TargetProductId ? 0 : v.OutQty; // finished target already counted in Started
            return v.ProdQty;
        }

        // -------------------------------------------------------------------------------------------------
        //  Actions
        // -------------------------------------------------------------------------------------------------

        private void StartMix(Chemist chemist, StationView v)
        {
            float threshold = 1f;
            try { if (v.Cfg?.StartThrehold != null) threshold = v.Cfg.StartThrehold.Value; }
            catch { /* keep default */ }

            if (v.ProdQty >= threshold)
            {
                chemist.StartMixingStationBehaviour.AssignStation(v.Station);
                chemist.StartMixingStationBehaviour.Enable_Networked();
                return;
            }

            // Final partial batch smaller than the player-set start threshold: the vanilla behaviour would
            // refuse it, so commit the mix directly (server-side, exactly what the cook routine does).
            DirectStartMix(v);
        }

        private static void DirectStartMix(StationView v)
        {
            MixingStation station = v.Station;
            ItemInstance product = station.ProductSlot.ItemInstance;
            ItemInstance mixer = station.MixerSlot.ItemInstance;
            if (product == null || mixer == null)
                return;

            int q = Math.Min(Math.Min(v.ProdQty, v.MixQty), v.MaxBatch);
            if (q <= 0)
                return;

            EQuality quality = EQuality.Standard;
            QualityItemInstance qii = product.TryCast<QualityItemInstance>();
            if (qii != null)
                quality = qii.Quality;

            station.ProductSlot.ChangeQuantity(-q);
            station.MixerSlot.ChangeQuantity(-q);
            MixOperation op = new MixOperation(DefId(product), quality, DefId(mixer), q);
            station.SendMixingOperation(op, 0);
        }

        // Push the whole intermediate output back into the (empty) product slot so the next stage mixes from it.
        private static void AdvanceIntermediate(StationView v)
        {
            MixingStation station = v.Station;
            ItemInstance outInst = station.OutputSlot.ItemInstance;
            if (outInst == null)
                return;
            int q = station.OutputSlot.Quantity;
            if (q <= 0)
                return;

            ItemInstance copy = outInst.GetCopy(q);
            station.ProductSlot.InsertItem(copy);   // product slot is empty (checked) and accepts unpackaged products
            station.OutputSlot.ChangeQuantity(-q);  // output slot is add-locked, not removal-locked
        }

        // Locate `itemId` on the assigned shelves and dispatch a MoveItemBehaviour haul of up to `amount` into
        // the station's matching input slot. Returns true if a haul was started.
        private bool FetchInto(Chemist chemist, List<ITransitEntity> shelves, StationView v, string itemId, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(itemId))
                return false;

            ITransitEntity shelf = FindShelfWith(shelves, itemId, out ItemInstance template);
            if (shelf == null || template == null)
                return false;

            TransitRoute route = GetRoute(shelf, v.Entity, ShelfGuid(shelf), v.Guid);
            if (route == null)
                return false;

            return Haul(chemist, route, template.GetCopy(1), amount);
        }

        private static bool Haul(Chemist chemist, TransitRoute route, ItemInstance template, int maxAmount)
        {
            MoveItemBehaviour move = chemist.MoveItemBehaviour;
            if (move == null || route == null || template == null)
                return false;

            string reason;
            if (!move.IsTransitRouteValid(route, template, out reason))
                return false;

            move.Initialize(route, template, maxAmount);
            move.Enable_Networked();
            return true;
        }

        // -------------------------------------------------------------------------------------------------
        //  Helpers
        // -------------------------------------------------------------------------------------------------

        private TransitRoute GetRoute(ITransitEntity src, ITransitEntity dst, string srcGuid, string dstGuid)
        {
            if (src == null || dst == null || srcGuid == null || dstGuid == null)
                return null;

            string k = srcGuid + "->" + dstGuid;
            if (_routes.TryGetValue(k, out TransitRoute existing))
            {
                if (existing != null && existing.Source != null && existing.Destination != null)
                    return existing;
                try { existing?.Destroy(); } catch { /* ignore */ }
            }

            TransitRoute route = new TransitRoute(src, dst);
            _routes[k] = route;
            return route;
        }

        private static List<ITransitEntity> ResolveShelves(ChemistOrderState order)
        {
            List<ITransitEntity> result = new();
            if (order.ShelfGuids == null)
                return result;

            foreach (string guid in order.ShelfGuids)
            {
                PlaceableStorageEntity shelf = ChemistOrderService.ResolveShelf(guid);
                if (shelf == null)
                    continue;
                ITransitEntity entity = shelf.TryCast<ITransitEntity>();
                if (entity != null)
                    result.Add(entity);
            }
            return result;
        }

        private static ITransitEntity FindShelfWith(List<ITransitEntity> shelves, string itemId, out ItemInstance template)
        {
            template = null;
            foreach (ITransitEntity shelf in shelves)
            {
                StorageEntity storage = ShelfStorage(shelf);
                if (storage == null || storage.ItemSlots == null)
                    continue;

                var slots = storage.ItemSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    ItemSlot slot = slots[i];
                    if (slot == null || slot.ItemInstance == null || slot.Quantity <= 0)
                        continue;
                    if (DefId(slot.ItemInstance) == itemId)
                    {
                        template = slot.ItemInstance;
                        return shelf;
                    }
                }
            }
            return null;
        }

        private static StorageEntity ShelfStorage(ITransitEntity shelf)
        {
            if (shelf == null)
                return null;
            PlaceableStorageEntity pse = shelf.TryCast<PlaceableStorageEntity>();
            return pse != null ? pse.StorageEntity : null;
        }

        private static string ShelfGuid(ITransitEntity shelf)
        {
            PlaceableStorageEntity pse = shelf?.TryCast<PlaceableStorageEntity>();
            if (pse == null)
                return null;
            try { return pse.GUID.ToString(); }
            catch { return null; }
        }

        // Largest batch the assigned shelves can fully supply through every stage at once: bounded by
        // floor(available / required) over the base product and each mixer (counting a mixer used in N stages
        // as needing N per unit). Intermediates aren't counted — they're produced in-station, not fetched.
        private static int MaxBatchByIngredients(ChemistOrderState order, Dictionary<string, int> shelfStock)
        {
            Dictionary<string, int> required = new();
            void Add(string id)
            {
                if (string.IsNullOrEmpty(id))
                    return;
                required[id] = required.TryGetValue(id, out int c) ? c + 1 : 1;
            }

            Add(order.Chain[0].InputId);
            for (int i = 0; i < order.Chain.Count; i++)
                Add(order.Chain[i].MixerId);

            int batch = int.MaxValue;
            foreach (KeyValuePair<string, int> need in required)
            {
                int have = shelfStock.TryGetValue(need.Key, out int a) ? a : 0;
                int canDo = need.Value > 0 ? have / need.Value : 0;
                if (canDo < batch)
                    batch = canDo;
            }
            return batch == int.MaxValue ? 0 : Math.Max(0, batch);
        }

        private static Dictionary<string, int> ShelfCounts(List<ITransitEntity> shelves)
        {
            Dictionary<string, int> counts = new();
            foreach (ITransitEntity shelf in shelves)
            {
                StorageEntity storage = ShelfStorage(shelf);
                if (storage == null || storage.ItemSlots == null)
                    continue;
                var slots = storage.ItemSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    ItemSlot slot = slots[i];
                    if (slot == null || slot.ItemInstance == null || slot.Quantity <= 0)
                        continue;
                    string id = DefId(slot.ItemInstance);
                    if (id == null)
                        continue;
                    counts[id] = counts.TryGetValue(id, out int c) ? c + slot.Quantity : slot.Quantity;
                }
            }
            return counts;
        }

        private bool IsFinalOp(MixOperation op, ChemistOrderState order)
        {
            if (op == null || order.Chain == null || order.Chain.Count == 0)
                return false;

            string prod = op.ProductID;
            string ing = op.IngredientID;
            for (int i = 0; i < order.Chain.Count; i++)
            {
                OrderStep step = order.Chain[i];
                if (step.InputId == prod && step.MixerId == ing)
                    return i == order.Chain.Count - 1;
            }
            return false;
        }

        private static int StageForInput(List<OrderStep> chain, string inputId)
        {
            for (int i = 0; i < chain.Count; i++)
                if (chain[i].InputId == inputId)
                    return i;
            return -1;
        }

        private static int OutputStage(List<OrderStep> chain, string outputId)
        {
            for (int i = 0; i < chain.Count; i++)
                if (chain[i].OutputId == outputId)
                    return i;
            return -1;
        }

        // Names the specific chain ingredient(s) the assigned shelves can't supply a single full unit of (base +
        // each mixer, counting a mixer used in N stages as needing N per unit). If nothing is actually short, the
        // chemist is just round-robining busy stations, so fall back to the generic wording.
        private string MissingReason(ChemistOrderState order, List<ITransitEntity> shelves)
        {
            Dictionary<string, int> required = new();
            void Add(string id)
            {
                if (string.IsNullOrEmpty(id))
                    return;
                required[id] = required.TryGetValue(id, out int c) ? c + 1 : 1;
            }
            Add(order.Chain[0].InputId);
            for (int i = 0; i < order.Chain.Count; i++)
                Add(order.Chain[i].MixerId);

            Dictionary<string, int> stock = ShelfCounts(shelves);
            List<string> missing = new();
            foreach (KeyValuePair<string, int> need in required)
            {
                int have = stock.TryGetValue(need.Key, out int a) ? a : 0;
                if (have < need.Value)
                    missing.Add(have <= 0
                        ? $"{ItemName(need.Key)} (none on the shelves)"
                        : $"{ItemName(need.Key)} (only {have})");
            }

            if (missing.Count > 0)
                return $"I can't make {DisplayName(order)} — I need {string.Join(", ", missing)}.";
            return $"I'm waiting on ingredients or running mixes for {DisplayName(order)}.";
        }

        private static string DisplayName(ChemistOrderState order) =>
            string.IsNullOrEmpty(order.TargetName) ? order.TargetProductId : order.TargetName;

        private static string ItemName(string id)
        {
            if (string.IsNullOrEmpty(id))
                return id;
            try
            {
                ItemDefinition def = Registry.GetItem(id);
                return def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : id;
            }
            catch { return id; }
        }

        private static string DefId(ItemInstance inst)
        {
            if (inst == null)
                return null;
            ItemDefinition def = inst.Definition;
            return def != null ? def.ID : null;
        }

        private static string SafeGuid(MixingStation station)
        {
            try { return station.GUID.ToString(); }
            catch { return null; }
        }

        private static string SafeName(Chemist chemist)
        {
            try { return chemist.fullName; }
            catch { return "chemist"; }
        }

        // Live snapshot of a mixing station's slots/operation for one planning pass.
        private sealed class StationView
        {
            public MixingStation Station;
            public MixingStationConfiguration Cfg;
            public ITransitEntity Entity;
            public string Guid;
            public string ProdId;
            public int ProdQty;
            public string MixId;
            public int MixQty;
            public string OutId;
            public int OutQty;
            public MixOperation Op;
            public int MaxBatch;

            public static StationView Build(MixingStation station)
            {
                if (station == null)
                    return null;

                ITransitEntity entity = station.TryCast<ITransitEntity>();
                if (entity == null)
                    return null;

                StationView v = new()
                {
                    Station = station,
                    Entity = entity,
                    Cfg = station.Configuration?.TryCast<MixingStationConfiguration>(),
                    Op = station.CurrentMixOperation,
                    MaxBatch = Math.Max(1, station.MaxMixQuantity),
                };

                try { v.Guid = station.GUID.ToString(); } catch { return null; }

                ItemSlot prod = station.ProductSlot;
                if (prod != null && prod.ItemInstance != null && prod.Quantity > 0)
                {
                    v.ProdId = DefId(prod.ItemInstance);
                    v.ProdQty = prod.Quantity;
                }

                ItemSlot mix = station.MixerSlot;
                if (mix != null && mix.ItemInstance != null && mix.Quantity > 0)
                {
                    v.MixId = DefId(mix.ItemInstance);
                    v.MixQty = mix.Quantity;
                }

                ItemSlot outp = station.OutputSlot;
                if (outp != null && outp.ItemInstance != null && outp.Quantity > 0)
                {
                    v.OutId = DefId(outp.ItemInstance);
                    v.OutQty = outp.Quantity;
                }

                return v;
            }
        }
    }
}
