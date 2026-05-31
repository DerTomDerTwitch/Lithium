using HarmonyLib;
using Il2CppScheduleOne.Quests;

namespace Lithium.Modules.Storyline.Patches
{
    [HarmonyPatch(typeof(Quest_WelcomeToHylandPoint), nameof(Quest_WelcomeToHylandPoint.BlowupRV))]
    public class QuestWelcomeToHylandPointPatch
    {
        [HarmonyPrefix]
        public static bool DisableRVExplosion(Quest_WelcomeToHylandPoint __instance)
        {
            ModStorylineConfiguration config = Core.Get<ModStoryline>().Configuration;
            if (!config.Enabled || !config.PreventRVExplosion)
                return true;

            return false;
        }
    }
}
