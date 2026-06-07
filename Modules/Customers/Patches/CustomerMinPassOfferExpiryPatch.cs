using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    /// <summary>
    /// Enforces Lithium's extended <see cref="AcceptanceWindow"/> from the un-inlinable
    /// <see cref="Customer.OnMinPass"/> instead of from <see cref="Customer.UpdateOfferExpiry"/>.
    ///
    /// The native expiry chain is <c>OnMinPass → UpdateOfferExpiry → ExpireOffer →
    /// RpcLogic___ExpireOffer</c> (the latter sends the "offer_expired" / "nvm" text and nulls the
    /// offer). In the IL2CPP build <c>UpdateOfferExpiry</c> (private) and <c>ExpireOffer</c> (a
    /// never-overridden virtual) are inlined and devirtualized straight into <c>OnMinPass</c>'s
    /// native body, so Harmony patches on those two methods are simply bypassed on the path that
    /// actually runs. A sleep / time-skip jumps the clock past the vanilla 600-minute window in a
    /// single <c>OnMinPass</c> tick, so the inlined check fires and the offer "fails immediately" on
    /// waking even though the customer's text promised a later deadline — the reported symptom
    /// (<see cref="CustomerUpdateOfferExpiryPatch"/> / <see cref="CustomerExpireOfferGuardPatch"/>
    /// could not stop it because their target methods never execute non-inlined here).
    ///
    /// <see cref="Customer.OnMinPass"/> is delegate-bound (<c>onMinutePass += new Action(OnMinPass)</c>),
    /// so IL2CPP keeps it callable and Harmony can patch it reliably. This prefix runs every minute,
    /// awake or skipped:
    ///   • While the Lithium deadline is in the future, it re-anchors <c>OfferedContractTime</c> to
    ///     "now" so the inlined <c>now &gt; OfferedContractTime + 600</c> check can never fire, and
    ///     refreshes the countdown slider against the real (extended) window.
    ///   • Once the deadline has elapsed, it expires the offer explicitly via the (still-patched)
    ///     <see cref="Customer.ExpireOffer"/> wrapper — the guard then lets it through, clears the
    ///     tracker, and <see cref="CustomerExpireOfferPatch"/> flags the next-day retry.
    /// </summary>
    [HarmonyPatch(typeof(Customer), nameof(Customer.OnMinPass))]
    public class CustomerMinPassOfferExpiryPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Customer __instance)
        {
            try
            {
                ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
                if (!config.Enabled || !config.Contracts.Enabled)
                    return;

                AcceptanceWindow window = config.Contracts.AcceptanceWindow;
                if (window == null || !window.Enabled)
                    return;

                if (!InstanceFinder.IsServer)
                    return;

                ContractInfo contract = __instance.OfferedContractInfo;
                if (contract == null || !contract.Expires)
                    return;

                string name = __instance.CustomerData?.name;
                if (string.IsNullOrEmpty(name))
                    return;

                int now = TimeManager.Instance.GetDateTime().GetMinSum();

                if (!OfferDeadlineTracker.TryGet(name, out int deadlineMinSum))
                {
                    // Self-heal: CustomerOfferDeadlinePatch sets the deadline on the private
                    // Customer.SetOfferedContract, which the native build can inline past (the same
                    // class of bug this patch fixes for the expiry side). Establish the deadline here
                    // from the un-inlinable OnMinPass so the extended window engages regardless of
                    // whether that patch fired. Anchored to "now" on first sighting (OnMinPass runs
                    // within a minute of an offer appearing); because it only runs when NO entry
                    // exists, it never extends or re-rolls a window that is already tracked.
                    if (contract.Products == null)
                        return;

                    int quantity = ProductHelper.GetTotalQuantity(contract.Products);
                    if (quantity <= 0)
                        return;

                    int windowMins = OfferAcceptanceWindow.Extend(Customer.OFFER_EXPIRY_TIME_MINS, quantity, window);
                    deadlineMinSum = now + windowMins;
                    OfferDeadlineTracker.Set(name, deadlineMinSum);
                    Log.Info($"[Customers] Self-healed offer deadline for {name}: +{windowMins} min (qty {quantity}).");
                }
                if (now < deadlineMinSum)
                {
                    // Suppress the inlined native expiry: keep OfferedContractTime fresh so
                    // (now > OfferedContractTime + 600) stays false, exactly as the awake-path
                    // re-anchoring in CustomerOfferDeadlinePatch / CustomerLoadOfferDeadlinePatch does.
                    __instance.OfferedContractTime = TimeManager.Instance.GetDateTime();
                    UpdateSlider(__instance, deadlineMinSum, now);
                    return;
                }

                // Deadline reached: expire it ourselves. The native inlined check may not fire this
                // tick (we just kept OfferedContractTime fresh), so drive the wrapper directly. The
                // ExpireOffer guard sees now >= deadline, clears the tracker, runs the real expiry
                // (sends the text, nulls the offer); CustomerExpireOfferPatch flags the retry.
                __instance.ExpireOffer();
            }
            catch (Exception e)
            {
                Log.Warning($"[Customers] OnMinPass offer-expiry enforcement failed: {e}");
            }
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
