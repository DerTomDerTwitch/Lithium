using HarmonyLib;
using Il2CppScheduleOne.UI.ATM;

namespace Lithium.Modules.Banking.Patches
{
    // `remainingAllowedDeposit` is the chokepoint that clamps the actual deposit amount
    // (SetSelectedAmount, GetAmountFromIndex for the MAX/ALL button). Vanilla returns the
    // inlined `10000 - WeeklyDepositSum`; we override it with the configured weekly/daily
    // headroom so the real limit is enforced regardless of the baked-in const.
    [HarmonyPatch(typeof(ATMInterface), "remainingAllowedDeposit", MethodType.Getter)]
    public class AtmRemainingDepositPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result)
        {
            ModBanking module = Core.Get<ModBanking>();
            if (!module.Configuration.Enabled)
                return;

            __result = module.EffectiveRemaining();
        }
    }
}
