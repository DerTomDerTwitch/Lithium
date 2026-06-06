using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    /// <summary>
    /// Keeps an offered contract alive until Lithium's extended <see cref="AcceptanceWindow"/>
    /// deadline actually elapses.
    ///
    /// The game's <c>Customer.UpdateOfferExpiry</c> (called every game-minute from
    /// <c>OnMinPass</c>) hard-expires every offer at <c>OfferedContractTime + OFFER_EXPIRY_TIME_MINS</c>
    /// (600 min): it calls <see cref="Customer.ExpireOffer"/> AND then sets
    /// <c>OfferedContractInfo = null</c> directly in the caller.
    /// <see cref="CustomerExpireOfferGuardPatch"/> can block the <see cref="Customer.ExpireOffer"/>
    /// RPC, but it cannot stop that null assignment — so the offer object is still destroyed at the
    /// 600-minute mark, long before the longer deadline the customer's message promised (e.g. a
    /// 1.5x <see cref="AcceptanceWindow.DurationMultiplier"/> advertises a 900-minute window). A
    /// sleep / time-skip jumps past 600 min in a single step, so the offer "fails immediately" on
    /// waking — the symptom this patch fixes.
    ///
    /// While a future Lithium deadline is tracked for the customer, this prefix updates the
    /// countdown slider against the real (extended) window and skips the vanilla method entirely,
    /// so neither <see cref="Customer.ExpireOffer"/> nor the null-out runs. Once the deadline is
    /// reached it defers to vanilla, which expires the offer normally (the guard then lets it
    /// through and clears the tracker).
    /// </summary>
    [HarmonyPatch(typeof(Customer), nameof(Customer.UpdateOfferExpiry))]
    public class CustomerUpdateOfferExpiryPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Customer __instance)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return true;

            AcceptanceWindow window = config.Contracts.AcceptanceWindow;
            if (window == null || !window.Enabled)
                return true;

            if (!InstanceFinder.IsServer)
                return true;

            ContractInfo contract = __instance.OfferedContractInfo;
            if (contract == null || !contract.Expires)
                return true;

            string name = __instance.CustomerData?.name;
            if (!OfferDeadlineTracker.TryGet(name, out int deadlineMinSum))
                return true;

            int now = TimeManager.Instance.GetDateTime().GetMinSum();
            if (now >= deadlineMinSum)
                return true; // deadline reached -> let vanilla expire it (guard clears the tracker)

            // Within the extended window: refresh the countdown bar, then skip vanilla so it cannot
            // null the offer out at the game's 600-minute hard cap.
            UpdateSlider(__instance, deadlineMinSum, now);
            return false;
        }

        private static void UpdateSlider(Customer customer, int deadlineMinSum, int now)
        {
            try
            {
                var npc = customer.NPC;
                if (npc == null || npc.MSGConversation == null)
                    return;

                int start = customer.OfferedContractTime.GetMinSum();
                int windowLen = deadlineMinSum - start;
                if (windowLen <= 0)
                    return;

                float remaining = 1f - Mathf.Clamp01((float)(now - start) / windowLen);

                HUD hud = Singleton<HUD>.Instance;
                Color color = hud != null ? hud.RedGreenGradient.Evaluate(remaining) : Color.white;
                npc.MSGConversation.SetSliderValue(remaining, color);
            }
            catch (Exception e)
            {
                Log.Warning($"[Customers] Offer-expiry slider update failed: {e}");
            }
        }
    }
}
