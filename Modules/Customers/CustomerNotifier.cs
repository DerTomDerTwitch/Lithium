using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Messaging;
using Lithium.Helper;
using Lithium.Modules.Customers.Behaviours;

namespace Lithium.Modules.Customers
{
    public sealed class ReducedDealDetails
    {
        public ReducedDealDetails(string productName, int quantity, float priceEach, float total)
        {
            ProductName = productName;
            Quantity = quantity;
            PriceEach = priceEach;
            Total = total;
        }

        public string ProductName { get; }
        public int Quantity { get; }
        public float PriceEach { get; }
        public float Total { get; }
    }

    public static class CustomerNotifier
    {
        public static void NotifyPlayerProductsNotSuitable(Customer customer)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            Notify(customer, config, config.Contracts.SendNotification, config.Contracts.MessageTemplates, isDealer: false);
        }

        public static void NotifyDealerNotSuitable(Customer customer)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            Notify(customer, config, config.Contracts.SendNotificationForDealers, config.Contracts.DealerTemplates, isDealer: true);
        }

        public static void NotifyPlayerReducedDeal(Customer customer, string productName, int quantity, float priceEach, float total)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            Notify(customer, config, config.Contracts.SendNotification, config.Contracts.ReducedSaleTemplates,
                isDealer: false, new ReducedDealDetails(productName, quantity, priceEach, total));
        }

        public static void NotifyDealerReducedDeal(Customer customer, string productName, int quantity, float priceEach, float total)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            Notify(customer, config, config.Contracts.SendNotificationForDealers, config.Contracts.ReducedDealerTemplates,
                isDealer: true, new ReducedDealDetails(productName, quantity, priceEach, total));
        }

        private static void Notify(Customer customer, ModCustomersConfiguration config, bool gateFlag,
            string[] templates, bool isDealer, ReducedDealDetails details = null)
        {
            if (!gateFlag)
                return;
            if (!ReadyToNotify(customer, config))
                return;

            string msg = templates.PickRandom()
                .Replace("##DESIRES##", ProductHelper.FormatDesires(customer.CustomerData));
            if (isDealer)
                msg = msg.Replace("##DEALER##", customer.AssignedDealer.FirstName);
            if (details != null)
                msg = msg
                    .Replace("##QTY##", details.Quantity.ToString())
                    .Replace("##PRODUCT##", details.ProductName)
                    .Replace("##PRICE_EACH##", $"${details.PriceEach:N2}")
                    .Replace("##TOTAL##", $"${details.Total:N0}");

            Send(customer, msg);
        }

        private static bool ReadyToNotify(Customer customer, ModCustomersConfiguration config)
        {
            if (customer.TryGetComponent(out CustomerNotificationState state))
            {
                if (TimeManager.Instance.Playtime - state.LastNotification < 60 * config.Contracts.NotificationCooldownInMinutes)
                    return false;
            }
            else
            {
                state = customer.gameObject.AddComponent<CustomerNotificationState>();
            }

            state.LastNotification = TimeManager.Instance.Playtime;
            return true;
        }

        private static void Send(Customer customer, string msg)
        {
            MessagingManager.Instance.ReceiveMessage(new(msg, Message.ESenderType.Other), true, customer.NPC.ID);
        }
    }
}
