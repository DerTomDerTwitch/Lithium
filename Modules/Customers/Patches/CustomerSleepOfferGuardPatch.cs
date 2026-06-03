using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    // Keeps a pending offer alive across sleep. The acceptance-window deadline is only enforced on the
    // awake, per-minute path (OnMinPass -> UpdateOfferExpiry -> ExpireOffer, which CustomerExpireOfferGuardPatch
    // suppresses). Sleeping never runs that loop: TimeManager.StartSleep stops the per-minute tick
    // (_stopMinPassWait) and jumps the clock with SkipForwardToTime, so the customer's OnSleepStart withdraws
    // the offer directly (clearing OfferedContractInfo) without ever calling the guarded ExpireOffer — which
    // is why a deal the player still had "days" left on vanished overnight.
    //
    // So we wrap OnSleepStart: snapshot a live offer whose promised deadline hasn't elapsed, and if the sleep
    // handler cleared it, restore it exactly as it was. Purely additive — if a future code path withdraws via
    // the (already guarded) ExpireOffer instead, OfferedContractInfo stays non-null and the postfix is a no-op.
    [HarmonyPatch(typeof(Customer), nameof(Customer.OnSleepStart))]
    public class CustomerSleepOfferGuardPatch
    {
        // OnSleepStart runs to completion per customer (prefix -> original -> postfix) on the main thread
        // before the next customer's, so a single static snapshot is safe — mirrors CustomerExpireOfferGuardPatch.
        private static ContractInfo _savedContract;
        private static GameDateTime _savedTime;
        private static bool _shouldRestore;

        [HarmonyPrefix]
        public static void Prefix(Customer __instance)
        {
            _shouldRestore = false;
            _savedContract = null;

            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            AcceptanceWindow window = config.Contracts.AcceptanceWindow;
            if (window == null)
                return;

            // Offer/expiry state is server-authoritative (clients receive it via RPC).
            if (!InstanceFinder.IsServer)
                return;

            ContractInfo contract = __instance.OfferedContractInfo;
            if (contract == null || !contract.Expires)
                return;

            string name = __instance.CustomerData?.name;
            if (!OfferDeadlineTracker.TryGet(name, out int deadlineMinSum))
                return; // no tracked deadline (offer predates this feature) — let the game decide.

            int now = TimeManager.Instance.GetDateTime().GetMinSum();
            if (now >= deadlineMinSum)
                return; // the promised window has elapsed — let the offer expire normally.

            // Live offer with time still on the clock. Remember it so we can undo an overnight withdrawal.
            _savedContract = contract;
            _savedTime = __instance.OfferedContractTime;
            _shouldRestore = true;
        }

        [HarmonyPostfix]
        public static void Postfix(Customer __instance)
        {
            if (!_shouldRestore)
                return;

            _shouldRestore = false;
            ContractInfo saved = _savedContract;
            _savedContract = null;

            // OnSleepStart withdrew the still-valid offer — put it back exactly as it was.
            if (saved != null && __instance.OfferedContractInfo == null)
            {
                __instance.OfferedContractInfo = saved;
                __instance.OfferedContractTime = _savedTime;
            }
        }
    }
}
