using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Property;
using UnityEngine;

namespace Lithium.Modules.Banking.Patches
{
    [HarmonyPatch(typeof(Business), nameof(Business.CompleteOperation))]
    public class LaunderCutPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Business __instance, LaunderingOperation op)
        {
            ModBankingConfiguration config = Core.Get<ModBanking>().Configuration;
            if (!config.Enabled || op == null)
                return;

            float laundered = op.amount;
            if (laundered <= 0f)
                return;

            string name = __instance.PropertyName;

            float cutPercent = 0f;
            if (!string.IsNullOrEmpty(name)
                && config.Laundering.Businesses.TryGetValue(name, out BusinessLaunderingConfiguration business))
            {
                cutPercent = business.Cut;
            }

            float cut = laundered * (cutPercent / 100f);
            if (cut > 0f)
            {
                MoneyManager moneyManager = NetworkSingleton<MoneyManager>.Instance;
                if (moneyManager != null)
                {
                    cut = Mathf.Min(cut, Mathf.Max(0f, moneyManager.onlineBalance));
                    if (cut > 0f)
                        moneyManager.CreateOnlineTransaction("Laundering Cut", -cut, 1f, $"{name} laundering cut");
                }
                else
                {
                    cut = 0f;
                }
            }

            ModBanking.RecordLaundering(name, laundered, cut);
        }
    }
}
