using Il2CppFishNet;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Building.Doors;
using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.Tiles;
using Lithium.Modules.Police.Contraband;
using UnityEngine;

// These types live in namespaces of the same name (IL2CPP nested-type artifact), so alias them.
using GameChemistryStation = Il2CppScheduleOne.ObjectScripts.ChemistryStation;
using GameLabOven = Il2CppScheduleOne.ObjectScripts.LabOven;
using GameBrickPress = Il2CppScheduleOne.ObjectScripts.BrickPress;

namespace Lithium.Modules.Police.PropertyContraband
{
    /// <summary>
    /// Server-side periodic scan: finds illegal infrastructure (grows, drug stations), display racks, and storage
    /// holding illegal items inside the player's owned properties, and if a conscious police officer is nearby
    /// **and the property is "exposed"** (a door or a blind/curtain is open), starts a pursuit. Walls block real
    /// line-of-sight to interior items, so instead of raycasting to each item the "exposed" state is used as the
    /// can-they-see-in proxy: seal the place (close doors and blinds) and patrols can't notice; leave it open and
    /// a passing cop busts you. Pursuit reuses the game's witnessed-drug-deal path (<see cref="NPCResponses_Police"/>).
    /// </summary>
    public static class PropertyContrabandScanner
    {
        internal struct ContrabandPoint
        {
            public Property Property;
            public Vector3 Position;
        }

        // Cached door list (static map fixtures — they don't spawn/despawn mid-scene). Refreshed on scene load.
        private static Il2CppArrayBase<PropertyDoorController> _doorsCache;

        /// <summary>Re-cache the property doors. Called on scene load (and lazily if missing) so the per-tick scan needs no scene scan.</summary>
        internal static void RefreshDoorsCache()
        {
            _doorsCache = UnityEngine.Object.FindObjectsOfType<PropertyDoorController>(true);
        }

        public static void Scan(PropertyContrabandSettings settings)
        {
            // Pursuit + crime are server-authoritative; only the host drives this.
            if (!InstanceFinder.IsServer)
                return;

            Player local = Player.Local;
            if (local == null || local.CrimeData == null)
                return;

            // Already being pursued — don't re-trigger (and don't pile crimes) every scan.
            if (local.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None)
                return;

            Il2CppSystem.Collections.Generic.List<PoliceOfficer> officers = PoliceOfficer.Officers;
            if (officers == null || officers.Count == 0)
                return;

            List<ContrabandPoint> contraband = CollectContraband(settings);
            if (contraband.Count == 0)
                return;

            // Doors are static map fixtures — cached once per scene (see RefreshDoorsCache) to avoid a per-tick scene scan.
            if (_doorsCache == null)
                RefreshDoorsCache();

            foreach (PropertyGroup group in GroupByProperty(contraband))
            {
                PoliceOfficer witness = FindWitnessForProperty(group.Property, group.Centroid, officers, _doorsCache, settings);
                if (witness != null)
                {
                    TriggerPursuit(local, witness);
                    return;
                }
            }

            if (Log.DebugEnabled)
                Log.Info($"[Police] Property scan: {contraband.Count} contraband point(s), no officer with a sightline through an open door/window.");
        }

        internal struct PropertyGroup
        {
            public Property Property;
            public Vector3 Centroid;   // a representative interior point (centroid of the property's contraband)
        }

        internal static List<PropertyGroup> GroupByProperty(List<ContrabandPoint> points)
        {
            var sums = new Dictionary<int, Vector3>();
            var counts = new Dictionary<int, int>();
            var props = new Dictionary<int, Property>();

            foreach (ContrabandPoint cp in points)
            {
                if (cp.Property == null)
                    continue;
                int id = cp.Property.GetInstanceID();
                sums[id] = (sums.TryGetValue(id, out Vector3 s) ? s : Vector3.zero) + cp.Position;
                counts[id] = (counts.TryGetValue(id, out int c) ? c : 0) + 1;
                props[id] = cp.Property;
            }

            var result = new List<PropertyGroup>();
            foreach (KeyValuePair<int, Property> kv in props)
                result.Add(new PropertyGroup { Property = kv.Value, Centroid = sums[kv.Key] / counts[kv.Key] });
            return result;
        }

