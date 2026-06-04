using System;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Lithium.Helper;

namespace Lithium.Modules.Banking
{
    public static class BankingContact
    {
        public static void Send(string contactNpcName, string displayName, string message)
        {
            try
            {
                NPC npc = Resolve(contactNpcName);
                if (npc == null)
                {
                    Log.Warning($"[Banking] Report contact NPC '{contactNpcName}' not found; report dropped.");
                    return;
                }

                MSGConversation conv = npc.MSGConversation;
                if (conv != null)
                {
                    if (!string.IsNullOrEmpty(displayName) && conv.contactName != displayName)
                        conv.contactName = displayName;
                    conv.SetIsKnown(true);
                }

                MessagingManager.Instance.ReceiveMessage(new Message(message, Message.ESenderType.Other), true, npc.ID);
            }
            catch (Exception e)
            {
                Log.Warning($"[Banking] Failed to send report via '{contactNpcName}': {e.Message}");
            }
        }

        private static NPC Resolve(string name)
        {
            if (string.IsNullOrEmpty(name) || NPCManager.NPCRegistry == null)
                return null;

            foreach (NPC npc in NPCManager.NPCRegistry.ToList())
            {
                if (npc != null && string.Equals(npc.fullName, name, StringComparison.OrdinalIgnoreCase))
                    return npc;
            }

            foreach (NPC npc in NPCManager.NPCRegistry.ToList())
            {
                if (npc != null && !string.IsNullOrEmpty(npc.fullName)
                    && npc.fullName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    return npc;
            }

            return null;
        }
    }
}
