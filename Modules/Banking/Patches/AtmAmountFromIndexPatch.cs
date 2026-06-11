using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI.ATM;
using UnityEngine;

namespace Lithium.Modules.Banking.Patches
{
    // GetAmountFromIndex returns the MAX/ALL deposit button's amount as
    // Min(cashBalance, remainingAllowedDeposit). That `remainingAllowedDeposit` getter is inlined
    // here too (see AtmSelectedAmountPatch), so the MAX amount is computed against the baked 10000
    // and the value fed into SetSelectedAmount is pre-truncated below the configured limit.
    // Recompute the MAX/ALL amount against the configured headroom; preset indices return fixed
    // sub-limit amounts and are left untouched. Withdrawals (depositing == false) are untouched.
    [HarmonyPatch(typeof(ATMInterface), nameof(ATMInterface.GetAmountFromIndex))]
    public class AtmAmountFromIndexPatch
    {
        [HarmonyPostfix]
        public static void Postfix(int index, bool depositing, ref float __result)
        {
            if (!depositing)
                return;

            ModBanking module = Core.Get<ModBanking>();
            if (!module.Configuration.Enabled)
                return;

            // Only the last index is the MAX/ALL button (Min(cash, remaining)); presets are fixed.
            if (index != ATMInterface.amounts.Length - 1)
                return;

            MoneyManager money = NetworkSingleton<MoneyManager>.Instance;
            if (money == null)
                return;

            __result = Mathf.Min(money.cashBalance, module.EffectiveRemaining());
        }
    }
}
