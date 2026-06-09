using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Vision;
using Lithium.Modules.Police.Contraband;
using Lithium.Modules.Police.PoliceEntry;
using Lithium.Modules.Police.PropertyContraband;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace Lithium.Modules.Police
{
    public class ModPoliceConfiguration : ModuleConfiguration
    {
        public override string Name => "Police";

        /// <summary>The "items illegal to carry in hand" feature. See <see cref="ContrabandSettings"/>.</summary>
        public ContrabandSettings Contraband { get; set; } = new();

        /// <summary>The "police pursue when they see contraband inside your property" feature.</summary>
        public PropertyContrabandSettings PropertyContraband { get; set; } = new();

        /// <summary>Let pursuing police follow you into your owned properties. See <see cref="PoliceEntrySettings"/>.</summary>
        public PoliceEntrySettings PoliceEntry { get; set; } = new();

        /// <summary>
        /// Seconds of "breathing room" after a pursuit ends during which neither held-contraband nor property
        /// detection re-triggers. Without it, the instant an arrest wears off a cop still standing at your (NPC-opened)
        /// door — or the contraband still in your hand — re-arrests you on the same frame. Gives you time to seal up
        /// or slip away. Set 0 to disable.
        /// </summary>
        public float PostPursuitGraceSeconds { get; set; } = 20.0f;
    }

    /// <summary>
    /// Settings for making the police pursue when a conscious officer can actually see illegal things placed inside
    /// one of your owned properties: growing plants, drug stations (chemistry, lab oven, mixing, drying rack, brick
    /// press, cauldron, packaging), and storage holding items whose <c>legalStatus</c> is not <c>Legal</c>.
    /// </summary>
    public class PropertyContrabandSettings
    {
        /// <summary>Per-feature toggle, gated under the Police module's own <c>Enabled</c>.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Treat a pot with an active plant (weed/coca — all plants are illegal) as contraband.</summary>
        public bool DetectPlants { get; set; } = true;

        /// <summary>Treat drug stations (chemistry, lab oven, mixing, drying rack, brick press, cauldron, packaging) as contraband.</summary>
        public bool DetectStations { get; set; } = true;

        /// <summary>Treat storage (racks/shelves) holding any item with <c>legalStatus != Legal</c> as contraband.</summary>
        public bool DetectStoredDrugs { get; set; } = true;

        /// <summary>
        /// Only let police notice contraband when the property is "exposed" — a door is open or a blind/curtain is
        /// open. Sealing up (closing doors and blinds) hides your operation from passing patrols; leaving it open is
        /// the risk. Default on. Set false to detect purely on proximity (a nearby cop always notices).
        /// </summary>
        public bool RequireOpenDoorsOrBlinds { get; set; } = true;

        /// <summary>How close (metres) an officer must be to a contraband object to notice it (the game's own vision range is ~25m).</summary>
        public float MaxDistance { get; set; } = 25.0f;

        /// <summary>
        /// How wide a cone (degrees) in front of an open door/window an officer can see in through. The angle is
        /// measured along the officer→opening→interior line: an officer behind the opening or at a grazing side angle
        /// can't see inside. Smaller = must be more square-on to the opening; larger = sees in from sharper angles.
        /// </summary>
        public float OpeningViewAngleDegrees { get; set; } = 60.0f;

        /// <summary>
        /// Also require the officer to be looking toward the opening (not just standing in front of it). Default on.
        /// </summary>
        public bool RequireFacing { get; set; } = true;

        /// <summary>Max angle (degrees) between the officer's look direction and the open door/window for them to count as looking at it. 90 = front half.</summary>
        public float MaxViewAngleDegrees { get; set; } = 90.0f;

        /// <summary>How often (seconds) to scan owned properties. Lower = more responsive, slightly more work.</summary>
        public float ScanIntervalSeconds { get; set; } = 1.0f;
    }

    /// <summary>
    /// Settings for letting pursuing police enter your owned properties (best-effort; see <see cref="PropertyDoorAccess"/>).
    /// </summary>
    public class PoliceEntrySettings
    {
        /// <summary>Per-feature toggle, gated under the Police module's own <c>Enabled</c>.</summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Settings for making configured items illegal to carry in hand: while the local player holds one of these
    /// in view of a police officer, they get pursued (or body-searched). Defaults cover all seeds, all products
    /// (weed/meth/cocaine/mushrooms) and coca leaf; extend via <see cref="IllegalItems"/>.
    /// </summary>
    public class ContrabandSettings
    {
        /// <summary>Per-feature toggle, gated under the Police module's own <c>Enabled</c>.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Mark every seed (any <c>SeedDefinition</c>) as illegal at <see cref="SeedSeverity"/>.</summary>
        public bool MarkAllSeeds { get; set; } = true;

        /// <summary>
        /// Mark every product (any <c>ProductDefinition</c> — weed, meth, cocaine, mushrooms, …) as illegal
        /// at <see cref="ProductSeverity"/>. Mushrooms are products, so this covers them.
        /// </summary>
        public bool MarkAllProducts { get; set; } = true;

        /// <summary>Legal status applied to the seeds matched by <see cref="MarkAllSeeds"/>.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ELegalStatus SeedSeverity { get; set; } = ELegalStatus.ModerateSeverityDrug;

        /// <summary>Legal status applied to the products matched by <see cref="MarkAllProducts"/>.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ELegalStatus ProductSeverity { get; set; } = ELegalStatus.HighSeverityDrug;

        /// <summary>
        /// Explicit per-item-ID overrides — THE easily-extensible list. Maps an item ID to the legal status it
        /// should carry, and takes precedence over the bulk <see cref="MarkAllSeeds"/>/<see cref="MarkAllProducts"/>
        /// rules. Use it to add items the category rules miss (e.g. coca leaf, an Ingredient), or to force a
        /// specific severity. Set an entry to <c>Legal</c> to explicitly un-mark something.
        /// Defaults seed the non-category contraband (coca leaf). To add more, just add <c>"itemid": "Severity"</c>.
        /// </summary>
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public Dictionary<string, ELegalStatus> IllegalItems { get; set; } = new()
        {
            { "cocaleaf", ELegalStatus.HighSeverityDrug },
        };

        /// <summary>Item IDs to never treat as contraband (escape hatch — wins over every rule above).</summary>
        public List<string> IgnoredItemIds { get; set; } = [];

        /// <summary>
        /// What the police do when they see you holding contraband in your hand.
        /// <c>DrugDealing</c>, <c>DisobeyingCurfew</c>, <c>PettyCrime</c> and <c>Wanted</c> all trigger an immediate
        /// foot pursuit; <c>Suspicious</c> instead makes the nearest officer walk over and body-search you first.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public EVisualState PursuitTrigger { get; set; } = EVisualState.DrugDealing;
    }

    /// <summary>
    /// Police-behaviour tweaks. Currently hosts the contraband feature; more police features will be added here.
    ///
    /// Contraband makes configured items illegal to carry in hand via two cooperating parts:
    ///   * <see cref="ContrabandMarker"/> writes an illegal <c>legalStatus</c> onto the matching item definitions
    ///     (mirrors the "Illegal Seeds" mod), captured/restored so a live reload can undo it. This also makes the
    ///     items count as contraband for the game's own body-search confiscation.
    ///   * <see cref="DriveUpdate"/> (driven from <c>Core.OnUpdate</c>) applies a visible "suspicious" state to the
    ///     player whenever the equipped item is illegal (mirrors the "Observant Cops" mod), which the police vision
    ///     system reacts to via <see cref="ContrabandSettings.PursuitTrigger"/>.
    /// </summary>
    public class ModPolice : ModuleBase<ModPoliceConfiguration>
    {
        private const string ContrabandStateLabel = "lithium_contraband";

        // Whether we currently have the suspicious state applied to the player (transition-guards the networked
        // ApplyState/RemoveState calls so a per-frame driver doesn't spam them).
        private bool _flagged;

        // Next time (Time.time) the property scan is allowed to run.
        private float _nextScanTime;

        // Post-pursuit grace: detection is suppressed until this time after a chase ends.
        private bool _pursuitWasActive;
        private float _graceUntil;

        public override void Apply()
        {
            // (Re)mark the item definitions for the current config; also restores originals on a live reload
            // that turned the feature off.
            ContrabandMarker.ReapplyAll();

            // Open (or restore) property doors so pursuing police can follow.
            PropertyDoorAccess.Apply(Configuration.PoliceEntry);

            // Re-cache the door list for the (scene-scan-free) property scan.
            PropertyContrabandScanner.RefreshDoorsCache();

            // Scene (re)loaded — re-evaluate the held-item state from scratch and scan again promptly,
            // and clear any stale post-pursuit grace tracking from a previous save/session.
            _flagged = false;
            _nextScanTime = 0f;
            _pursuitWasActive = false;
            _graceUntil = 0f;
        }

        public void DriveUpdate()
        {
            if (!Configuration.Enabled)
            {
                ClearFlag();
                return;
            }

            UpdatePursuitGrace();

            DriveHeldContraband(Configuration.Contraband);
            DrivePropertyScan(Configuration.PropertyContraband);
        }

        /// <summary>Starts the grace window on the frame a pursuit ends, so detection doesn't re-fire instantly.</summary>
        private void UpdatePursuitGrace()
        {
            PlayerCrimeData crime = Player.Local?.CrimeData;
            bool active = crime != null && crime.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None;

            if (_pursuitWasActive && !active)
                _graceUntil = Time.time + Mathf.Max(0f, Configuration.PostPursuitGraceSeconds);

            _pursuitWasActive = active;
        }

        private bool InPostPursuitGrace => Time.time < _graceUntil;

        private void DriveHeldContraband(ContrabandSettings contraband)
        {
            if (!contraband.Enabled || InPostPursuitGrace)
            {
                ClearFlag();
                return;
            }

            Player local = Player.Local;
            if (local == null)
            {
                _flagged = false;
                return;
            }

            bool desired = ShouldFlag(local, contraband);
            if (desired == _flagged)
                return;

            EntityVisibility visibility = local.Visibility;
            if (visibility == null)
                return;

            if (desired)
                visibility.ApplyState(ContrabandStateLabel, contraband.PursuitTrigger, 0f);
            else
                visibility.RemoveState(ContrabandStateLabel, 0f);

            _flagged = desired;
        }

        private void DrivePropertyScan(PropertyContrabandSettings settings)
        {
            if (!settings.Enabled)
                return;

            float now = Time.time;
            if (now < _nextScanTime)
                return;
            _nextScanTime = now + Mathf.Max(0.1f, settings.ScanIntervalSeconds);

            if (InPostPursuitGrace)
                return;

            PropertyContrabandScanner.Scan(settings);
        }

        private static bool ShouldFlag(Player local, ContrabandSettings contraband)
        {
            // Holding it inside a vehicle can't be seen, and re-flagging mid-pursuit just adds noise.
            if (local.IsInVehicle)
                return false;
            if (local.CrimeData != null && local.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None)
                return false;

            // Hidden at home: sealed inside one of your own properties — holding contraband there is safe.
            if (PropertyContrabandScanner.IsPlayerInSealedOwnedProperty(local.transform.position))
                return false;

            ItemInstance equipped = PlayerSingleton<PlayerInventory>.Instance?.EquippedItem;
            ItemDefinition definition = equipped?.Definition;
            if (definition == null)
                return false;

            if (contraband.IgnoredItemIds.Contains(definition.ID))
                return false;

            // Drive off the live legal status we wrote onto the definitions (consistent with the game's own
            // contraband check), so anything marked illegal — by us or vanilla — counts.
            return definition.legalStatus != ELegalStatus.Legal;
        }

        private void ClearFlag()
        {
            if (!_flagged)
                return;

            EntityVisibility visibility = Player.Local?.Visibility;
            visibility?.RemoveState(ContrabandStateLabel, 0f);
            _flagged = false;
        }
    }
}
