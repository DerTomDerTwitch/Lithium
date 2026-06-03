using HarmonyLib;
using Il2CppScheduleOne.Employees;

namespace Lithium.Modules.Employees.Patches
{
    [HarmonyPatch(typeof(Packager), nameof(Packager.NetworkInitialize__Late))]
    public class PackagerPatch
    {
        [HarmonyPostfix]
        static void Postfix(Packager __instance)
        {
            if (!ModEmployees.TryBeginConfigure(__instance, out ModEmployeesConfiguration config))
                return;

            // This runs from inside the game's native NetworkInitialize__Late trampoline, so any
            // exception escapes into native code (the "during invoking native->managed trampoline"
            // crash). The configuration entity (and its Stations/Routes list fields) is not guaranteed
            // to be built yet for every spawned Packager, so guard each dereference and skip — never
            // throw — when a member is missing.
            var packagerConfig = __instance.configuration;
            if (packagerConfig != null)
            {
                if (packagerConfig.Stations != null)
                    packagerConfig.Stations.MaxItems = config.Packagers.MaxStations;
                if (packagerConfig.Routes != null)
                    packagerConfig.Routes.MaxRoutes = config.Packagers.MaxRoutes;
            }
            else
            {
                Log.Warning($"Packager configuration was null in NetworkInitialize__Late; skipped station/route caps for {__instance.fullName}.");
            }

            if (__instance.Movement != null)
                __instance.Movement.WalkSpeed = config.Packagers.WalkSpeed;
            __instance.DailyWage = config.Packagers.DailyWage;
            __instance.PackagingSpeedMultiplier = config.Packagers.PackagingSpeedMultiplier;
        }
    }
}
