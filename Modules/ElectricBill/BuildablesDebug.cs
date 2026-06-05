using System;
using System.Collections.Generic;
using System.Text;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Property;
using Lithium.Helper;
using MelonLoader.Utils;
using UnityEngine;

namespace Lithium.Modules.ElectricBill
{
    // F9 debug dump for authoring the appliance-metered electric bill. Lists every player-built
    // BuildableItem currently in the scene, grouped by the property it belongs to, with a per-item-ID
    // tally. The item ID is the stable key an ElectricBill config would price each appliance by, so this
    // is what to run once you've built things in each location, then share the resulting text file.
    public static class BuildablesDebug
    {
        private sealed class Entry
        {
            public string Name;
            public string Category;
            public int Count;
        }

        public static void Dump()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== Lithium Buildables (player-built, by property) ===");

                // includeInactive: true so culled/unrendered buildables in unvisited properties are caught.
                BuildableItem[] all = UnityEngine.Object.FindObjectsOfType<BuildableItem>(true);

                // propertyKey -> (display, ownership flag, itemId -> Entry)
                Dictionary<string, SortedDictionary<string, Entry>> byProperty = new();
                Dictionary<string, string> headers = new();
                int orphaned = 0;

                foreach (BuildableItem bi in all)
                {
                    if (bi == null || bi.isGhost || bi.IsDestroyed)
                        continue;

                    ItemInstance inst = bi.ItemInstance;
                    if (inst == null)
                        continue;

                    Property prop = bi.ParentProperty;
                    string key, header;
                    if (prop == null)
                    {
                        key = "(no property)";
                        header = "(no property)";
                        orphaned++;
                    }
                    else
                    {
                        key = string.IsNullOrEmpty(prop.PropertyCode) ? prop.PropertyName : prop.PropertyCode;
                        string owned = prop.IsOwned ? "* " : "  ";
                        header = $"{owned}\"{prop.PropertyName}\"  code={prop.PropertyCode}";
                    }

                    headers[key] = header;
                    if (!byProperty.TryGetValue(key, out SortedDictionary<string, Entry> items))
                    {
                        items = new SortedDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                        byProperty[key] = items;
                    }

                    string id = inst.ID ?? "(null id)";
                    if (!items.TryGetValue(id, out Entry e))
                    {
                        e = new Entry { Name = inst.Name, Category = inst.Category.ToString() };
                        items[id] = e;
                    }
                    e.Count++;
                }

                sb.AppendLine($"Total buildables: {all.Length}  (orphaned/no-property: {orphaned})");
                sb.AppendLine("* = owned property. Price each appliance in the ElectricBill config by its id.");
                sb.AppendLine();

                foreach (KeyValuePair<string, SortedDictionary<string, Entry>> kv in byProperty)
                {
                    sb.AppendLine(headers[kv.Key]);
                    int total = 0;
                    foreach (KeyValuePair<string, Entry> item in kv.Value)
                    {
                        Entry e = item.Value;
                        total += e.Count;
                        sb.AppendLine($"    {e.Count,3}x  id={item.Key,-28} name=\"{e.Name}\"  category={e.Category}");
                    }
                    sb.AppendLine($"    -- {total} buildable(s) --");
                    sb.AppendLine();
                }

                string dir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "Buildables.txt");
                System.IO.File.WriteAllText(path, sb.ToString());
                Log.Warning($"[ElectricBill] Wrote buildables dump to {path}");
            }
            catch (Exception ex)
            {
                Log.Error($"[ElectricBill] Buildables dump failed: {ex}");
            }
        }
    }
}
