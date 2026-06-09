using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    /// <summary>
    /// Clears the tracked acceptance deadline once an offer is consumed by acceptance (player or
    /// dealer path — both route through <see cref="Customer.ContractAccepted"/>). Without this the
    /// persisted <see cref="OfferDeadlineTracker"/> entry outlives the offer; when the customer's
    /// NEXT offer arrives days later and the inline-prone <c>SetOfferedContract</c> patch fails to
    /// overwrite it, the stale (long-elapsed) deadline makes <see cref="CustomerMinPassOfferExpiryPatch"/>
    /// expire the fresh offer on the next minute tick — the "offer cancelled the moment it arrived"
    /// symptom. <see cref="CustomerOfferDeadlineMessagePatch.RepairTrackedDeadline"/> is the
    /// announce-time backstop for the same hole.
    /// </summary>
    [HarmonyPatch(typeof(Customer), nameof(Customer.ContractAccepted))]
    public class CustomerContractAcceptedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Customer __instance)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            if (!InstanceFinder.IsServer)
                return;

            string name = __instance.CustomerData?.name;
            if (string.IsNullOrEmpty(name))
                return;

            OfferDeadlineTracker.Clear(name);
        }
    }
}
