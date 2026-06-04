using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.ExpireOffer))]
    public class CustomerExpireOfferGuardPatch
    {
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

            if (!InstanceFinder.IsServer)
                return true;

            ContractInfo contract = __instance.OfferedContractInfo;
            if (contract == null || !contract.Expires)
                return true;

            string name = __instance.CustomerData?.name;
            if (!OfferDeadlineTracker.TryGet(name, out int deadlineMinSum))
                return true;

            int now = TimeManager.Instance.GetDateTime().GetMinSum();
            if (now < deadlineMinSum)
            {
                LastCallBlocked = true;
                return false;
            }

            OfferDeadlineTracker.Clear(name);
            return true;
        }
    }
}
