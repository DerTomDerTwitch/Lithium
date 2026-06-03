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
    // Gives larger orders a longer acceptance window. ExpiresAfter is what the game's UpdateOfferExpiry
    // counts against, so extending it here makes the deal-acceptance expiry honour the bigger window.
    // The deadline itself is shown to the player by CustomerOfferDeadlineMessagePatch.
    [HarmonyPatch(typeof(Customer), nameof(Customer.SetOfferedContract))]
    public class CustomerOfferDeadlinePatch
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

            // The deadline is anchored to NOW (the moment the offer is presented), exactly like the
            // customer's deadline text in CustomerOfferDeadlineMessagePatch, so the enforced window always
            // equals the one the player was promised.
            //
            // We must NOT anchor to OfferedContractTime: for weekly / scheduled orders that timestamp is
            // stamped when the order is *scheduled* (days before it surfaces), not when it's shown. Adding
            // the window to that stale time collapsed it — e.g. a Sunday order whose OfferedContractTime was
            // the previous Monday got a deadline of (Monday + 7 days) ≈ this Monday, so the guard let it
            // expire on the rollover to Monday even though the text promised "Sunday (7 days)".
            //
            // Anchoring to now is also reload-safe: if a save load re-runs SetOfferedContract, we re-record
            // the deadline relative to the restored "now", which only ever lands at or after the original
            // promise (never earlier), so the guard keeps protecting it.
            int nowMinSum = TimeManager.Instance.GetDateTime().GetMinSum();

            int quantity = contract.Products.entries.ToList().Sum(e => e.Quantity);
            if (quantity <= 0)
                return;

            // Window the player is actually granted, computed exactly like the customer's deadline text
            // (CustomerOfferDeadlineMessagePatch) so the enforced expiry matches the promise.
            int baseWindow = Customer.OFFER_EXPIRY_TIME_MINS;
            int windowMins = window.Enabled
                ? OfferAcceptanceWindow.Extend(baseWindow, quantity, window)
                : baseWindow;

            // Authoritative enforcement: the ExpireOffer guard keeps the deal alive until this absolute
            // deadline, regardless of which field the native expiry check actually consults.
            OfferDeadlineTracker.Set(__instance.CustomerData.name, nowMinSum + windowMins);

            // Belt-and-suspenders: also widen the per-contract window the native check may count against
            // (never shrinking whatever the game already set).
            contract.ExpiresAfter = Mathf.Max(contract.ExpiresAfter, windowMins);
        }
    }
}
