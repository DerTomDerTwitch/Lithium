using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI.ATM;
using UnityEngine;

namespace Lithium.Modules.Banking.Patches
{
    [HarmonyPatch(typeof(ATMInterface), nameof(ATMInterface.ProcessTransaction))]
    public class AtmTransactionPatch
    {
        [HarmonyPrefix]
        public static void Prefix(float amount, bool depositing)
        {
            ModBankingConfiguration config = Core.Get<ModBanking>().Configuration;
            if (!config.Enabled)
                return;

            if (depositing && config.Atm.DailyLimited)
                ModBanking.DailyDepositSum += amount;

            TransferFeeConfiguration fee = config.TransferFee;
            if (!fee.Enabled)
                return;

            bool applies = depositing ? fee.ApplyToDeposits : fee.ApplyToWithdrawals;
            if (!applies)
                return;

            float amountToCharge = fee.Compute(amount);
            if (amountToCharge <= 0f)
                return;

            MoneyManager moneyManager = NetworkSingleton<MoneyManager>.Instance;
            if (moneyManager == null)
                return;

            float available = moneyManager.onlineBalance - (depositing ? 0f : amount);
            amountToCharge = Mathf.Min(amountToCharge, Mathf.Max(0f, available));
            if (amountToCharge <= 0f)
                return;

            moneyManager.CreateOnlineTransaction(
                "Bank Transfer Fee",
                -amountToCharge,
                1f,
                depositing ? "ATM deposit fee" : "ATM withdrawal fee");
        }
    }
}
