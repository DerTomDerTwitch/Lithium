using System;
using HarmonyLib;
using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.Banking.Patches
{
    /// <summary>
    /// Drives the daily laundering report. <c>TimeManager.PassMinute</c> fires every in-game minute; the module
    /// only acts when the elapsed-day counter rolls over (handling multi-day sleep jumps). Other modules already
    /// hang their daily work off this method.
    /// </summary>
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
