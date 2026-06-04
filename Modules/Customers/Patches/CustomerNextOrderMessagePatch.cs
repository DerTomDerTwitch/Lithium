using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.Quests;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(Customer), nameof(Customer.CurrentContractEnded))]
    public class CustomerNextOrderMessagePatch
    {
        private static readonly string[] DayNames =
            { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        private static readonly Dictionary<string, int> _lastAnnouncedMinSum = new();

        [HarmonyPostfix]
        public static void Postfix(Customer __instance, EQuestState outcome)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled || !config.OrderPatterns.AnnounceNextOrder)
                return;

            if (outcome != EQuestState.Completed)
                return;

            if (!InstanceFinder.IsServer)
                return;

            if (__instance == null || __instance.CustomerData == null || __instance.NPC == null)
                return;

            if (__instance.AssignedDealer != null)
                return;

            string customerName = __instance.CustomerData.name;

            int nowMinSum = TimeManager.Instance.GetDateTime().GetMinSum();
            if (_lastAnnouncedMinSum.TryGetValue(customerName, out int lastMinSum) && lastMinSum == nowMinSum)
                return;
            _lastAnnouncedMinSum[customerName] = nowMinSum;

            if (!config.OrderPatterns.Enabled ||
                LevelManager.Instance == null || LevelManager.Instance.TotalXP < config.Contracts.XPRequired)
                return;

            OrderPatternProfile profile = OrderPatternProfile.Create(
                customerName,
                __instance.CustomerData.MinOrdersPerWeek,
                __instance.CustomerData.MaxOrdersPerWeek);

            EDay today = TimeManager.Instance.CurrentDay;
            string dayPhrase = DayPhrase((int)today, profile.DaysUntilNextOrder(today));

            string template = config.OrderPatterns.NextOrderTemplates.PickRandom();
            if (string.IsNullOrEmpty(template))
                return;

            string msg = template.Replace("##DAY##", dayPhrase);
            MessagingManager.Instance.ReceiveMessage(
                new Message(msg, Message.ESenderType.Other), true, __instance.NPC.ID);

            Log.Info($"[Lithium] Next-order text sent to {customerName}: \"{msg}\"");
        }

        private static string DayPhrase(int today, int delta)
        {
            if (delta == 1)
                return "tomorrow";

            string name = DayNames[(today + delta) % 7];
            return today + delta >= 7 ? $"next {name}" : $"on {name}";
        }
    }
}
