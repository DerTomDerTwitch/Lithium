using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.DryingRacks.Patches
{
    [HarmonyPatch(typeof(DryingOperation), nameof(DryingOperation.GetQuality))]
    public class DryingOperationPatch
    {
        [HarmonyPrefix]
        static bool Prefix(DryingOperation __instance, ref EQuality __result)
        {
            ModDryingRacksConfiguration config = Core.Get<ModDryingRacks>().Configuration;
            if (!config.Enabled)
                return true;

            Dictionary<string, int> dryTimes = config.PerQualityDryTimes;
            string quality = __instance.StartQuality.ToString();

            if (__instance.Time >= dryTimes.GetValueOrDefault(quality, 720))
            {
                __result = __instance.StartQuality + 1;
            }
            else
            {
                __result = __instance.StartQuality;
            }

            return false;
        }
    }
}
