using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Product;
using Lithium.Helper;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.PassMinute))]
    public class CustomerDailyNotificationPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TimeManager __instance)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;
            if (!config.Contracts.SendNotification && !config.Contracts.SendNotificationForDealers)
                return;

            int windowStartMinute = config.Contracts.NotificationWindowStartHour * 60;
            int windowEndMinute = config.Contracts.NotificationWindowEndHour * 60;

            if (!InstanceFinder.IsServer)
                return;

            if (LevelManager.Instance == null || LevelManager.Instance.TotalXP < config.Contracts.XPRequired)
                return;

            int nowMinute = TimeManager.GetMinSumFrom24HourTime(__instance.CurrentTime);
            uint span = (uint)(windowEndMinute - windowStartMinute);

            foreach (Customer customer in Customer.UnlockedCustomers.ToList())
            {
                if (customer == null || customer.CustomerData == null)
                    continue;

                int slot = windowStartMinute + (int)((uint)StableHash.Compute(customer.CustomerData.name) % span);
                if (nowMinute != slot)
                    continue;

                List<string> desires = customer.CustomerData.PreferredProperties
                    .ToList()
                    .Select(p => p.Name)
                    .ToList();
                if (desires.Count == 0)
                    continue;

                if (customer.AssignedDealer != null)
                {
                    customer.DealerHasSuitableProduct(out List<ItemInstance> dealerItems);
                    if (dealerItems.Count == 0)
                        CustomerNotifier.NotifyDealerNotSuitable(customer);
                }
                else
                {
                    if (ProductManager.ListedProducts.ToList().Count == 0)
                        CustomerNotifier.NotifyPlayerProductsNotSuitable(customer);
                }
            }
        }
    }
}
