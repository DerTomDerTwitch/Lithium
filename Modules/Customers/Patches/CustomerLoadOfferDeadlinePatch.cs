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
    [HarmonyPatch(typeof(Customer), nameof(Customer.Load), typeof(Il2CppScheduleOne.Persistence.Datas.CustomerData))]
    public class CustomerLoadOfferDeadlinePatch
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

            if (!InstanceFinder.IsServer)
                return;

            ContractInfo contract = __instance.OfferedContractInfo;
            if (contract == null || !contract.Expires || contract.Products == null)
                return;

            string name = __instance.CustomerData?.name;
            if (string.IsNullOrEmpty(name))
                return;

            int now = TimeManager.Instance.GetDateTime().GetMinSum();

            int quantity = ProductHelper.GetTotalQuantity(contract.Products);
            int baseWindow = Customer.OFFER_EXPIRY_TIME_MINS;
            int windowMins = window.Enabled
                ? OfferAcceptanceWindow.Extend(baseWindow, quantity, window)
                : baseWindow;

            int deadline;
            if (OfferDeadlineTracker.TryGet(name, out int existing))
            {
                if (existing <= now)
                    return;
                deadline = existing;
            }
            else
            {
                deadline = now + windowMins;
                OfferDeadlineTracker.Set(name, deadline);
            }

            __instance.OfferedContractTime = TimeManager.Instance.GetDateTime();
        }
    }
}
