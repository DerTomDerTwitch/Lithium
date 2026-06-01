using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Lithium.Helper;

namespace Lithium.Modules.Customers
{
    /// <summary>
    /// Repurposes an existing in-game NPC's Messages conversation as the "Lithium" contact: renames
    /// the conversation, marks it known so it shows up, and sends texts through it. The NPC is resolved
    /// by display name each time (no caching) so a save reload's fresh NPC instance is always used.
    /// </summary>
    public static class LithiumContact
    {
        public static void Send(string message)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            NPC npc = Resolve(config.Coverage.ContactNpcName);
            if (npc == null)
            {
                Core.Logger.Warning($"[Lithium] Coverage contact NPC '{config.Coverage.ContactNpcName}' not found; message dropped.");
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
