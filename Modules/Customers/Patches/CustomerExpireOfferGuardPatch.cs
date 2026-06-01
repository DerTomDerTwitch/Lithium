using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    // Authoritative enforcement of the acceptance window. The game's native offer-expiry check (invisible
    // in the IL2CPP proxies) was cancelling large bulk offers on the vanilla window even though the
    // customer texted a later deadline. Rather than guess which native field that check reads, we gate
    // ExpireOffer itself: while the deadline recorded by CustomerOfferDeadlinePatch hasn't elapsed, the
    // expiry (and its "nvm" text) is suppressed, so the deal can never be pulled before the promised time.
    [HarmonyPatch(typeof(Customer), nameof(Customer.ExpireOffer))]
    public class CustomerExpireOfferGuardPatch
    {
        // Set by the prefix so CustomerExpireOfferPatch's postfix doesn't flag a next-day retry for a call
        // we suppressed (Harmony still runs postfixes when a prefix returns false). Prefixes always run
        // before postfixes, so the flag is current by the time that postfix reads it.
        internal static bool LastCallBlocked;

        [HarmonyPrefix]
        public static bool Prefix(Customer __instance)
        {
            LastCallBlocked = false;

            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return true;

            AcceptanceWindow window = config.Contracts.AcceptanceWindow;
            if (window == null)
                return true;

            // Expiry is server-authoritative; clients receive the result via RPC.
            if (!InstanceFinder.IsServer)
                return true;

            ContractInfo contract = __instance.OfferedContractInfo;
            if (contract == null || !contract.Expires)
                return true;

            string name = __instance.CustomerData?.name;
            if (!OfferDeadlineTracker.TryGet(name, out int deadlineMinSum))
                return true; // no tracked deadline (offer predates this feature) — let the game decide.

            int now = TimeManager.Instance.GetDateTime().GetMinSum();
            if (now < deadlineMinSum)
            {
                // The promised acceptance window hasn't elapsed yet — keep the offer alive.
                LastCallBlocked = true;
                return false;
            }

            // Deadline reached: allow the real expiry and forget the deadline.
            OfferDeadlineTracker.Clear(name);
            return true;
        }
    }
}
