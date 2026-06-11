using System;
using HarmonyLib;
using Il2CppScheduleOne.Employees;
using Lithium.Modules.Employees.ProductionOrders;

namespace Lithium.Modules.Employees.Patches
{
    // Takes over the chemist's per-tick decision logic when it has an active production order. Chemist.
    // UpdateBehaviour is a vtable-dispatched virtual with multiple sibling overrides (Botanist/Packager/
    // Cleaner), so it is reliably patchable in IL2CPP. RunOrder runs host-only and returns true only when it
    // actually took the chemist over (active order + CanWork), in which case we skip the vanilla decision tick
    // so it can't start partial mixes on order-reserved stations or move our intermediate outputs to a
    // destination. Otherwise (no order, disabled, or can't work) the vanilla logic runs unchanged.
    [HarmonyPatch(typeof(Chemist), nameof(Chemist.UpdateBehaviour))]
    internal static class ChemistOrderUpdateBehaviourPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Chemist __instance)
        {
            try
            {
                // true => order took over => skip the original method.
                return !ChemistOrderService.RunOrder(__instance);
            }
            catch (Exception e)
            {
                Log.Warning($"[ChemistOrders] UpdateBehaviour prefix failed: {e.Message}");
                return true;
            }
        }
    }
}
