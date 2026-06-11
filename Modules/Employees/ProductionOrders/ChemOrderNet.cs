using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Variables;

namespace Lithium.Modules.Employees.ProductionOrders
{
    // Client → host request channel for production orders. The game is host-authoritative: only the host runs
    // the orchestrator and owns the order store, so a non-host client can't set an order directly. We ride the
    // game's VariableDatabase.SendValue (a [ServerRpc(RequireOwnership=false, RunLocally=true)] that already
    // carries two strings client→host) to deliver a serialized request, and intercept it on the host in
    // VariableDatabase.ReceiveValue (see ChemOrderRequestPatch). A deliberately non-"lithium_" channel name is
    // used so the host→client HostStateSync guard (which drops client-side "lithium_" sends) leaves it alone.
    //
    // Direction is one-way (client asks, host applies/validates). The client doesn't get the host's per-order
    // store back (no host→client order-state replication yet), so its screen can't show order progress/history;
    // the chemist visibly doing the work is the feedback. This mirrors the other host-driven Lithium modules.
    internal static class ChemOrderNet
    {
        public const string Channel = "lithord_req";
        private const char Sep = '|';
        private const char ShelfSep = ',';

        public static bool IsHost => InstanceFinder.IsServer;

        // ---- Client side: send a request to the host ----

        public static void SendSet(Chemist chemist, string productId, int quantity, List<string> shelfGuids)
        {
            string shelves = shelfGuids != null ? string.Join(ShelfSep.ToString(), shelfGuids) : "";
            Send($"SET{Sep}{Guid(chemist)}{Sep}{productId}{Sep}{quantity}{Sep}{shelves}");
        }

        public static void SendCancel(Chemist chemist)
        {
            Send($"CANCEL{Sep}{Guid(chemist)}");
        }

        private static void Send(string payload)
        {
            try
            {
                VariableDatabase db = NetworkSingleton<VariableDatabase>.InstanceExists
                    ? NetworkSingleton<VariableDatabase>.Instance : null;
                if (db == null)
                {
                    Log.Warning("[ChemistOrders] No VariableDatabase; can't send order request to host.");
                    return;
                }
                db.SendValue(null, Channel, payload);
            }
            catch (Exception e)
            {
                Log.Warning($"[ChemistOrders] Failed to send order request: {e.Message}");
            }
        }

        // ---- Host side: apply a received request ----

        public static void Process(string payload)
        {
            if (!InstanceFinder.IsServer || string.IsNullOrEmpty(payload))
                return;

            try
            {
                string[] parts = payload.Split(Sep);
                if (parts.Length < 2)
                    return;

                Chemist chemist = ResolveChemist(parts[1]);
                if (chemist == null)
                {
                    Log.Warning($"[ChemistOrders] Order request for unknown chemist GUID '{parts[1]}'.");
                    return;
                }

                switch (parts[0])
                {
                    case "SET" when parts.Length >= 5:
                        int qty = int.TryParse(parts[3], out int q) ? q : 0;
                        List<string> shelves = parts[4].Length > 0
                            ? new List<string>(parts[4].Split(ShelfSep))
                            : new List<string>();
                        if (ChemistOrderService.TrySetOrder(chemist, parts[2], qty, shelves, out string error))
                            Log.Info($"[ChemistOrders] Applied client order for {Name(chemist)}: {qty}x {parts[2]}.");
                        else
                            Log.Warning($"[ChemistOrders] Rejected client order for {Name(chemist)}: {error}");
                        break;

                    case "CANCEL":
                        ChemistOrderService.ClearOrder(chemist);
                        Log.Info($"[ChemistOrders] Applied client order cancel for {Name(chemist)}.");
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[ChemistOrders] Failed to process order request: {e.Message}");
            }
        }

        private static Chemist ResolveChemist(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;
            try { return GUIDManager.GetObject<Chemist>(new Il2CppSystem.Guid(guid)); }
            catch { return null; }
        }

        private static string Guid(Chemist chemist)
        {
            try { return chemist.GUID.ToString(); }
            catch { return ""; }
        }

        private static string Name(Chemist chemist)
        {
            try { return chemist.fullName; }
            catch { return "chemist"; }
        }
    }
}
