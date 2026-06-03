using HarmonyLib;
using Il2CppScheduleOne.Employees;

namespace Lithium.Modules.Employees.Patches
{
    // "Employees sometimes stop working and you have to punch them to resume" is the consecutive-pathing-
    // failure mechanic: every failed walk bumps Employee.consecutivePathingFailures, and once it reaches
    // MAX_CONSECUTIVE_PATHING_FAILURES the employee gives up — it submits a no-work reason and goes idle
    // until physically dislodged (the punch just shoves them off the spot they were stuck on).
    //
    // Resetting the counter before WalkCallback runs means a failed walk only ever bumps it back to 1, so
    // it never reaches the cap: the employee keeps retrying its route instead of permanently stopping.
    // WalkCallback is the sole site that increments the counter and is not overridden by any subclass
    // (Botanist/Chemist/Packager/Cleaner/Dealer all inherit Employee), so this one patch covers them all.
    //
    // This does not touch the 4 AM shift end — that is driven by the day/night schedule, not this counter.
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
