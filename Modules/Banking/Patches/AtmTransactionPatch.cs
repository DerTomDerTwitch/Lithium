using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI.ATM;

namespace Lithium.Modules.Banking.Patches
{
    /// <summary>
    /// Hooks the start of an ATM transaction (<c>ATMInterface.ProcessTransaction(amount, depositing)</c>) to:
    /// (1) accumulate today's deposits for the per-day cap, and (2) charge the configured bank-transfer fee.
    /// The fee is always taken from the online (bank) balance via a fee transaction, so a $X deposit nets
    /// +$(X-fee) and a $X withdrawal costs an extra $fee from the account.
    /// </summary>
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

            moneyManager.CreateOnlineTransaction(
                "Bank Transfer Fee",
                -amountToCharge,
                1f,
                depositing ? "ATM deposit fee" : "ATM withdrawal fee");
        }
    }
}
