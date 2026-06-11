using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI.ATM;
using UnityEngine;
using UnityEngine.UI;

namespace Lithium.Modules.Banking.Patches
{
    // The actual deposit clamp lives here: SetSelectedAmount stores `selectedAmount`, the value
    // AmountConfirmed -> ProcessTransaction ultimately deposits. Vanilla clamps it to
    // Min(cashBalance, remainingAllowedDeposit), where `remainingAllowedDeposit` is a tiny
    // `private static` getter (=> 10000 - WeeklyDepositSum). IL2CPP AOT inlines that getter into
    // this consumer, so AtmRemainingDepositPatch's getter postfix never fires on this path and the
    // stored amount stays capped at the baked 10000 even when the configured weekly limit is higher
    // (symptom: the top-bar UI shows the new limit but deposits still cap at 10000). We re-clamp the
    // stored amount against the configured headroom at this un-inlinable consumer. Withdrawals
    // (depositing == false) are untouched.
    [HarmonyPatch(typeof(ATMInterface), "SetSelectedAmount")]
    public class AtmSelectedAmountPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ATMInterface __instance, float amount)
        {
            ModBanking module = Core.Get<ModBanking>();
            if (!module.Configuration.Enabled || !__instance.depositing)
                return;

            MoneyManager money = NetworkSingleton<MoneyManager>.Instance;
            if (money == null)
                return;

            float cap = Mathf.Min(money.cashBalance, module.EffectiveRemaining());
            float clamped = Mathf.Clamp(amount, 0f, cap);

            __instance.selectedAmount = clamped;

            Text label = __instance.amountLabelText;
            if (label != null)
                label.text = MoneyManager.FormatAmount(clamped);
        }
    }
}
