using HarmonyLib;
using Il2CppScheduleOne.UI.ATM;
using UnityEngine;

namespace Lithium.Modules.Banking.Patches
{
    [HarmonyPatch(typeof(ATMInterface), "remainingAllowedDeposit", MethodType.Getter)]
    public class AtmRemainingDepositPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result)
        {
            ModBankingConfiguration config = Core.Get<ModBanking>().Configuration;
            if (!config.Enabled || !config.Atm.DailyLimited)
                return;

            float dailyRemaining = Mathf.Max(0f, config.Atm.DailyDepositLimit - ModBanking.DailyDepositSum);
            if (__result > dailyRemaining)
                __result = dailyRemaining;
        }
    }
}
