using System;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Building.Doors;
using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.ItemFramework;
using Lithium.Modules.Police.Contraband;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Il2CppFishNet;
using MelonLoader.Utils;
using UnityEngine;

namespace Lithium.Modules.Police.PropertyContraband
{
    /// <summary>
    /// Hotkey diagnostic (default F11): dumps everything the property-contraband scan looks at — officers, owned
    /// buildables classified as contraband, each property's exposure (open doors / blinds), proximity, and the
    /// resulting would-trigger verdict — to <c>UserData/Lithium/PoliceScan.txt</c>. Run it where you expect a cop
    /// to notice, then share the file.
    /// </summary>
    public static class PoliceContrabandDebug
    {
        public static void Dump()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== Lithium Police Contraband Scan Debug ===");

                ModPolice module = Core.Get<ModPolice>();
                if (module == null) { sb.AppendLine("ModPolice not registered."); Write(sb); return; }

                PropertyContrabandSettings settings = module.Configuration.PropertyContraband;
                sb.AppendLine($"IsServer: {InstanceFinder.IsServer}");
                sb.AppendLine($"Module.Enabled: {module.Configuration.Enabled}   PropertyContraband.Enabled: {settings.Enabled}   PoliceEntry.Enabled: {module.Configuration.PoliceEntry.Enabled}");
                sb.AppendLine($"Settings: Plants={settings.DetectPlants} Stations={settings.DetectStations} StoredDrugs={settings.DetectStoredDrugs} RequireOpenDoorsOrBlinds={settings.RequireOpenDoorsOrBlinds} MaxDistance={settings.MaxDistance} RequireFacing={settings.RequireFacing} MaxViewAngle={settings.MaxViewAngleDegrees} ScanInterval={settings.ScanIntervalSeconds}");

                Player local = Player.Local;
                Vector3 playerPos = local != null ? local.transform.position : Vector3.zero;
                sb.AppendLine($"Player.Local: {(local != null ? "ok" : "NULL")}  pos={playerPos}  pursuitLevel={(local?.CrimeData != null ? local.CrimeData.CurrentPursuitLevel.ToString() : "?")}");

                // Held-contraband feature (the OTHER trigger source — flags you when you hold an illegal item in view).
                ItemDefinition equipped = PlayerSingleton<PlayerInventory>.Instance?.EquippedItem?.Definition;
                bool heldIsContraband = equipped != null && (equipped.legalStatus != ELegalStatus.Legal || ContrabandMatcher.GetSeverity(equipped) != null);
                bool sealedHome = local != null && PropertyContrabandScanner.IsPlayerInSealedOwnedProperty(playerPos);
                sb.AppendLine($"Held item: {(equipped != null ? $"{equipped.ID}:{equipped.legalStatus} contraband={heldIsContraband}" : "(none)")}  playerInSealedOwnedProperty={sealedHome}  -> heldWouldFlag={heldIsContraband && !sealedHome && (local == null || !local.IsInVehicle)}");
                sb.AppendLine();

                Il2CppSystem.Collections.Generic.List<PoliceOfficer> officers = PoliceOfficer.Officers;
                int officerCount = officers != null ? officers.Count : 0;
                sb.AppendLine($"Officers: {officerCount}");
                for (int o = 0; o < officerCount; o++)
                {
                    PoliceOfficer officer = officers[o];
                    if (officer == null) { sb.AppendLine($"  #{o} NULL"); continue; }
                    float dist = local != null ? Vector3.Distance(officer.transform.position, playerPos) : -1f;
                    sb.AppendLine($"  #{o} distToPlayer={dist:0.0}m conscious={officer.IsConscious} ignorePlayers={officer.IgnorePlayers}");
                }
                sb.AppendLine();

                var doors = UnityEngine.Object.FindObjectsOfType<PropertyDoorController>(true);

                // Classify contraband and collect points (property + position).
                var all = UnityEngine.Object.FindObjectsOfType<BuildableItem>(true);
                int total = all != null ? all.Length : 0;
                var points = new System.Collections.Generic.List<PropertyContrabandScanner.ContrabandPoint>();
                sb.AppendLine($"BuildableItems in scene: {total}");
                sb.AppendLine("Contraband in owned properties:");
                for (int i = 0; i < total; i++)
                {
                    BuildableItem item = all[i];
                    if (item == null || item.isGhost || item.IsDestroyed) continue;
                    Property prop = item.ParentProperty;
                    if (prop == null || !prop.IsOwned) continue;

                    string desc = DescribeContraband(item, settings, out bool illegal, out Vector3 point);
                    if (!illegal) continue;
                    points.Add(new PropertyContrabandScanner.ContrabandPoint { Property = prop, Position = point });
                    sb.AppendLine($"  \"{prop.PropertyName}\" {desc}");
                }
                sb.AppendLine($"  ({points.Count} contraband object(s))");
                sb.AppendLine();

