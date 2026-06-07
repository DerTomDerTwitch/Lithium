using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Vision;
using Lithium.Modules.Police.Contraband;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Lithium.Modules.Police
{
    public class ModPoliceConfiguration : ModuleConfiguration
    {
        public override string Name => "Police";

        /// <summary>The "items illegal to carry in hand" feature. See <see cref="ContrabandSettings"/>.</summary>
        public ContrabandSettings Contraband { get; set; } = new();
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

        public override void Apply()
        {
            // (Re)mark the item definitions for the current config; also restores originals on a live reload
            // that turned the feature off.
            ContrabandMarker.ReapplyAll();

            // Scene (re)loaded — the player's visibility state is fresh; re-evaluate from scratch next tick.
            _flagged = false;
        }

        public void DriveUpdate()
        {
            ContrabandSettings contraband = Configuration.Contraband;
            if (!Configuration.Enabled || !contraband.Enabled)
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

        private static bool ShouldFlag(Player local, ContrabandSettings contraband)
        {
            // Holding it inside a vehicle can't be seen, and re-flagging mid-pursuit just adds noise.
            if (local.IsInVehicle)
                return false;
            if (local.CrimeData != null && local.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None)
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
