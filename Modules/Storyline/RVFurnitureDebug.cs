using System;
using System.Collections.Generic;
using System.Text;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Property;
using Lithium.Helper;
using MelonLoader.Utils;
using UnityEngine;

namespace Lithium.Modules.Storyline
{
    // Debug dump for authoring the RV starter-furniture whitelist. Lists every BuildableItem currently
    // parented to the RV property, with its item ID and per-instance GUID. APPENDS to RVFurniture.txt so
    // you can launch a couple of fresh games in a row and accumulate the runs in one file. The point of
    // multiple fresh games is to see whether the starter furniture's GUIDs are stable across new saves:
    //   - if the same GUIDs show up in every run, we can whitelist by GUID (precise — only the original
    //     pieces are protected, bought-and-placed copies stay lootable);
    //   - if the GUIDs differ run to run, the GUID is generated per save and we fall back to whitelisting
    //     by item ID (coarser — every copy of those IDs in the RV is protected).
    public static class RVFurnitureDebug
    {
        public static void Dump()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("==================================================================");
                sb.AppendLine($"=== Lithium RV furniture dump @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                sb.AppendLine("==================================================================");

                // includeInactive: true so culled/unrendered buildables are caught.
                BuildableItem[] all = UnityEngine.Object.FindObjectsOfType<BuildableItem>(true);

                List<BuildableItem> rvItems = new List<BuildableItem>();
                foreach (BuildableItem bi in all)
                {
                    if (bi == null || bi.isGhost || bi.IsDestroyed)
                        continue;

                    Property prop = bi.ParentProperty;
                    if (prop == null || prop.TryCast<RV>() == null)
                        continue;

                    rvItems.Add(bi);
                }

                sb.AppendLine($"RV furniture count: {rvItems.Count}");
                sb.AppendLine();

                foreach (BuildableItem bi in rvItems)
                {
                    ItemInstance inst = bi.ItemInstance;
                    string id = inst?.ID ?? "(null id)";
                    string name = inst?.Name ?? "(null)";
                    string guid = bi.GUID.ToString();
                    sb.AppendLine($"id={id,-28} guid={guid}  name=\"{name}\"");
                }

                sb.AppendLine();

                string dir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "RVFurniture.txt");
                System.IO.File.AppendAllText(path, sb.ToString());
                Log.Warning($"[Storyline] Appended RV furniture dump ({rvItems.Count} items) to {path}");
            }
            catch (Exception ex)
            {
                Log.Error($"[Storyline] RV furniture dump failed: {ex}");
            }
        }
    }
}
