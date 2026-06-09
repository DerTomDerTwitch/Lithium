using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI.Phone.Messages;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.NotifyPlayerOfContract))]
    public class CustomerOfferDeadlineMessagePatch
    {
        private static readonly string[] DayNames =
            { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        [HarmonyPrefix]
        public static void Prefix(Customer __instance, ContractInfo contract, MessageChain offerMessage)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            AcceptanceWindow window = config.Contracts.AcceptanceWindow;
            if (window == null)
                return;

            if (contract == null || !contract.Expires)
                return;

            int quantity = ProductHelper.GetTotalQuantity(contract.Products);

            int windowMins = Customer.OFFER_EXPIRY_TIME_MINS;
            if (window.Enabled)
                windowMins = OfferAcceptanceWindow.Extend(windowMins, quantity, window);

            RepairTrackedDeadline(__instance, window, windowMins, quantity);

            if (!window.SendDeadlineMessage || offerMessage == null || offerMessage.Messages == null)
                return;

            string[] templates = window.OfferDeadlineTemplates;
            if (templates == null || templates.Length == 0)
                return;

            GameDateTime deadline = TimeManager.Instance.GetDateTime().AddMins(windowMins);

            int idx = ((deadline.GetMinSum() % templates.Length) + templates.Length) % templates.Length;
            string line = templates[idx]
                .Replace("##QUANTITY##", quantity.ToString())
                .Replace("##DEADLINE##", FormatDeadline(deadline));

            foreach (string existing in offerMessage.Messages)
                if (existing == line)
                    return;

            Log.Info($"[Lithium] Offer deadline: {windowMins} min window (base {Customer.OFFER_EXPIRY_TIME_MINS}, " +
                $"qty {quantity}) -> {FormatDeadline(deadline)}");
            offerMessage.Messages.Add(line);
        }

        private static void RepairTrackedDeadline(Customer customer, AcceptanceWindow window, int windowMins, int quantity)
        {
            if (!window.Enabled || quantity <= 0)
                return;

            if (!InstanceFinder.IsServer)
                return;

            string name = customer?.CustomerData?.name;
            if (string.IsNullOrEmpty(name))
                return;

            int now = TimeManager.Instance.GetDateTime().GetMinSum();

            if (OfferDeadlineTracker.TryGet(name, out int existing) && existing > now)
                return;

            OfferDeadlineTracker.Set(name, now + windowMins);
            Log.Info($"[Customers] Repaired offer deadline for {name} at announce: +{windowMins} min (qty {quantity}).");
        }

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
