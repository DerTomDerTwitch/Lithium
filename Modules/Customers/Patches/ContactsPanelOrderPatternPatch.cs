using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI.Phone.ContactsApp;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(ContactsDetailPanel), nameof(ContactsDetailPanel.Open))]
    public class ContactsPanelOrderPatternPatch
    {
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

            if (!config.OrderPatterns.RankMet())
                return;

            if (__instance.PropertiesLabel == null || __instance.PropertiesContainer == null)
                return;
            if (!__instance.PropertiesContainer.gameObject.activeInHierarchy)
                return;

            NPC npc = __instance.SelectedNPC;
            if (npc == null)
                return;

            Customer customer = npc.GetComponent<Customer>();
            if (customer == null || customer.CustomerData == null)
                return;

            const string marker = "\nOrders: ";
            if (__instance.PropertiesLabel.text.Contains(marker))
                return;

            OrderPatternProfile profile = OrderPatternProfile.Create(
                customer.CustomerData.name,
                customer.CustomerData.MinOrdersPerWeek,
                customer.CustomerData.MaxOrdersPerWeek);

            __instance.PropertiesLabel.text += marker + Describe(profile);

            string preferences = DescribePreferences(customer.CustomerData);
            if (preferences != null)
                __instance.PropertiesLabel.text += "\nPrefers: " + preferences;
        }

        private static string DescribePreferences(CustomerData data)
        {
            if (data.DefaultAffinityData == null || data.DefaultAffinityData.ProductAffinities == null)
                return null;

            var parts = data.DefaultAffinityData.ProductAffinities.ToList()
                .Select(a => $"{a.DrugType} {Mathf.RoundToInt(a.Affinity * 100f).ToString("+0;-0;0")}%")
                .ToList();

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        private static string Describe(OrderPatternProfile profile)
        {
            string days = string.Join(", ", profile.OrderDays.Select(d => DayAbbr[(int)d]));
            string cadence = profile.Archetype switch
            {
                OrderPatternArchetype.EveryThreeDays => "every few days",
                OrderPatternArchetype.TwiceWeekly => "twice weekly",
                OrderPatternArchetype.Weekly => "weekly bulk",
                _ => "varies"
            };
            return $"{days} ({cadence})";
        }
    }
}
