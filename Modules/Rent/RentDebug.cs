using System;
using System.Text;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.Property;
using Lithium.Helper;
using MelonLoader.Utils;
using UnityEngine;

namespace Lithium.Modules.Rent
{
    /// <summary>
    /// Debug helper (F8): dumps every dead drop (name, GUID, region, world position) and every owned
    /// property/business (display name, code, position) plus the nearest dead drop to each. Use it in-game
    /// to fill in the per-location <c>DeadDropName</c>/<c>DeadDropGUID</c> mapping in Rent.json.
    /// </summary>
    public static class RentDebug
    {
        public static void Dump()
        {
            try
            {
                StringBuilder sb = new StringBuilder();

                sb.AppendLine("=== Lithium Dead Drops ===");
                if (DeadDrop.DeadDrops != null)
                {
                    sb.AppendLine($"Total: {DeadDrop.DeadDrops.Count}");
                    foreach (DeadDrop dd in DeadDrop.DeadDrops)
                    {
                        if (dd == null)
                            continue;
                        Vector3 p = dd.transform.position;
                        sb.AppendLine($"  name=\"{dd.DeadDropName}\"  guid={dd.GUID}  region={dd.Region}  pos=({p.x:F1}, {p.y:F1}, {p.z:F1})");
                    }
                }
                sb.AppendLine();

                sb.AppendLine("=== Properties & Businesses (owned=*) ===");
                HashSet<string> seen = new();
                DumpProperties(sb, Property.Properties, seen);
                DumpBusinesses(sb, seen);
                sb.AppendLine();

                DumpContacts(sb);

                string dir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "RentLocations.txt");
                System.IO.File.WriteAllText(path, sb.ToString());
                // Always-visible (this is a manually triggered user action, not background logging).
                Log.Warning($"[Rent] Wrote dead drop / property dump to {path}");
            }
            catch (Exception ex)
            {
                Log.Error($"[Rent] Dump failed: {ex}");
            }
        }

        private static void DumpProperties(StringBuilder sb, Il2CppSystem.Collections.Generic.List<Property> list, HashSet<string> seen)
        {
            if (list == null)
                return;
            foreach (Property prop in list)
            {
                if (prop == null)
                    continue;
                WriteProperty(sb, prop, seen);
            }
        }

        private static void DumpBusinesses(StringBuilder sb, HashSet<string> seen)
        {
            if (Business.Businesses == null)
                return;
            foreach (Business b in Business.Businesses)
            {
                if (b == null)
                    continue;
                WriteProperty(sb, b, seen);
            }
        }

        private static void WriteProperty(StringBuilder sb, Property prop, HashSet<string> seen)
        {
            if (!string.IsNullOrEmpty(prop.PropertyCode) && !seen.Add(prop.PropertyCode))
                return;

            Vector3 pos = prop.transform.position;
            DeadDrop nearest = NearestDrop(pos, out float dist);
            string near = nearest != null ? $"\"{nearest.DeadDropName}\" ({dist:F0}m)" : "(none)";
            string owned = prop.IsOwned ? "* " : "  ";
            sb.AppendLine($"{owned}name=\"{prop.PropertyName}\"  code={prop.PropertyCode}  pos=({pos.x:F1}, {pos.y:F1}, {pos.z:F1})  nearestDrop={near}");
        }

        /// <summary>
        /// Lists NPCs usable as a location's <c>ContactNpcName</c>. Entries marked ** are good candidates
        /// (they have a Messages conversation and are not customers); customers are flagged because texting
        /// them for rent could interfere with their normal deals.
        /// </summary>
        private static void DumpContacts(StringBuilder sb)
        {
            sb.AppendLine("=== Available Contacts (set as ContactNpcName) ===");
            sb.AppendLine("** = good candidate (has a conversation, not a customer). Avoid customers — texting them may interfere with deals.");

            if (NPCManager.NPCRegistry == null)
            {
                sb.AppendLine("  (NPC registry not available — load into a save first)");
                return;
            }

            HashSet<string> customerIds = new();
            if (Customer.UnlockedCustomers != null)
                foreach (Customer c in Customer.UnlockedCustomers)
                    if (c != null && c.NPC != null) customerIds.Add(c.NPC.ID);
            if (Customer.LockedCustomers != null)
                foreach (Customer c in Customer.LockedCustomers)
                    if (c != null && c.NPC != null) customerIds.Add(c.NPC.ID);

            foreach (NPC npc in NPCManager.NPCRegistry)
            {
                if (npc == null)
                    continue;
                bool isCustomer = customerIds.Contains(npc.ID);
                bool hasConv = npc.MSGConversation != null;
                bool candidate = hasConv && !isCustomer;
                string type = npc.GetIl2CppType().Name;
                sb.AppendLine($"{(candidate ? "** " : "   ")}name=\"{npc.fullName}\"  type={type}  customer={isCustomer}  conversation={hasConv}");
            }
        }

        private static DeadDrop NearestDrop(Vector3 from, out float dist)
        {
            DeadDrop best = null;
            dist = float.MaxValue;
            if (DeadDrop.DeadDrops == null)
                return null;
            foreach (DeadDrop dd in DeadDrop.DeadDrops)
            {
                if (dd == null)
                    continue;
                float d = Vector3.Distance(dd.transform.position, from);
                if (d < dist)
                {
                    dist = d;
                    best = dd;
                }
            }
            return best;
        }
    }
}
