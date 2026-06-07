using HarmonyLib;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI.ATM;
using UnityEngine;
using UnityEngine.UI;

namespace Lithium.Modules.Banking.Patches
{
    // ATMInterface.Update() rebuilds the top-bar limit text and the deposit button state every
    // frame from the inlined `10000f` const. We postfix it to re-render both against the
    // configured weekly limit. Update is a MonoBehaviour lifecycle method, so it is a reliable
    // patch point in IL2CPP.
    [HarmonyPatch(typeof(ATMInterface), nameof(ATMInterface.Update))]
    public class AtmDepositDisplayPatch
    {
        private static readonly Color32 OverLimitColor = new Color32(255, 75, 75, 255);

        [HarmonyPostfix]
        public static void Postfix(ATMInterface __instance)
        {
            ModBanking module = Core.Get<ModBanking>();
            AtmConfiguration atm = module.Configuration.Atm;
            if (!module.Configuration.Enabled || !__instance.isOpen)
                return;

            float sum = ATM.WeeklyDepositSum;

            Text limitText = __instance.depositLimitText;
            if (limitText != null)
            {
                if (atm.WeeklyLimited)
                {
                    limitText.text = MoneyManager.FormatAmount(sum) + " / " + MoneyManager.FormatAmount(atm.WeeklyDepositLimit);
                    limitText.color = sum >= atm.WeeklyDepositLimit ? OverLimitColor : Color.white;
                }
                else
                {
                    // No weekly cap: just show how much has been deposited.
                    limitText.text = MoneyManager.FormatAmount(sum);
                    limitText.color = Color.white;
                }
            }

            // Vanilla only re-enables the deposit button while on the menu screen; setting it
            // unconditionally is harmless and avoids a fragile screen-equality check.
            Button depositButton = __instance.menu_DepositButton;
            if (depositButton != null)
                depositButton.interactable = module.EffectiveRemaining() > 0f;
        }
    }
}
