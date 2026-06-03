using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Messaging;
using Lithium.Helper;
using Lithium.Modules.Customers.Behaviours;

namespace Lithium.Modules.Customers
{
    /// <summary>
    /// Sends the "you don't stock an effect I want" complaint texts. Driven daily (spread across the
    /// day) by CustomerDailyNotificationPatch. A per-customer cooldown (CustomerNotificationState)
    /// prevents the same customer texting more than once per NotificationCooldownInMinutes.
    /// </summary>
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

        public static void NotifyPlayerReducedDeal(Customer customer)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            Notify(customer, config, config.Contracts.SendNotification, config.Contracts.ReducedSaleTemplates, isDealer: false);
        }

        public static void NotifyDealerReducedDeal(Customer customer)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            Notify(customer, config, config.Contracts.SendNotificationForDealers, config.Contracts.ReducedDealerTemplates, isDealer: true);
        }

        // Shared flow for all four notifications: bail if the relevant SendNotification flag is off or the
        // per-customer cooldown hasn't elapsed, then pick a random template, fill its placeholders and send.
        // Dealer templates additionally support the ##DEALER## placeholder (the assigned dealer's name).
        private static void Notify(Customer customer, ModCustomersConfiguration config, bool gateFlag,
            string[] templates, bool isDealer)
        {
            if (!gateFlag)
                return;
            if (!ReadyToNotify(customer, config))
                return;

            string msg = templates.PickRandom()
                .Replace("##DESIRES##", ProductHelper.FormatDesires(customer.CustomerData));
            if (isDealer)
                msg = msg.Replace("##DEALER##", customer.AssignedDealer.FirstName);

            Send(customer, msg);
        }

        // Returns true (and stamps the time) only if the cooldown has elapsed since this customer was
        // last notified, so a customer texts at most once per cooldown window.
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
