using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using Lithium.Modules.PlantGrowth.Behaviours;

namespace Lithium.Modules.PlantGrowth.Patches
{
    [HarmonyPatch(typeof(Pot), nameof(Pot.Start))]
    public class PotStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pot __instance)
        {
            if (!Core.Get<ModPlants>().Configuration.Enabled)
                return;
            __instance.gameObject.AddComponent<PotBaseValues>().Init(__instance);
        }
    }
}