        /// <summary>
        /// An officer who can actually see the contraband inside this property. With <see cref="PropertyContrabandSettings.RequireOpenDoorsOrBlinds"/>
        /// (default) that means a sightline *through an open door or window*: the officer must be in front of the
        /// opening and roughly aligned with the line through it into the room (not behind it, not at a grazing side
        /// angle). Otherwise it falls back to plain proximity to the contraband.
        /// </summary>
        private static PoliceOfficer FindWitnessForProperty(
            Property property, Vector3 interior, Il2CppSystem.Collections.Generic.List<PoliceOfficer> officers,
            Il2CppArrayBase<PropertyDoorController> doors, PropertyContrabandSettings settings)
        {
            if (!settings.RequireOpenDoorsOrBlinds)
                return NearestSeeingOfficer(officers, interior, settings);

            foreach (Vector3 opening in EnumerateOpenOpenings(property, doors))
            {
                PoliceOfficer witness = OfficerSeeingThroughOpening(officers, opening, interior, settings);
                if (witness != null)
                    return witness;
            }

            return null;
        }

        /// <summary>World positions of every open door and open blind/window on the property — the points an officer can see in through.</summary>
        internal static IEnumerable<Vector3> EnumerateOpenOpenings(Property property, Il2CppArrayBase<PropertyDoorController> doors)
        {
            int propertyId = property.GetInstanceID();

            if (doors != null)
            {
                for (int i = 0; i < doors.Length; i++)
                {
                    PropertyDoorController door = doors[i];
                    if (!IsDeliberatelyOpenDoor(door))
                        continue;
                    Property doorProperty = door.Property;
                    if (doorProperty != null && doorProperty.GetInstanceID() == propertyId)
                        yield return door.transform.position;
                }
            }

            Il2CppSystem.Collections.Generic.List<InteractableToggleable> toggleables = property.Toggleables;
            if (toggleables != null)
            {
                for (int i = 0; i < toggleables.Count; i++)
                {
                    InteractableToggleable toggleable = toggleables[i];
                    if (IsOpenBlind(toggleable))
                        yield return toggleable.transform.position;
                }
            }
        }

        /// <summary>
        /// A door counts as deliberate exposure only if it's open and was NOT auto-opened. Property doors pop open
        /// whenever the player (<c>autoOpenedForPlayer</c>) or an NPC (<c>openedByNPC</c>) walks near and auto-close
        /// on a timer, so those transient opens must not count — otherwise just being home (or a passing patrol)
        /// keeps the door "open" and re-busts you the instant a pursuit's grace ends.
        /// </summary>
        internal static bool IsDeliberatelyOpenDoor(PropertyDoorController door)
        {
            return door != null && door.IsOpen && !door.openedByNPC && !door.autoOpenedForPlayer;
        }

        /// <summary>
        /// Whether any officer can see through this opening into the room. The officer must be within range of the
        /// opening, positioned so the opening lies between them and the interior (the angle officer→opening→interior
        /// is within <see cref="PropertyContrabandSettings.OpeningViewAngleDegrees"/> — that rejects being behind the
        /// opening or off to a grazing side), and — with <see cref="PropertyContrabandSettings.RequireFacing"/> —
        /// looking toward the opening.
        /// </summary>
        internal static PoliceOfficer OfficerSeeingThroughOpening(
            Il2CppSystem.Collections.Generic.List<PoliceOfficer> officers, Vector3 openingPos, Vector3 interior,
            PropertyContrabandSettings settings)
        {
            for (int o = 0; o < officers.Count; o++)
            {
                PoliceOfficer officer = officers[o];
                if (officer == null || !officer.IsConscious || officer.IgnorePlayers)
                    continue;

                Vector3 officerPos = officer.transform.position;
                if (Vector3.Distance(officerPos, openingPos) > settings.MaxDistance)
                    continue;

                if (!CanSeeThroughOpening(officerPos, openingPos, interior, settings.OpeningViewAngleDegrees))
                    continue;

                if (settings.RequireFacing && !IsFacing(officer, openingPos, settings.MaxViewAngleDegrees))
                    continue;

                return officer;
            }

            return null;
        }

