using HarmonyLib;
using Il2CppScheduleOne.Employees;

namespace Lithium.Modules.Employees.Patches
{
    [HarmonyPatch(typeof(Cleaner), nameof(Cleaner.NetworkInitialize___Early))]
    public class CleanerPatch
    {
        [HarmonyPostfix]
        static void Postfix(Cleaner __instance)
        {
            if (!ModEmployees.TryBeginConfigure(__instance, out ModEmployeesConfiguration config))
                return;

            __instance.configuration.Bins.MaxItems = config.Cleaners.MaxBins;
            __instance.Movement.WalkSpeed = config.Cleaners.WalkSpeed;
            __instance.DailyWage = config.Cleaners.DailyWage;
        }
    }
}
