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
    // Sends each customer a daily complaint text when neither the player's listed products nor their
    // assigned dealer offer any of the effects the customer wants — every day, not only on the days
    // the game would have generated an order.
    //
    // The exact time of day is derived deterministically from the customer's name (the same
    // StableHash used for order patterns), so the texts are spread across the active hours instead of
    // all arriving at once. PassMinute fires once per in-game minute; each customer's slot is matched
    // exactly, so it fires at most once per day. The window sits in daytime hours so sleeping (which
    // skips minutes at night) doesn't cause missed days.
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

            // Server-authoritative, mirroring contract generation, so the host doesn't double-send.
            if (!InstanceFinder.IsServer)
                return;

            // Same XP gate as the contract system these notifications belong to.
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

                // Only nudge when there's nothing at all to sell. If something is available (even
                // without the wanted effect), the customer instead does a reduced substitute deal,
                // which sends its own "bought reduced" text from CustomerContractGenerationPatch —
                // so this daily nudge would otherwise contradict it.
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
