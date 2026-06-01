using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.Quests;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    // When a contract offer is set for the player, give larger orders a longer acceptance window and
    // have the customer text the resulting deadline. ExpiresAfter is what the game's UpdateOfferExpiry
    // counts against, so extending it here makes the deal-acceptance expiry honour the bigger window.
    [HarmonyPatch(typeof(Customer), nameof(Customer.SetOfferedContract))]
    public class CustomerOfferDeadlinePatch
    {
        // EDay is Monday = 0 .. Sunday = 6.
        private static readonly string[] DayNames =
            { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        [HarmonyPostfix]
        public static void Postfix(Customer __instance)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            AcceptanceWindow window = config.Contracts.AcceptanceWindow;
            if (window == null || !window.Enabled)
                return;

            // Offer/expiry state is server-authoritative (clients receive it via RPC).
            if (!InstanceFinder.IsServer)
                return;

            ContractInfo contract = __instance.OfferedContractInfo;
            if (contract == null || !contract.Expires || contract.Products == null)
                return;

            // Only act on a freshly-made offer, not one restored when a save loads — otherwise the window
            // would compound and the deadline text would be re-sent every load. A restored offer keeps
            // its original (already-extended) OfferedContractTime, which is in the past.
            GameDateTime offeredAt = __instance.OfferedContractTime;
            if (TimeManager.Instance.GetDateTime().GetMinSum() - offeredAt.GetMinSum() > 2)
                return;

            int quantity = contract.Products.entries.ToList().Sum(e => e.Quantity);
            if (quantity <= 0)
                return;

            int original = contract.ExpiresAfter;
            int extended = OfferAcceptanceWindow.Extend(original, quantity, window);
            if (extended <= original)
                return; // not a large enough order to grant extra time.

            contract.ExpiresAfter = extended;

            if (window.SendDeadlineMessage)
                SendDeadlineText(__instance, quantity, offeredAt.AddMins(extended), window);
        }

        private static void SendDeadlineText(Customer customer, int quantity, GameDateTime deadline,
            AcceptanceWindow window)
        {
            if (customer.NPC == null)
                return;

            string template = window.DeadlineTemplates
                .OrderBy(x => UnityEngine.Random.value)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(template))
                return;

            string msg = template
                .Replace("##QUANTITY##", quantity.ToString())
                .Replace("##DEADLINE##", FormatDeadline(deadline));

            MessagingManager.Instance.ReceiveMessage(
                new Message(msg, Message.ESenderType.Other), true, customer.NPC.ID);
        }

        // Renders a deadline as e.g. "today, 6:00 PM", "tomorrow, 9:30 AM" or "Monday, 12:00 PM".
        private static string FormatDeadline(GameDateTime deadline)
        {
            int dayDelta = deadline.elapsedDays - TimeManager.Instance.ElapsedDays;
            string time = Format12Hour(deadline.time);

            if (dayDelta <= 0)
                return $"today, {time}";
            if (dayDelta == 1)
                return $"tomorrow, {time}";

            int dayIndex = (((int)TimeManager.Instance.CurrentDay + dayDelta) % 7 + 7) % 7;
            string dayName = DayNames[dayIndex];
            return dayDelta < 7 ? $"{dayName}, {time}" : $"{dayName} ({dayDelta} days), {time}";
        }

        // hhmm is a 24-hour HHMM value (e.g. 1430 -> "2:30 PM", 0 -> "12:00 AM").
        private static string Format12Hour(int hhmm)
        {
            int hours = hhmm / 100;
            int minutes = hhmm % 100;
            string suffix = hours >= 12 ? "PM" : "AM";
            int hour12 = hours % 12;
            if (hour12 == 0)
                hour12 = 12;
            return $"{hour12}:{minutes:D2} {suffix}";
        }
    }
}
