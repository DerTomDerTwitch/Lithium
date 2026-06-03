using HarmonyLib;
using Il2CppScheduleOne.Money;

namespace Lithium.Modules.Banking.Patches
{
    /// <summary>
    /// Pushes the configured weekly deposit limit onto the ATM's static fields when each ATM wakes.
    /// The vanilla game tracks deposits weekly in <see cref="ATM.WeeklyDepositSum"/> and blocks once
    /// <see cref="ATM.WEEKLY_DEPOSIT_LIMIT"/> is hit (gated by <see cref="ATM.DepositLimitEnabled"/>).
    /// We keep the limiter enabled whenever either a weekly or daily cap is configured so the UI clamps
    /// deposits; the daily cap itself is layered on in <see cref="AtmRemainingDepositPatch"/>.
    /// </summary>
    [HarmonyPatch(typeof(ATM), nameof(ATM.Awake))]
    public class AtmDepositLimitPatch
    {
        // Stand-in for "no weekly limit" while keeping the limiter on (so the daily cap can still apply).
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
