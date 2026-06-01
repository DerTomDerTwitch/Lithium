using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI.Phone.Messages;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    // Appends the acceptance deadline as the final bubble of the contract offer message itself, so the
    // player sees how long they have right inside the deal — after the details, not as a separate text.
    //
    // Done by editing the offer's MessageChain (a list of message strings) rather than sending a new
    // message: a separate message renders immediately and lands BEFORE the deferred deal chain, and
    // NotifyPlayerOfContract can fire more than once per offer (which sent the text twice). Appending to
    // the chain with an idempotency guard fixes both — the line travels with the deal and is added once.
    [HarmonyPatch(typeof(Customer), nameof(Customer.NotifyPlayerOfContract))]
    public class CustomerOfferDeadlineMessagePatch
    {
        // EDay is Monday = 0 .. Sunday = 6.
        private static readonly string[] DayNames =
            { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        [HarmonyPrefix]
        public static void Prefix(ContractInfo contract, MessageChain offerMessage)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            AcceptanceWindow window = config.Contracts.AcceptanceWindow;
            if (window == null || !window.SendDeadlineMessage)
                return;

            if (contract == null || !contract.Expires || offerMessage == null)
                return;

            if (offerMessage.Messages == null)
                return;

            string[] templates = window.OfferDeadlineTemplates;
            if (templates == null || templates.Length == 0)
                return;

            int quantity = contract.Products != null
                ? contract.Products.entries.ToList().Sum(e => e.Quantity)
                : 0;

            // contract.ExpiresAfter isn't populated yet when the offer message is built, so derive the
            // acceptance window from the game's offer-expiry default and apply the same large-order
            // extension the enforced window uses (see CustomerOfferDeadlinePatch).
            int windowMins = Customer.OFFER_EXPIRY_TIME_MINS;
            if (window.Enabled)
                windowMins = OfferAcceptanceWindow.Extend(windowMins, quantity, window);

            GameDateTime deadline = TimeManager.Instance.GetDateTime().AddMins(windowMins);

            // Deterministic template choice (stable per offer) so the duplicate guard below can match the
            // exact line on a repeated NotifyPlayerOfContract call.
            int idx = ((deadline.GetMinSum() % templates.Length) + templates.Length) % templates.Length;
            string line = templates[idx]
                .Replace("##QUANTITY##", quantity.ToString())
                .Replace("##DEADLINE##", FormatDeadline(deadline));

            foreach (string existing in offerMessage.Messages)
                if (existing == line)
                    return; // already appended to this chain.

            Log.Info($"[Lithium] Offer deadline: {windowMins} min window (base {Customer.OFFER_EXPIRY_TIME_MINS}, " +
                $"qty {quantity}) -> {FormatDeadline(deadline)}");
            offerMessage.Messages.Add(line);
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
