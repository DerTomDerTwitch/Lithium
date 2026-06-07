using HarmonyLib;
using Il2CppScheduleOne.UI.ATM;
using UnityEngine.UI;

namespace Lithium.Modules.Banking.Patches
{
    // UpdateAvailableAmounts() greys out preset deposit buttons using the inlined
    // `WeeklyDepositSum + amount <= 10000f` check. We re-evaluate the deposit-side buttons
    // against the configured weekly/daily headroom so they reflect the real limit. (Actual
    // deposits are already clamped by AtmRemainingDepositPatch; this is the matching UI state.)
    [HarmonyPatch(typeof(ATMInterface), nameof(ATMInterface.UpdateAvailableAmounts))]
    public class AtmAvailableAmountsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ATMInterface __instance)
        {
            ModBanking module = Core.Get<ModBanking>();
            if (!module.Configuration.Enabled || !__instance.depositing)
                return;

            Il2CppSystem.Collections.Generic.List<Button> buttons = __instance.amountButtons;
            if (buttons == null || buttons.Count == 0)
                return;

            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> amounts = ATMInterface.amounts;
            float remaining = module.EffectiveRemaining();
            float balance = __instance.relevantBalance;

            for (int i = 0; i < amounts.Length && i < buttons.Count; i++)
            {
                if (i == amounts.Length - 1)
                {
                    // ALL/MAX button
                    buttons[buttons.Count - 1].interactable = balance > 0f && remaining > 0f;
                    break;
                }

                buttons[i].interactable = balance >= amounts[i] && amounts[i] <= remaining;
            }
        }
    }
}
