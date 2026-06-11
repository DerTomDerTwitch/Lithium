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

            // A freshly hired / not-yet-property-assigned chemist has no configuration yet at this point
            // (it's created later in AssignProperty), so guard it like the Packager path does.
            var chemConfig = __instance.configuration;
            if (chemConfig != null && chemConfig.Stations != null)
                chemConfig.Stations.MaxItems = config.Chemists.MaxStations;
            else
                Log.Warning($"Chemist configuration was null in NetworkInitialize__Late; skipped station caps for {__instance.fullName}.");

            if (__instance.Movement != null)
                __instance.Movement.WalkSpeed = config.Chemists.WalkSpeed;
            __instance.DailyWage = config.Chemists.DailyWage;
        }
    }
}
