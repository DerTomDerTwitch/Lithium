using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.UI.Handover;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    /// <summary>
    /// Records a customer's completed order for "today" (absolute <c>ElapsedDays</c>) when a handover
    /// finalises, so the Lithium phone app's Daily tab can mark customers who have already been served as
    /// done. Host-only: the server-side handover is the authoritative completion point and matches the
    /// host-written <see cref="DailyOrderTracker"/>. A reused per-minute counter (<c>TimeSinceLastDealCompleted</c>)
    /// can't be used for this — a sleep advances it by a single tick, so a completion from a previous day can
    /// look like it happened today; the absolute day is unambiguous.
    /// </summary>
    [HarmonyPatch(typeof(Customer), nameof(Customer.ProcessHandoverServerSide))]
    public static class CustomerCompletionTrackPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Customer __instance, HandoverScreen.EHandoverOutcome outcome)
        {
            try
            {
                if (!InstanceFinder.IsServer || outcome != HandoverScreen.EHandoverOutcome.Finalize)
                    return;
                if (__instance == null || __instance.CustomerData == null)
                    return;

                DailyOrderTracker.RecordCompletion(__instance.CustomerData.name);
            }
            catch (Exception e)
            {
                Log.Warning($"[Customers] Daily completion tracking failed: {e.Message}");
            }
        }
    }
}
