using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    /// <summary>
    /// Recovers orders that a sleep / time-skip silently swallows.
    ///
    /// The game only ever generates a customer's order during a per-minute tick where the day is one of
    /// their order days and the clock is inside <c>[OrderTime, OrderTime + 120]</c> (see
    /// <c>Customer.IsDealTime</c>). But <c>TimeManager.RpcLogic___OnTimeSkip_Client</c> sets the new time,
    /// rolls the day, then fires <c>onMinutePass</c> exactly once — so every minute the skip jumped over is
    /// never evaluated. A customer whose order window fell inside the slept-over span therefore never gets
    /// the chance to order that day. For bulk / order-pattern customers that order only once a week this is
    /// very visible: one slept-through night and they do not order at all that week.
    ///
    /// This postfix runs after the skip (so <c>TimeManager.CurrentDay/CurrentTime</c> are the wake values),
    /// finds the customers whose order window was jumped over without being serviced, and re-offers that one
    /// order immediately — i.e. in the morning the player wakes into. It is a one-time catch-up: it calls the
    /// customer's own <c>ForceDealOffer()</c> and never touches <c>GetOrderDays</c>, so the recurring cadence
    /// stays anchored to its normal days and does not permanently shift forward by a day.
    ///
    /// Coexists with <see cref="OfferDeadlineTimeSkipPatch"/> (a prefix on the same method): that one shifts
    /// the deadlines of customers who already have a live offer; this one only acts on customers with no
    /// offered/current contract, so the two never touch the same customer.
    /// </summary>
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.OnTimeSkip_Client))]
    public class CustomerMissedOrderCatchupPatch
    {
        // Mirrors the upper bound of Customer.ShouldTryGenerateDeal's per-customer throttle
        // (600 + FirstName[0] % 10 * 20, max 780). Using the maximum means we never force a catch-up when the
        // customer has done business recently enough that the vanilla gate would still block a fresh order —
        // which is exactly the case we must avoid (e.g. they ordered last night, then the player slept).
        private const int RecentDealCooldownMinutes = 780;

        [HarmonyPostfix]
        public static void Postfix(int oldTime, int newTime)
        {
            try
            {
                if (!InstanceFinder.IsServer)
                    return;

                ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
                if (!config.Enabled || !config.Contracts.Enabled)
                    return;
                if (!config.OrderPatterns.Enabled || !config.OrderPatterns.CatchUpMissedOrders ||
                    !config.OrderPatterns.RankMet())
                    return;

                TimeManager time = TimeManager.Instance;
                if (time == null)
                    return;

                int oldMin = TimeManager.GetMinSumFrom24HourTime(oldTime);
                int newMin = TimeManager.GetMinSumFrom24HourTime(newTime);
                if (oldMin == newMin)
                    return;

                // Matches the game's own day-rollover test in OnTimeSkip_Client (newMinSum < oldMinSum).
                bool dayRolled = newMin < oldMin;
                EDay newDay = time.CurrentDay;
                EDay oldDay = dayRolled ? (EDay)(((int)newDay - 1 + 7) % 7) : newDay;

                foreach (Customer customer in Customer.UnlockedCustomers.ToList())
                {
                    try
                    {
                        if (!MissedOrderWindow(customer, oldDay, newDay, oldMin, newMin, dayRolled))
                            continue;

                        // Re-offers the order exactly like the normal generation path (TryGenerateContract +
                        // OfferContract / OfferContractToDealer), bypassing only the day/time gate.
                        customer.ForceDealOffer();

                        // Only record (so the phone app's Daily tab lists the shifted order today) when an
                        // offer actually resulted — ForceDealOffer no-ops if nothing is orderable.
                        if (customer.OfferedContractInfo != null)
                            DailyOrderTracker.RecordCatchUp(customer.CustomerData.name);

                        if (Log.DebugEnabled)
                            Log.Info($"[Customers] Sleep catch-up: re-offered {customer.NPC.fullName}'s " +
                                     $"missed {oldDay} order in the morning ({oldTime} -> {newTime}).");
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[Customers] Missed-order catch-up failed for a customer: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[Customers] Missed-order catch-up failed: {e}");
            }
        }

        private static bool MissedOrderWindow(Customer customer, EDay oldDay, EDay newDay,
            int oldMin, int newMin, bool dayRolled)
        {
            if (customer == null || customer.CustomerData == null || customer.NPC == null)
                return false;

            // Only unlocked, conscious customers generate orders; never clobber an in-flight offer/contract.
            if (customer.NPC.RelationData == null || !customer.NPC.RelationData.Unlocked)
                return false;
            if (customer.OfferedContractInfo != null || customer.CurrentContract != null)
                return false;
            if (!customer.NPC.IsConscious)
                return false;

            // They already did business recently enough that the vanilla throttle would block a fresh order,
            // so this was not a missed window (e.g. they ordered earlier and the player slept right after).
            if (customer.TimeSinceLastDealOffered < RecentDealCooldownMinutes ||
                customer.TimeSinceLastDealCompleted < RecentDealCooldownMinutes)
                return false;

            CustomerData data = customer.CustomerData;
            float relation = customer.NPC.RelationData.RelationDelta / 5f;
            // Same arguments Customer.IsDealTime uses for unlocked customers; routes through
            // CustomerGetOrderDaysPatch, so the result already reflects the active order pattern.
            List<EDay> orderDays = data.GetOrderDays(customer.CurrentAddiction, relation).ToList();

            int windowStart = TimeManager.GetMinSumFrom24HourTime(data.OrderTime);
            int windowEnd = windowStart + 120; // IsDealTime accepts [OrderTime, OrderTime + 120].

            // The day we slept away from: if the clock rolled over it will not recur for a week, so any
            // window not already fully elapsed when the skip began was jumped over. On a same-day forward
            // skip, the window is missed only if it sits entirely inside the skipped span.
            if (orderDays.Contains(oldDay))
            {
                if (dayRolled)
                {
                    if (windowEnd > oldMin)
                        return true;
                }
                else if (oldMin < windowEnd && windowEnd <= newMin)
                {
                    return true;
                }
            }

            // Rare: an early-morning window on the day we woke into that the skip had already passed
            // (real OrderTimes sit in [07:00, 23:59], so this almost never fires — kept for completeness).
            if (dayRolled && orderDays.Contains(newDay) && windowEnd <= newMin)
                return true;

            return false;
        }
    }
}
