using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.OnSleepStart))]
    public class CustomerSleepOfferGuardPatch
    {
        private static ContractInfo _savedContract;
        private static GameDateTime _savedTime;
        private static bool _shouldRestore;

        [HarmonyPrefix]
        public static void Prefix(Customer __instance)
        {
            _shouldRestore = false;
            _savedContract = null;

            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            AcceptanceWindow window = config.Contracts.AcceptanceWindow;
            if (window == null)
                return;

            if (!InstanceFinder.IsServer)
                return;

            ContractInfo contract = __instance.OfferedContractInfo;
            if (contract == null || !contract.Expires)
                return;

            string name = __instance.CustomerData?.name;
            if (!OfferDeadlineTracker.TryGet(name, out int deadlineMinSum))
                return;

            int now = TimeManager.Instance.GetDateTime().GetMinSum();
            if (now >= deadlineMinSum)
                return;

            _savedContract = contract;
            _savedTime = __instance.OfferedContractTime;
            _shouldRestore = true;
        }

        [HarmonyPostfix]
        public static void Postfix(Customer __instance)
        {
            if (!_shouldRestore)
                return;

            _shouldRestore = false;
            ContractInfo saved = _savedContract;
            _savedContract = null;

            if (saved != null && __instance.OfferedContractInfo == null)
            {
                __instance.OfferedContractInfo = saved;
                __instance.OfferedContractTime = _savedTime;
            }
        }
    }
}
