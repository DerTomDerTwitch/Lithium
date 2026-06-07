using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Quests;

namespace Lithium.Modules.Banking.Patches
{
    // The "Clean Cash" tutorial quest begins once weekly ATM deposits reach the cap, but that cap
    // is the inlined `10000f` const (ATM.WEEKLY_DEPOSIT_LIMIT), so the trigger ignores our
    // configured weekly limit. OnUncappedMinPass is delegate-bound to
    // TimeManager.onUncappedMinutePass (reliably patchable) and its base implementation is empty,
    // so we reimplement it verbatim with the threshold swapped for the configured weekly limit.
    [HarmonyPatch(typeof(Quest_CleanCash), nameof(Quest_CleanCash.OnUncappedMinPass))]
    public class CleanCashQuestTriggerPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Quest_CleanCash __instance)
        {
            ModBanking module = Core.Get<ModBanking>();
            AtmConfiguration atm = module.Configuration.Atm;

            // Only override the threshold when Banking owns a weekly limit; otherwise leave the
            // vanilla method (the inlined $10,000 trigger) to run unchanged.
            if (!module.Configuration.Enabled || !atm.WeeklyLimited)
                return true;

            // base.OnUncappedMinPass() is empty, so there is nothing to forward to.
            if (__instance.State == EQuestState.Inactive
                && InstanceFinder.IsServer
                && ATM.WeeklyDepositSum >= atm.WeeklyDepositLimit)
            {
                __instance.Begin();
            }

            if (__instance.State == EQuestState.Completed)
                return false;

            if (InstanceFinder.IsServer
                && __instance.BuyBusinessEntry.State == EQuestState.Active
                && Business.OwnedBusinesses.Count > 0)
            {
                __instance.BuyBusinessEntry.Complete();
            }

            if (__instance.GoToBusinessEntry.State == EQuestState.Active)
            {
                if (Business.OwnedBusinesses.Count > 0)
                    __instance.GoToBusinessEntry.transform.position =
                        Business.OwnedBusinesses[0].PoI.transform.position;

                if (Player.Local.CurrentBusiness != null)
                    __instance.GoToBusinessEntry.Complete();
            }

            return false;
        }
    }
}
