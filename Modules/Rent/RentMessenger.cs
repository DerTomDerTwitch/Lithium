using System;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Lithium.Helper;

namespace Lithium.Modules.Rent
{
    /// <summary>
    /// Sends the rent texts through an existing in-game NPC's Messages conversation (resolved by name every
    /// time, so a reloaded save's fresh NPC instance is used): marks the conversation known so it shows up,
    /// then posts the message. The NPC is configured per location (<see cref="RentLocationConfiguration"/>),
    /// so different properties can be fronted by different contacts (e.g. the Fixer). The conversation's own
    /// name is deliberately left untouched, so repurposing an NPC never interferes with their normal dialog.
    /// </summary>
    public static class RentMessenger
    {
        public static void Send(RentLocationConfiguration location, string message)
        {
            if (location == null)
                return;

            try
            {
                NPC npc = Resolve(location.ContactNpcName);
                if (npc == null)
                {
                    Log.Warning($"[Rent] Contact NPC '{location.ContactNpcName}' not found; message dropped: {message}");
                    return;
                }

                MSGConversation conv = npc.MSGConversation;
                if (conv != null)
                    conv.SetIsKnown(true);

                MessagingManager.Instance.ReceiveMessage(new Message(message, Message.ESenderType.Other), true, npc.ID);
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Failed to send message via '{location.ContactNpcName}': {e.Message}");
            }
        }

        private static NPC Resolve(string fullName)
        {
            if (string.IsNullOrEmpty(fullName) || NPCManager.NPCRegistry == null)
                return null;

            foreach (NPC npc in NPCManager.NPCRegistry.ToList())
            {
                if (npc != null && string.Equals(npc.fullName, fullName, StringComparison.OrdinalIgnoreCase))
                    return npc;
            }
            return null;
        }
    }
}
