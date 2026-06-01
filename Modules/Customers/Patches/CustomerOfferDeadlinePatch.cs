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

            // Only act on a freshly-made offer, not one restored when a save loads — otherwise the window
            // would compound. A restored offer keeps its original OfferedContractTime, which is in the past.
            GameDateTime offeredAt = __instance.OfferedContractTime;
            if (TimeManager.Instance.GetDateTime().GetMinSum() - offeredAt.GetMinSum() > 2)
                return;

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
            OfferDeadlineTracker.Set(__instance.CustomerData.name, offeredAt.GetMinSum() + windowMins);

            // Belt-and-suspenders: also widen the per-contract window the native check may count against
            // (never shrinking whatever the game already set).
            contract.ExpiresAfter = Mathf.Max(contract.ExpiresAfter, windowMins);
        }
    }
}
