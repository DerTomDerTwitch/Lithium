using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI.Phone.ContactsApp;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    // Appends the customer's order pattern (days + cadence) to the phone Contacts customer panel, right
    // where their desires/spending are listed. ContactsDetailPanel.Open() repopulates the labels each
    // time it runs, so appending in a postfix is safe and never compounds.
    [HarmonyPatch(typeof(ContactsDetailPanel), nameof(ContactsDetailPanel.Open))]
    public class ContactsPanelOrderPatternPatch
    {
        // EDay is Monday = 0 .. Sunday = 6.
        private static readonly string[] DayAbbr =
            { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        [HarmonyPostfix]
        public static void Postfix(ContactsDetailPanel __instance)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled || !config.OrderPatterns.Enabled)
                return;
            if (!config.OrderPatterns.ShowPatternInContactPanel)
                return;

            // The displayed pattern is only the real schedule when order patterns are reshaping it — the
            // same condition CustomerGetOrderDaysPatch uses — so don't show a pattern that won't hold.
            if (LevelManager.Instance == null || LevelManager.Instance.TotalXP < config.Contracts.XPRequired)
                return;

            if (__instance.PropertiesLabel == null || __instance.PropertiesContainer == null)
                return;
            // Mirror the desires section's visibility — don't reveal a pattern where the panel itself
            // hides the customer's details (e.g. a still-locked customer).
            if (!__instance.PropertiesContainer.gameObject.activeInHierarchy)
                return;

            NPC npc = __instance.SelectedNPC;
            if (npc == null)
                return;

            Customer customer = npc.GetComponent<Customer>();
            if (customer == null || customer.CustomerData == null)
                return;

            // Open() can run more than once per view and doesn't reliably reset the label text, so guard
            // against appending our line a second time.
            const string marker = "\nOrders: ";
            if (__instance.PropertiesLabel.text.Contains(marker))
                return;

            OrderPatternProfile profile = OrderPatternProfile.Create(
                customer.CustomerData.name,
                customer.CustomerData.MinOrdersPerWeek,
                customer.CustomerData.MaxOrdersPerWeek);

            __instance.PropertiesLabel.text += marker + Describe(profile);
        }

        private static string Describe(OrderPatternProfile profile)
        {
            string days = string.Join(", ", profile.OrderDays.Select(d => DayAbbr[(int)d]));
            string cadence = profile.Archetype switch
            {
                OrderPatternArchetype.DailySmall => "daily, small",
                OrderPatternArchetype.EveryTwoDays => "every couple days",
                OrderPatternArchetype.Irregular => "irregular",
                OrderPatternArchetype.BiWeekly => "twice weekly",
                OrderPatternArchetype.WeeklyBulk => "weekly bulk",
                _ => "varies"
            };
            return $"{days} ({cadence})";
        }
    }
}
