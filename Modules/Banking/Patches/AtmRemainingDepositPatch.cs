using HarmonyLib;
using Il2CppScheduleOne.UI.ATM;
using UnityEngine;

namespace Lithium.Modules.Banking.Patches
{
    /// <summary>
    /// Layers the Lithium per-day deposit cap on top of the vanilla weekly one. The ATM UI asks
    /// <c>ATMInterface.remainingAllowedDeposit</c> (a private static getter returning the weekly headroom)
    /// to decide how much may still be deposited; we clamp that result to the remaining daily allowance so
    /// neither cap can be exceeded. Today's running total is tracked in <see cref="ModBanking.DailyDepositSum"/>.
    /// </summary>
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