        /// <summary>Horizontal alignment test: is the opening between the officer and the room interior (officer in front, not behind/grazing)?</summary>
        internal static bool CanSeeThroughOpening(Vector3 officerPos, Vector3 openingPos, Vector3 interior, float maxAngleDegrees)
        {
            Vector3 officerToOpening = openingPos - officerPos;
            officerToOpening.y = 0f;
            Vector3 openingToInterior = interior - openingPos;
            openingToInterior.y = 0f;

            // Contraband sitting right at the opening — no meaningful axis; treat as visible.
            if (officerToOpening.sqrMagnitude < 0.0001f || openingToInterior.sqrMagnitude < 0.0001f)
                return true;

            return Vector3.Angle(officerToOpening, openingToInterior) <= maxAngleDegrees;
        }

        /// <summary>
        /// An officer who could plausibly see the point: conscious, not ignoring players, within range, and (when
        /// <see cref="PropertyContrabandSettings.RequireFacing"/>) generally facing toward it (horizontal angle from
        /// their look direction within <see cref="PropertyContrabandSettings.MaxViewAngleDegrees"/>).
        /// </summary>
        internal static PoliceOfficer NearestSeeingOfficer(
            Il2CppSystem.Collections.Generic.List<PoliceOfficer> officers, Vector3 position, PropertyContrabandSettings settings)
        {
            for (int o = 0; o < officers.Count; o++)
            {
                PoliceOfficer officer = officers[o];
                if (officer == null || !officer.IsConscious || officer.IgnorePlayers)
                    continue;

                if (Vector3.Distance(officer.transform.position, position) > settings.MaxDistance)
                    continue;

                if (settings.RequireFacing && !IsFacing(officer, position, settings.MaxViewAngleDegrees))
                    continue;

                return officer;
            }

            return null;
        }

        /// <summary>Whether the officer is generally facing toward a point (horizontal angle only — they're often below an upstairs room).</summary>
        internal static bool IsFacing(PoliceOfficer officer, Vector3 point, float maxAngleDegrees)
        {
            Transform origin = officer.Awareness?.VisionCone?.VisionOrigin;
            if (origin == null)
                return true; // can't determine facing — don't block on it

            Vector3 forward = origin.forward;
            forward.y = 0f;
            Vector3 toTarget = point - origin.position;
            toTarget.y = 0f;
            if (forward.sqrMagnitude < 0.0001f || toTarget.sqrMagnitude < 0.0001f)
                return true;

            return Vector3.Angle(forward, toTarget) <= maxAngleDegrees;
        }

        // ── Exposure (doors / blinds) ────────────────────────────────────────────────────────────────

        /// <summary>
        /// True if the player stands inside one of their own properties and it's sealed (not deliberately exposed).
        /// Used to make held contraband safe when you're hidden at home with the doors/blinds closed — the same
        /// "sealed = hidden" rule the property scan uses.
        /// </summary>
        internal static bool IsPlayerInSealedOwnedProperty(Vector3 position)
        {
            if (_doorsCache == null)
                RefreshDoorsCache();

            foreach (Property property in GetOwnedProperties())
            {
                if (property.DoBoundsContainPoint(position))
                    return !IsExposed(property, _doorsCache);
            }

            return false;
        }

