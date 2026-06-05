using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.Repairs.Patches
{
    // See AtmSendBreakPatch for rationale — VendingMachine (the destructible cuke machine) has an
    // identical break path: RpcLogic___SendBreak does `DaysUntilRepair = 0; Break(null);` and DayPass
    // decrements the countdown each night, repairing at <= 0.
    [HarmonyPatch(typeof(VendingMachine), nameof(VendingMachine.RpcLogic___SendBreak_2166136261))]
    public class VendingMachineSendBreakPatch
    {
        [HarmonyPostfix]
        public static void Postfix(VendingMachine __instance)
        {
            ModRepairs module = Core.Get<ModRepairs>();
            if (module == null || !module.Configuration.Enabled)
                return;

            if (__instance == null)
                return;

            __instance.DaysUntilRepair = module.Configuration.RepairDays;
        }
    }
}
