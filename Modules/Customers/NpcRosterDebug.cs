using System.Text;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Lithium.Helper;
using MelonLoader.Utils;

namespace Lithium.Modules.Customers
{
    /// <summary>
    /// Debug helper (F7): dumps every NPC with their messaging/customer status so a safe "unused" NPC
    /// can be picked to host the Lithium coverage-update contact. A line starting with "**" is a
    /// candidate: not a customer, already has a conversation, and has no message history yet.
    /// </summary>
    public static class NpcRosterDebug
    {
        public static void Dump()
        {
            try
            {
                HashSet<string> customerIds = [];
                foreach (Customer c in Customer.UnlockedCustomers.ToList())
                    if (c != null && c.NPC != null) customerIds.Add(c.NPC.ID);
                foreach (Customer c in Customer.LockedCustomers.ToList())
                    if (c != null && c.NPC != null) customerIds.Add(c.NPC.ID);

                List<NPC> npcs = NPCManager.NPCRegistry.ToList();

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== Lithium NPC Roster ===");
                sb.AppendLine("** = hijack candidate (not a customer, has a conversation, 0 messages).");
                sb.AppendLine($"Total NPCs: {npcs.Count}");
                sb.AppendLine();

                foreach (NPC npc in npcs)
                {
                    if (npc == null)
                        continue;

                    bool isCustomer = customerIds.Contains(npc.ID);
                    MSGConversation conv = npc.MSGConversation;
                    int msgs = conv != null ? conv.messageHistory.Count : -1;
                    string type = npc.GetIl2CppType().Name;
                    bool candidate = !isCustomer && conv != null && msgs == 0;

                    sb.AppendLine(
                        $"{(candidate ? "** " : "   ")}{npc.fullName,-22} type={type,-14} id={npc.ID,-22} " +
                        $"customer={isCustomer,-5} conv={(conv != null),-5} msgs={msgs,-3} important={npc.IsImportant,-5} region={npc.Region}");
                }

                string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "NpcRoster.txt");
                File.WriteAllText(path, sb.ToString());
                Core.Logger.Msg($"[NpcRoster] Wrote {npcs.Count} NPCs to {path}");
            }
            catch (Exception ex)
            {
                Core.Logger.Error($"[NpcRoster] Dump failed: {ex}");
            }
        }
    }
}