        /// <summary>A property is exposed if any of its doors is open or any of its blinds/curtains is open (used by the F11 dump).</summary>
        internal static bool IsExposed(Property property, Il2CppArrayBase<PropertyDoorController> doors)
        {
            if (doors != null)
            {
                int propertyId = property.GetInstanceID();
                for (int i = 0; i < doors.Length; i++)
                {
                    PropertyDoorController door = doors[i];
                    if (!IsDeliberatelyOpenDoor(door))
                        continue;
                    Property doorProperty = door.Property;
                    if (doorProperty != null && doorProperty.GetInstanceID() == propertyId)
                        return true;
                }
            }

            Il2CppSystem.Collections.Generic.List<InteractableToggleable> toggleables = property.Toggleables;
            if (toggleables != null)
            {
                for (int i = 0; i < toggleables.Count; i++)
                {
                    if (IsOpenBlind(toggleables[i]))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a property toggleable is a window covering that is currently open. Identified by its interaction
        /// prompt mentioning blinds/curtains/shutters; the action it currently offers tells us the state (we never
        /// depend on which boolean the prefab calls "activated"): an action that OPENS the covering means it's
        /// currently closed, one that CLOSES it means it's open.
        ///
        /// Fail-safe to <b>closed</b> when the wording isn't recognised — a wrongful bust while you've sealed up is
        /// far more frustrating than a cop occasionally not noticing through an oddly-labelled open blind.
        /// </summary>
        internal static bool IsOpenBlind(InteractableToggleable toggleable)
        {
            if (toggleable == null)
                return false;

            string activate = toggleable.ActivateMessage ?? string.Empty;
            string deactivate = toggleable.DeactivateMessage ?? string.Empty;
            string both = (activate + " " + deactivate).ToLowerInvariant();
            if (!both.Contains("blind") && !both.Contains("curtain") && !both.Contains("shutter"))
                return false;

            // The prompt currently shown to the player is the action available right now.
            string current = (toggleable.IsActivated ? deactivate : activate).ToLowerInvariant();

            // Offered action OPENS it → it's currently CLOSED (sealed). Checked first so "open up"/"open the blinds"
            // can't be misread by the "up" in the close branch.
            if (current.Contains("open") || current.Contains("raise"))
                return false;

            // Offered action CLOSES it → it's currently OPEN.
            if (current.Contains("close") || current.Contains("shut") || current.Contains("lower"))
                return true;

            // Unrecognised wording — assume sealed so a mislabelled blind never wrongly exposes the property.
            return false;
        }

        // ── Contraband collection ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Collects each illegal object across the player's owned properties and businesses by reading each property's
        /// live <c>BuildableItems</c> list — no scene-wide <c>FindObjectsOfType</c>, so it's cheap to run every tick.
        /// (A buildable's <c>ParentProperty</c> is set in the same call that adds it to that list, so this is the same
        /// set a scene scan filtered by <c>ParentProperty.IsOwned</c> would yield.)
        /// </summary>
        private static List<ContrabandPoint> CollectContraband(PropertyContrabandSettings settings)
        {
            var points = new List<ContrabandPoint>();

            foreach (Property property in GetOwnedProperties())
            {
                Il2CppSystem.Collections.Generic.List<BuildableItem> buildables = property.BuildableItems;
                if (buildables == null)
                    continue;

                for (int b = 0; b < buildables.Count; b++)
                {
                    BuildableItem item = buildables[b];
                    if (item == null || item.isGhost || item.IsDestroyed)
                        continue;

                    if (TryClassifyContraband(item, settings, out Vector3 point))
                        points.Add(new ContrabandPoint { Property = property, Position = point });
                }
            }

            return points;
        }

        /// <summary>Owned properties and owned businesses (businesses keep a separate list), deduped.</summary>
        private static List<Property> GetOwnedProperties()
        {
            var result = new List<Property>();
            var seen = new HashSet<int>();

            Il2CppSystem.Collections.Generic.List<Property> properties = Property.OwnedProperties;
            if (properties != null)
            {
                for (int i = 0; i < properties.Count; i++)
                {
                    Property p = properties[i];
                    if (p != null && seen.Add(p.GetInstanceID()))
                        result.Add(p);
                }
            }

            Il2CppSystem.Collections.Generic.List<Business> businesses = Business.OwnedBusinesses;
            if (businesses != null)
            {
                for (int i = 0; i < businesses.Count; i++)
                {
                    Business b = businesses[i];
                    if (b != null && seen.Add(b.GetInstanceID()))
                        result.Add(b);
                }
            }

            return result;
        }

        /// <summary>True if this owned-property buildable is illegal infrastructure or holds contraband.</summary>
        internal static bool TryClassifyContraband(BuildableItem item, PropertyContrabandSettings settings, out Vector3 point)
        {
            point = default;

            if (settings.DetectStations && IsDrugStation(item))
            {
                point = SightPoint(item);
                return true;
            }

            if (settings.DetectPlants)
            {
                Pot pot = item.TryCast<Pot>();
                if (pot != null && pot.Plant != null)
                {
                    point = SightPoint(pot);
                    return true;
                }
            }

            if (settings.DetectStoredDrugs)
            {
                // Display racks (storefront shelving) hold product on procedural tiles, not a StorageEntity.
                FloorRack rack = item.TryCast<FloorRack>();
                if (rack != null && RackHasIllegalItem(rack))
                {
                    point = SightPoint(rack);
                    return true;
                }

                // Storage racks and surface shelves/tables both expose their StorageEntity via a field.
                StorageEntity storage = item.TryCast<PlaceableStorageEntity>()?.StorageEntity
                                        ?? item.TryCast<SurfaceStorageEntity>()?.StorageEntity;
                if (storage != null && storage.ItemCount > 0 && ContainsIllegalItem(storage))
                {
                    point = SightPoint(storage);
                    return true;
                }
            }

            return false;
        }

        internal static bool IsDrugStation(BuildableItem item)
        {
            return item.TryCast<GameChemistryStation>() != null
                || item.TryCast<GameLabOven>() != null
                || item.TryCast<MixingStation>() != null
                || item.TryCast<DryingRack>() != null
                || item.TryCast<GameBrickPress>() != null
                || item.TryCast<Cauldron>() != null
                || item.TryCast<PackagingStation>() != null;
        }

        internal static bool RackHasIllegalItem(FloorRack rack)
        {
            Il2CppSystem.Collections.Generic.List<ProceduralTile> tiles = rack.ProceduralTiles;
            if (tiles == null)
                return false;

            for (int t = 0; t < tiles.Count; t++)
            {
                Il2CppSystem.Collections.Generic.List<ProceduralGridItem> occupants = tiles[t]?.Occupants;
                if (occupants == null)
                    continue;

                for (int i = 0; i < occupants.Count; i++)
                {
                    if (IsContraband(occupants[i]?.ItemInstance?.Definition))
                        return true;
                }
            }

            return false;
        }

        internal static bool ContainsIllegalItem(StorageEntity storage)
        {
            Il2CppSystem.Collections.Generic.List<ItemInstance> items = storage.GetAllItems();
            if (items == null)
                return false;

            for (int i = 0; i < items.Count; i++)
            {
                if (IsContraband(items[i]?.Definition))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// An item is contraband if the game already marks it illegal, or if our own rules (all seeds / all products /
        /// the explicit list) classify it — so the property scan works even if the held-item marking didn't run.
        /// </summary>
        internal static bool IsContraband(ItemDefinition definition)
        {
            if (definition == null)
                return false;

            return definition.legalStatus != ELegalStatus.Legal
                || ContrabandMatcher.GetSeverity(definition) != null;
        }

        /// <summary>A point on the object likely to be visible — its render-bounds centre, else origin + half a metre.</summary>
        internal static Vector3 SightPoint(Component component)
        {
            Renderer renderer = component.GetComponentInChildren<Renderer>();
            if (renderer != null)
                return renderer.bounds.center;

            return component.transform.position + Vector3.up * 0.5f;
        }

        private static void TriggerPursuit(Player local, PoliceOfficer officer)
        {
            // Mirrors NPCResponses_Police.NoticedDrugDeal: record the crime, escalate, and chase.
            local.CrimeData.AddCrime(new DrugTrafficking());
            local.CrimeData.SetPursuitLevel(PlayerCrimeData.EPursuitLevel.Arresting);
            officer.BeginFootPursuit_Networked(local.PlayerCode, true);

            Log.Warning("[Police] An officer spotted contraband inside one of your properties — pursuit started.");
        }
    }
}
