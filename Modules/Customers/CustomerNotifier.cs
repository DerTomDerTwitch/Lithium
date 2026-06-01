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
            if (!config.Contracts.SendNotification)
                return;
            if (!ReadyToNotify(customer))
                return;

            string msg = config.Contracts.MessageTemplates
                .OrderBy(x => UnityEngine.Random.value)
                .FirstOrDefault()
                .Replace("##DESIRES##", ProductHelper.FormatDesires(customer.CustomerData));

            Send(customer, msg);
        }

        public static void NotifyDealerNotSuitable(Customer customer)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Contracts.SendNotificationForDealers)
                return;
            if (!ReadyToNotify(customer))
                return;

            string msg = config.Contracts.DealerTemplates
                .OrderBy(x => UnityEngine.Random.value)
                .FirstOrDefault()
                .Replace("##DEALER##", customer.AssignedDealer.FirstName)
                .Replace("##DESIRES##", ProductHelper.FormatDesires(customer.CustomerData));

            Send(customer, msg);
        }

        public static void NotifyPlayerReducedDeal(Customer customer)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Contracts.SendNotification)
                return;
            if (!ReadyToNotify(customer))
                return;

            string msg = config.Contracts.ReducedSaleTemplates
                .OrderBy(x => UnityEngine.Random.value)
                .FirstOrDefault()
                .Replace("##DESIRES##", ProductHelper.FormatDesires(customer.CustomerData));

            Send(customer, msg);
        }

        public static void NotifyDealerReducedDeal(Customer customer)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Contracts.SendNotificationForDealers)
                return;
            if (!ReadyToNotify(customer))
                return;

            string msg = config.Contracts.ReducedDealerTemplates
                .OrderBy(x => UnityEngine.Random.value)
                .FirstOrDefault()
                .Replace("##DEALER##", customer.AssignedDealer.FirstName)
                .Replace("##DESIRES##", ProductHelper.FormatDesires(customer.CustomerData));

            Send(customer, msg);
        }

        // Returns true (and stamps the time) only if the cooldown has elapsed since this customer was
        // last notified, so a customer texts at most once per cooldown window.
        private static bool ReadyToNotify(Customer customer)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;

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
