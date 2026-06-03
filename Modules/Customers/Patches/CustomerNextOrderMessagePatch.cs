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
    // When a customer's contract is fulfilled, have them text roughly when they'll order again — their
    // next order day from the bulk/order-pattern schedule.
    //
    // Hooked on CurrentContractEnded (the quest-completion callback) rather than ProcessHandoverServerSide:
    // the latter is the in-person handover-screen path and does NOT fire for dead-drop deliveries, so it
    // missed perfectly valid player-completed deals. CurrentContractEnded fires for every delivery method,
    // and its EQuestState distinguishes a real completion from a failure/expiry.
    [HarmonyPatch(typeof(Customer), nameof(Customer.CurrentContractEnded))]
    public class CustomerNextOrderMessagePatch
    {
        // EDay is Monday = 0 .. Sunday = 6.
        private static readonly string[] DayNames =
            { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        // CurrentContractEnded can fire more than once for a single completed contract (same multi-fire
        // behaviour the game shows for NotifyPlayerOfContract), which sent the next-order text twice. Key
        // the last announcement by customer + the in-game minute it completed: the duplicate callbacks
        // share that minute, while two genuine completions for one customer in the same minute can't happen.
        private static readonly Dictionary<string, int> _lastAnnouncedMinSum = new();

        [HarmonyPostfix]
        public static void Postfix(Customer __instance, EQuestState outcome)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled || !config.OrderPatterns.AnnounceNextOrder)
                return;

            // Only when the deal was actually completed (not failed, expired or cancelled).
            if (outcome != EQuestState.Completed)
                return;

            // Scheduling/messaging is server-authoritative (clients receive it via RPC).
            if (!InstanceFinder.IsServer)
                return;

            if (__instance == null || __instance.CustomerData == null || __instance.NPC == null)
                return;

            string customerName = __instance.CustomerData.name;

            // Suppress the duplicate CurrentContractEnded callback for this same completion.
            int nowMinSum = TimeManager.Instance.GetDateTime().GetMinSum();
            if (_lastAnnouncedMinSum.TryGetValue(customerName, out int lastMinSum) && lastMinSum == nowMinSum)
                return;
            _lastAnnouncedMinSum[customerName] = nowMinSum;

            // The announced day comes from the order-pattern schedule, which is only the customer's real
            // schedule when order patterns are reshaping it (same condition as CustomerGetOrderDaysPatch),
            // so we don't promise a day that won't hold.
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

        // Renders the next order day as "tomorrow", "on Wednesday" (later this week) or "next Monday"
        // (once the week has wrapped). delta is 1..7 whole days from today.
        private static string DayPhrase(int today, int delta)
        {
            if (delta == 1)
                return "tomorrow";

            string name = DayNames[(today + delta) % 7];
            return today + delta >= 7 ? $"next {name}" : $"on {name}";
        }
    }
}
