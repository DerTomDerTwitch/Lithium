using HarmonyLib;
using Il2CppScheduleOne;

namespace Lithium.Modules.Weapons.Patches
{
    // The item Registry is fully populated by the time Registry.Start runs (same hook StackSizes
    // uses to edit definitions). We capture the instance and apply the pawn-value override here;
    // ModWeapons.Apply() re-runs ReapplyAll for live config reloads.
    [HarmonyPatch(typeof(Registry), nameof(Registry.Start))]
    public class RegistryStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Registry __instance)
        {
            WeaponPawnValue.RegistryInstance = __instance;
            WeaponPawnValue.ReapplyAll();
        }
    }
}
