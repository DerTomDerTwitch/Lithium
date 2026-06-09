using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    /// <summary>
    /// Freezes acceptance windows across time skips (sleeping, story skips). The window measures
    /// the player's <em>decision time</em>, but a sleep jumps the clock straight past any deadline
    /// that falls in the slept-over night: <c>TimeManager.RpcLogic___OnTimeSkip_Client</c> sets the
    /// new time, increments the day, then fires <c>onMinutePass</c> once — so the very first wake
    /// tick of <see cref="CustomerMinPassOfferExpiryPatch"/> sees <c>now &gt;= deadline</c> and
    /// expires the offer, delivering a bare "nvm" as the first message of the day (reported with
    /// Kyle: evening offer, "tomorrow 3 AM" deadline, cancelled at day start). This prefix runs
    /// before the native body (and therefore before the wake <c>onMinutePass</c>), shifting every
    /// live tracked deadline forward by the skipped minutes so the player wakes with the remaining
    /// window intact. Deadlines already lapsed before the skip are left alone (they expired fairly
    /// while the player was awake). The shift equals exactly the skipped time — no free extension,
    /// so the anti-farm rule is preserved.
    ///
    /// The skipped count is computed modularly (wrap at midnight), NOT the game's own
    /// <c>Mathf.Abs</c> diff — for a 22:00 → 7:00 sleep the game's count (900) overstates the real
    /// absolute-clock advance (540), and the tracker deadlines are absolute min-sums.
    /// </summary>
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.OnTimeSkip_Client))]
    public class OfferDeadlineTimeSkipPatch
    {
        [HarmonyPrefix]
        public static void Prefix(int oldTime, int newTime)
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

                int skipped = ((TimeManager.GetMinSumFrom24HourTime(newTime)
                                - TimeManager.GetMinSumFrom24HourTime(oldTime)) % 1440 + 1440) % 1440;
                if (skipped <= 0)
                    return;

                int now = TimeManager.Instance.GetDateTime().GetMinSum();

                foreach (Customer customer in Customer.UnlockedCustomers.ToList())
                {
                    ContractInfo contract = customer?.OfferedContractInfo;
                    if (contract == null || !contract.Expires)
                        continue;

                    string name = customer.CustomerData?.name;
                    if (string.IsNullOrEmpty(name))
                        continue;

                    if (!OfferDeadlineTracker.TryGet(name, out int deadline) || deadline <= now)
                        continue;

                    OfferDeadlineTracker.Set(name, deadline + skipped);
                    Log.Info($"[Customers] Shifted {name}'s offer deadline by {skipped} skipped min " +
                        $"({oldTime} -> {newTime}).");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[Customers] Offer-deadline time-skip shift failed: {e}");
            }
        }
    }
}