                // Per-property sightline through open doors/windows (the actual trigger model).
                sb.AppendLine("Sightline check (per property, through each open door/window):");
                foreach (PropertyContrabandScanner.PropertyGroup group in PropertyContrabandScanner.GroupByProperty(points))
                {
                    Property prop = group.Property;
                    sb.AppendLine($"  \"{prop.PropertyName}\" interior={group.Centroid} exposed={PropertyContrabandScanner.IsExposed(prop, doors)}");

                    bool anyOpening = false;
                    foreach (Vector3 opening in PropertyContrabandScanner.EnumerateOpenOpenings(prop, doors))
                    {
                        anyOpening = true;
                        PoliceOfficer seer = PropertyContrabandScanner.OfficerSeeingThroughOpening(officers, opening, group.Centroid, settings);

                        // Detail for the nearest officer to this opening.
                        PoliceOfficer nearest = null; float best = float.MaxValue;
                        for (int o = 0; o < officerCount; o++)
                        {
                            PoliceOfficer off = officers[o];
                            if (off == null) continue;
                            float d = Vector3.Distance(off.transform.position, opening);
                            if (d < best) { best = d; nearest = off; }
                        }
                        float seeAngle = nearest != null ? Vector3.Angle(Flat(opening - nearest.transform.position), Flat(group.Centroid - opening)) : -1f;
                        bool faces = nearest != null && PropertyContrabandScanner.IsFacing(nearest, opening, settings.MaxViewAngleDegrees);
                        sb.AppendLine($"      opening={opening}: nearestOfficer={best:0.0}m seeThroughAngle={seeAngle:0}°(<= {settings.OpeningViewAngleDegrees}) facingOpening={faces}  => seenThroughThis={(seer != null)}");
                    }
                    if (!anyOpening)
                        sb.AppendLine("      (no open doors/windows — sealed)");
                }
                sb.AppendLine();

                // Per-owned-property exposure breakdown (doors + blinds)
                sb.AppendLine("Owned properties — doors & blinds:");
                Il2CppSystem.Collections.Generic.List<Property> props = Property.Properties;
                int propCount = props != null ? props.Count : 0;
                for (int p = 0; p < propCount; p++)
                {
                    Property prop = props[p];
                    if (prop == null || !prop.IsOwned) continue;
                    sb.AppendLine($"  \"{prop.PropertyName}\" exposed={PropertyContrabandScanner.IsExposed(prop, doors)}");

                    if (doors != null)
                    {
                        for (int d = 0; d < doors.Length; d++)
                        {
                            PropertyDoorController door = doors[d];
                            if (door == null || door.Property == null || door.Property.GetInstanceID() != prop.GetInstanceID()) continue;
                            sb.AppendLine($"      door: IsOpen={door.IsOpen} openedByNPC={door.openedByNPC} autoOpenedForPlayer={door.autoOpenedForPlayer} countsAsOpening={PropertyContrabandScanner.IsDeliberatelyOpenDoor(door)}");
                        }
                    }

                    Il2CppSystem.Collections.Generic.List<InteractableToggleable> togs = prop.Toggleables;
                    if (togs != null)
                    {
                        for (int t = 0; t < togs.Count; t++)
                        {
                            InteractableToggleable tog = togs[t];
                            if (tog == null) continue;
                            sb.AppendLine($"      toggleable: activate=\"{tog.ActivateMessage}\" deactivate=\"{tog.DeactivateMessage}\" IsActivated={tog.IsActivated} -> openBlind={PropertyContrabandScanner.IsOpenBlind(tog)}");
                        }
                    }
                }

                Write(sb);
            }
            catch (Exception ex)
            {
                Log.Error($"[Police] Contraband scan dump failed: {ex}");
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static string DescribeContraband(BuildableItem item, PropertyContrabandSettings settings, out bool illegal, out Vector3 point)
        {
            illegal = false;
            point = PropertyContrabandScanner.SightPoint(item);

            if (PropertyContrabandScanner.IsDrugStation(item))
            {
                illegal = settings.DetectStations;
                return $"drug station (counts={settings.DetectStations})";
            }

            Pot pot = item.TryCast<Pot>();
            if (pot != null && pot.Plant != null)
            {
                illegal = settings.DetectPlants;
                point = PropertyContrabandScanner.SightPoint(pot);
                return $"pot with plant (counts={settings.DetectPlants})";
            }

            FloorRack rack = item.TryCast<FloorRack>();
            if (rack != null)
            {
                bool dirty = PropertyContrabandScanner.RackHasIllegalItem(rack);
                illegal = settings.DetectStoredDrugs && dirty;
                point = PropertyContrabandScanner.SightPoint(rack);
                return $"display rack (illegalItems={dirty})";
            }

            StorageEntity storage = item.TryCast<PlaceableStorageEntity>()?.StorageEntity
                                    ?? item.TryCast<SurfaceStorageEntity>()?.StorageEntity;
            if (storage != null)
            {
                point = PropertyContrabandScanner.SightPoint(storage);
                bool dirty = storage.ItemCount > 0 && PropertyContrabandScanner.ContainsIllegalItem(storage);
                illegal = settings.DetectStoredDrugs && dirty;
                return $"storage [{DescribeStorage(storage)}]";
            }

            return "(not contraband)";
        }

        private static string DescribeStorage(StorageEntity storage)
        {
            Il2CppSystem.Collections.Generic.List<ItemInstance> items = storage.GetAllItems();
            if (items == null || items.Count == 0) return "empty";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < 8; i++)
            {
                ItemDefinition def = items[i]?.Definition;
                if (def == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"{def.ID}:{def.legalStatus}{(PropertyContrabandScanner.IsContraband(def) ? "!" : "")}");
            }
            return sb.ToString();
        }

        private static void Write(StringBuilder sb)
        {
            string dir = System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "PoliceScan.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            Log.Warning($"[Police] Wrote contraband scan debug to {path}");
        }
    }
}
