using HarmonyLib;
using Il2CppScheduleOne.Employees;

namespace Lithium.Modules.Employees.Patches
{
    [HarmonyPatch(typeof(Botanist), nameof(Botanist.NetworkInitialize__Late))]
    public class BotanistPatch
    {
        [HarmonyPostfix]
        static void Postfix(Botanist __instance)
        {
            if (!ModEmployees.TryBeginConfigure(__instance, out ModEmployeesConfiguration config))
                return;

            __instance.MaxAssignedPots = config.Botanists.MaxAssignedPots;

            __instance.Movement.WalkSpeed = config.Botanists.WalkSpeed;
            __instance.DailyWage = config.Botanists.DailyWage;
        }
    }
}
