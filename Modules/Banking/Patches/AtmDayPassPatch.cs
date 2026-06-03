using HarmonyLib;
using Il2CppScheduleOne.Money;

namespace Lithium.Modules.Banking.Patches
{
    /// <summary>
    /// Resets the per-day deposit tracker each game day. <see cref="ATM.DayPass"/> fires once per day per ATM;
    /// resetting to zero (idempotently) restores the full daily allowance. The weekly total is reset by the
    /// game itself in <c>ATM.WeekPass</c>.
    /// </summary>
    [HarmonyPatch(typeof(ATM), nameof(ATM.DayPass))]
    public class AtmDayPassPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!Core.Get<ModBanking>().Configuration.Enabled)
                return;

            ModBanking.DailyDepositSum = 0f;
        }
    }
}
