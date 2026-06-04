using HarmonyLib;
using Il2CppScheduleOne.Employees;

namespace Lithium.Modules.Employees.Patches
{
    [HarmonyPatch(typeof(Employee), nameof(Employee.WalkCallback))]
    public class EmployeeStuckPatch
    {
        [HarmonyPrefix]
        static void Prefix(Employee __instance)
        {
            ModEmployees mod = Core.Get<ModEmployees>();
            if (mod == null || !mod.Configuration.Enabled || !mod.Configuration.PreventWorkStoppage)
                return;

            __instance.consecutivePathingFailures = 0;
        }
    }
}
