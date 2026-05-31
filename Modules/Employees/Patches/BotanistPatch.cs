using HarmonyLib;
using Il2CppScheduleOne.Employees;

namespace Lithium.Modules.Employees.Patches
{
    // Botanist does not declare a Start method (only Awake / NetworkInitialize__*), and the timing
    // fields were renamed from ALL_CAPS constants to PascalCase properties (HARVEST_TIME is now
    // IndividualHarvestTime). Apply on NetworkInitialize__Late like the other employee patches so the
    // configuration object is fully initialized.
    [HarmonyPatch(typeof(Botanist), nameof(Botanist.NetworkInitialize__Late))]
    public class BotanistValuesPatch
    {
        [HarmonyPostfix]
        static void Postfix(Botanist __instance)
        {
            ModEmployees mod = Core.Get<ModEmployees>();
            if (mod == null || !mod.Configuration.Enabled)
                return;

            if (!ModEmployees.ConfiguredEmployees.Add(__instance))
                return;

            ModEmployeesConfiguration config = mod.Configuration;
            // The assignable-pot cap lives on the Botanist itself; BotanistConfiguration has no
            // station-list field (unlike Chemist/Packager), so MaxAssignedPots is all we set here.
            __instance.MaxAssignedPots = config.Botanists.MaxAssignedPots;

            __instance.Movement.WalkSpeed = config.Botanists.WalkSpeed;
            __instance.DailyWage = config.Botanists.DailyWage;

            // The pour/sow/harvest timings are static on Botanist (shared by all botanists).
            Botanist.SoilPourTime = config.Botanists.SoilPourTime;
            Botanist.WaterPourTime = config.Botanists.WaterPourTime;
            Botanist.AdditivePourTime = config.Botanists.AdditivePourTime;
            Botanist.SeedSowTime = config.Botanists.SeedSowTime;
            Botanist.IndividualHarvestTime = config.Botanists.HarvestTime;
        }
    }
}
