using HarmonyLib;
using Il2CppScheduleOne.Employees;

namespace Lithium.Modules.Employees.Patches
{
    [HarmonyPatch(typeof(Chemist), nameof(Chemist.NetworkInitialize__Late))]
    public class ChemistPatch
    {
        [HarmonyPostfix]
        static void Postfix(Chemist __instance)
        {
            if (!ModEmployees.TryBeginConfigure(__instance, out ModEmployeesConfiguration config))
                return;

            __instance.configuration.Stations.MaxItems = config.Chemists.MaxStations;
            __instance.Movement.WalkSpeed = config.Chemists.WalkSpeed;
            __instance.DailyWage = config.Chemists.DailyWage;
        }
    }
}
