using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    // Preserves a pending contract offer across a savegame load. Offers are restored by Customer.Load,
    // which writes OfferedContractInfo / OfferedContractTime straight from the save data WITHOUT going
    // through SetOfferedContract — so CustomerOfferDeadlinePatch never runs on load and no deadline is
    // recorded for the session. The restored OfferedContractTime is also stale (for weekly/scheduled
    // orders it's the moment the order was *scheduled*, days before it surfaced), so the native expiry
    // check sees a huge elapsed time and fires ExpireOffer the instant the save loads. With no tracked
    // deadline the ExpireOffer guard can't recognise the offer either, so the deal is cancelled with the
    // "nvm" text — exactly the punishing loss of weekly orders the player reported.
    //
    // Fix: after Load restores an expiring offer, re-establish its deadline and realign the offer clock:
    //   - Honour an already-recorded deadline if one survived the reload (never extend it on reload, so
    //     reloading can't farm a fresh window). If that deadline has genuinely elapsed, leave the offer to
    //     expire normally.
    //   - Otherwise (the common case — restored without SetOfferedContract) grant the full promised window
    //     measured from the load moment, so the request is preserved with the duration it was owed.
    // Then re-anchor OfferedContractTime to "now" and widen ExpiresAfter to the remaining time, so the
    // native expiry check itself only fires at the real deadline rather than immediately — independent of
    // when the guard happens to see the recorded deadline.
    [HarmonyPatch(typeof(Customer), nameof(Customer.Load), typeof(Il2CppScheduleOne.Persistence.Datas.CustomerData))]
    public class CustomerLoadOfferDeadlinePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Customer __instance)
        {
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
            if (contract == null || !contract.Expires || contract.Products == null)
                return;

            string name = __instance.CustomerData?.name;
            if (string.IsNullOrEmpty(name))
                return;

            int now = TimeManager.Instance.GetDateTime().GetMinSum();

            // Window the player is owed for this order, computed exactly like the offer message / the
            // enforced window (CustomerOfferDeadlinePatch) so the restored deal keeps its promised duration.
            int quantity = ProductHelper.GetTotalQuantity(contract.Products);
            int baseWindow = Customer.OFFER_EXPIRY_TIME_MINS;
            int windowMins = window.Enabled
                ? OfferAcceptanceWindow.Extend(baseWindow, quantity, window)
                : baseWindow;

            int deadline;
            if (OfferDeadlineTracker.TryGet(name, out int existing))
            {
                // A deadline already survived the reload — honour it exactly, never extending on reload so
                // a player can't refresh an offer by quitting and loading.
                if (existing <= now)
                    return; // the promised window has genuinely elapsed; let the offer expire normally.
                deadline = existing;
            }
            else
            {
                // No recorded deadline (restored without SetOfferedContract): grant the full promised
                // window from the load moment so the request is preserved with the duration it was owed.
                deadline = now + windowMins;
                OfferDeadlineTracker.Set(name, deadline);
            }

            // Realign the offer clock so the native expiry check fires at the real deadline instead of
            // immediately on the stale OfferedContractTime. The ExpireOffer guard still enforces the same
            // deadline as a backstop.
            int remaining = deadline - now;
            contract.ExpiresAfter = Mathf.Max(contract.ExpiresAfter, remaining);
            __instance.OfferedContractTime = TimeManager.Instance.GetDateTime();
        }
    }
}
