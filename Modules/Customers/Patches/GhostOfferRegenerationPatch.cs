using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI.Phone.Messages;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(MessagesApp), nameof(MessagesApp.SetCurrentConversation))]
    public class GhostOfferRegenerationPatch
    {
        private static string _lastName = string.Empty;
        private static int _lastMin = int.MinValue;

        [HarmonyPostfix]
        public static void Postfix(MSGConversation conversation)
        {
            try
            {
                ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
                if (!config.Enabled || !config.Contracts.Enabled)
                    return;

                if (!InstanceFinder.IsServer)
                    return;

                if (conversation == null || conversation.sender == null)
                    return;

                Customer customer = FindCustomerByNpc(conversation.sender);
                if (customer == null || customer.CustomerData == null)
                    return;

                if (customer.OfferedContractInfo != null || customer.CurrentContract != null)
                    return;

                string name = customer.CustomerData.name;
                if (string.IsNullOrEmpty(name))
                    return;

                if (!OfferDeadlineTracker.TryGet(name, out int deadline))
                    return;

                int now = TimeManager.Instance.GetDateTime().GetMinSum();
                if (now >= deadline)
                {
                    OfferDeadlineTracker.Clear(name);
                    return;
                }

                if (_lastName == name && _lastMin == now)
                    return;
                _lastName = name;
                _lastMin = now;

                OfferDeadlineTracker.Clear(name);
                Log.Info($"[Customers] Regenerating ghost offer for {name} (offer lost, {deadline - now} min still left on its window).");
                customer.ForceDealOffer();
            }
            catch (Exception e)
            {
                Log.Warning($"[Customers] Ghost-offer regeneration failed: {e}");
            }
        }

        private static Customer FindCustomerByNpc(NPC npc)
        {
            var all = Customer.UnlockedCustomers;
            if (all == null)
                return null;

            IntPtr target = npc.Pointer;
            for (int i = 0; i < all.Count; i++)
            {
                Customer c = all[i];
                if (c != null && c.NPC != null && c.NPC.Pointer == target)
                    return c;
            }
            return null;
        }
    }
}
