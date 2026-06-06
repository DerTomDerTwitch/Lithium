using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.Repairs.Patches
{
    // See AtmDayPassPatch for the rationale. VendingMachine (the destructible cuke machine) has an
    // identical break/repair path; the only difference is its vanilla DayPass has no IsServer gate
    // (it counts on every peer), so we mirror that here.
    [HarmonyPatch(typeof(VendingMachine), nameof(VendingMachine.DayPass))]
    public class VendingMachineDayPassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(VendingMachine __instance)
        {
            ModRepairs module = Core.Get<ModRepairs>();
            if (module == null || !module.Configuration.Enabled)
                return true; // run vanilla DayPass

            if (__instance == null || !__instance.IsBroken)
                return true;

            int elapsed = __instance.DaysUntilRepair + 1;
            if (elapsed >= module.Configuration.RepairDays)
                __instance.Repair();
            else
                __instance.DaysUntilRepair = elapsed;

            return false; // replace vanilla's decrement-and-repair-after-one-night
        }
    }
}
