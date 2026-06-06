using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Money;

namespace Lithium.Modules.Repairs.Patches
{
    // The break path seeds DaysUntilRepair = 0 (RpcLogic___SendBreak) and DayPass() then decrements it
    // once per night, repairing at <= 0 — so vanilla repairs after the first sleep. We reimplement
    // DayPass as an up-counter: while broken, DaysUntilRepair counts the nights elapsed since the break
    // and we repair once that reaches RepairDays (1 = vanilla, i.e. repair on the first night).
    //
    // Why patch DayPass and not the break: DayPass is wired into TimeManager.onSleepStart via a
    // delegate, so it always has a real, detourable body. The seed setter RpcLogic___SendBreak is a
    // two-line FishNet-generated method that IL2CPP can inline into its callers, which would leave a
    // postfix on it as dead code (the previous approach — it silently never ran). Reusing the field the
    // game already persists ("daysUntilRepair") means an in-progress repair window survives save/load
    // for free; we never touch the break or load paths.
    [HarmonyPatch(typeof(ATM), nameof(ATM.DayPass))]
    public class AtmDayPassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ATM __instance)
        {
            ModRepairs module = Core.Get<ModRepairs>();
            if (module == null || !module.Configuration.Enabled)
                return true; // run vanilla DayPass

            // Mirror the vanilla gate (server-authoritative for the ATM). When it doesn't hold, let the
            // original run — it's a no-op in that case anyway.
            if (__instance == null || !InstanceFinder.IsServer || !__instance.IsBroken)
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
