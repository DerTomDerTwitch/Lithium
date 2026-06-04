using System;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Messaging;

namespace Lithium.Modules.Dealers.Architecture
{
    internal static class DealerMessenger
    {
        public static void Send(Dealer dealer, string message)
        {
            if (dealer == null)
                return;

            MessagingManager mm = MessagingManager.Instance;
            if (mm == null)
                return;

            try
            {
                MSGConversation conv = dealer.MSGConversation;
                if (conv != null)
                    conv.SetIsKnown(true);

                mm.ReceiveMessage(new Message(message, Message.ESenderType.Other), true, dealer.ID);
            }
            catch (Exception e)
            {
                Log.Warning($"[Dealers] Failed to text from {dealer.fullName}: {e.Message}");
            }
        }
    }
}
