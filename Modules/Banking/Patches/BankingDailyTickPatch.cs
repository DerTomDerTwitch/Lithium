using System;
using HarmonyLib;
using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.Banking.Patches
{
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.PassMinute))]
    public class BankingDailyTickPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                Core.Get<ModBanking>()?.Tick();
            }
            catch (Exception e)
            {
                Log.Warning($"[Banking] Daily tick failed: {e.Message}");
            }
        }
    }
}
