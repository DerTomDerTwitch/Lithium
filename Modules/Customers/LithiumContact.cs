using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Lithium.Helper;

namespace Lithium.Modules.Customers
{
    public static class LithiumContact
    {
        public static void Send(string message)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            NPC npc = Resolve(config.Coverage.ContactNpcName);
            if (npc == null)
            {
                Log.Warning($"[Lithium] Coverage contact NPC '{config.Coverage.ContactNpcName}' not found; message dropped.");
                return;
            }

            MSGConversation conv = npc.MSGConversation;
            if (conv != null)
            {
                if (conv.contactName != config.Coverage.ContactDisplayName)
                    conv.contactName = config.Coverage.ContactDisplayName;
                conv.SetIsKnown(true);
            }

            MessagingManager.Instance.ReceiveMessage(new Message(message, Message.ESenderType.Other), true, npc.ID);
        }

        private static NPC Resolve(string fullName)
        {
            foreach (NPC npc in NPCManager.NPCRegistry.ToList())
            {
                if (npc != null && string.Equals(npc.fullName, fullName, StringComparison.OrdinalIgnoreCase))
                    return npc;
            }
            return null;
        }
    }
}
