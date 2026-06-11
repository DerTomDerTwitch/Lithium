using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Variables;
using Lithium.Modules.Employees.ProductionOrders;

namespace Lithium.Modules.Employees.Patches
{
    // Intercepts the client→host production-order requests carried over VariableDatabase.SendValue. On the host,
    // a client's SendValue lands in the public ReceiveValue (RpcLogic___SendValue calls it) — the same public
    // RPC-wrapper shape the HostStateSync guard already patches, so it's reliably patchable in IL2CPP. We swallow
    // our channel entirely (never let it flow through the normal variable path, which would otherwise broadcast
    // a no-op for an unregistered variable) and, on the host, hand the payload to ChemOrderNet.
    [HarmonyPatch(typeof(VariableDatabase), nameof(VariableDatabase.ReceiveValue))]
    internal static class ChemOrderRequestPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(string variableName, string value)
        {
            try
            {
                if (variableName != null &&
                    variableName.Equals(ChemOrderNet.Channel, StringComparison.OrdinalIgnoreCase))
                {
                    if (InstanceFinder.IsServer)
                        ChemOrderNet.Process(value);
                    return false; // consume the request; don't route it through the variable system
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[ChemistOrders] Order request intercept failed: {e.Message}");
            }
            return true;
        }
    }
}
