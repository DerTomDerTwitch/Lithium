using HarmonyLib;
using Il2CppScheduleOne.Money;

namespace Lithium.Modules.Banking.Patches
{
    [HarmonyPatch(typeof(ATM), nameof(ATM.Awake))]
    public class AtmDepositLimitPatch
    {
        private const float Unlimited = 1_000_000_000f;

        [HarmonyPostfix]
        public static void Postfix()
        {
            ModBankingConfiguration config = Core.Get<ModBanking>().Configuration;
            if (!config.Enabled)
                return;

            AtmConfiguration atm = config.Atm;
            ATM.DepositLimitEnabled = atm.WeeklyLimited || atm.DailyLimited;
            ATM.WEEKLY_DEPOSIT_LIMIT = atm.WeeklyLimited ? atm.WeeklyDepositLimit : Unlimited;
        }
    }
}
