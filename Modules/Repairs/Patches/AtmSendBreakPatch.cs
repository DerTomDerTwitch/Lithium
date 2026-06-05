using HarmonyLib;
using Il2CppScheduleOne.Money;

namespace Lithium.Modules.Repairs.Patches
{
    // RpcLogic___SendBreak is the server-side break logic (SendBreak is [ServerRpc(RunLocally=true)]).
    // Vanilla body: `DaysUntilRepair = 0; Break(null);` — i.e. it seeds the repair countdown to 0,
    // which DayPass() then decrements once per night (`DaysUntilRepair--; if (<= 0) Repair();`), so
    // the machine repairs after the first sleep. We overwrite the seed in a postfix; RepairDays = 1
    // reproduces vanilla (1 → 0 on the first night), and higher values add that many extra nights.
    // Note: Load() restores DaysUntilRepair from the save via Break(null), NOT SendBreak, so this
    // patch never disturbs a saved countdown.
    [HarmonyPatch(typeof(ATM), nameof(ATM.RpcLogic___SendBreak_2166136261))]
    public class AtmSendBreakPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ATM __instance)
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
