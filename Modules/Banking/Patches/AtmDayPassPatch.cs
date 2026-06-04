using HarmonyLib;
using Il2CppScheduleOne.Money;

namespace Lithium.Modules.Banking.Patches
{
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
