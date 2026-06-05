namespace Lithium.Modules.Weapons
{
    public class ModWeaponsConfiguration : ModuleConfiguration
    {
        public override string Name => "Weapons";

        /// <summary>
        /// Block weapons from being looted off police officers (pickpocket / "view inventory"
        /// of an unconscious officer). The weapon stays on the officer, locked and un-takeable.
        /// Robust and save-safe; bought weapons are completely unaffected. Defaults on so that
        /// enabling the module fixes the police-loot money exploit out of the box.
        /// </summary>
        public bool BlockPoliceLooting { get; set; } = true;

        /// <summary>
        /// Make weapons worth $0 at the pawn shop by zeroing their <c>ResellMultiplier</c>.
        /// This is unconditional — it cannot tell a looted weapon from a bought one (they are the
        /// same item), so it affects ALL weapons. Off by default; turn on if you also want bought
        /// weapons to be unsellable. (Also zeroes the weapon's monetary value used by the pickpocket
        /// minigame, a minor side effect.)
        /// </summary>
        public bool ZeroPawnValue { get; set; } = false;

        /// <summary>
        /// Treat any item with <c>CombatUtility &gt; 0</c> as a weapon. This auto-covers every
        /// current and future weapon without hardcoding IDs.
        /// </summary>
        public bool MatchCombatItems { get; set; } = true;

        /// <summary>Extra item IDs to always treat as a weapon (in addition to the CombatUtility match).</summary>
        public List<string> ExtraWeaponIds { get; set; } = [];

        /// <summary>Item IDs to never treat as a weapon (escape hatch / overrides the above).</summary>
        public List<string> IgnoredItemIds { get; set; } = [];
    }

    /// <summary>
    /// Rebalances weapons as a money source. Schedule I lets you loot weapons off downed/searched
    /// police officers and pawn them for large sums, which imbalances the early economy.
    ///
    /// A looted weapon and a bought weapon are the exact same <c>ItemInstance</c> (only ID + quantity
    /// persist), so there is no save-safe way to make <em>only</em> looted ones worthless at the pawn
    /// shop. This module therefore offers two independent, config-toggled levers:
    ///   * <see cref="ModWeaponsConfiguration.BlockPoliceLooting"/> — stop the weapon reaching the
    ///     player at all (police-only, fully save-safe, leaves bought weapons sellable).
    ///   * <see cref="ModWeaponsConfiguration.ZeroPawnValue"/> — make all weapons worth $0 to resell.
    ///
    /// Weapon detection is shared via <see cref="WeaponMatcher"/>. The pawn-value change is applied to
    /// the (shared) item definitions, captured/restored so toggling it off via a live config reload
    /// restores vanilla resale values.
    /// </summary>
    public class ModWeapons : ModuleBase<ModWeaponsConfiguration>
    {
        public override void Apply()
        {
            // Reapply the pawn-value override against the current config (also handles the
            // Ctrl+Shift+F8 live reload, restoring originals when ZeroPawnValue is turned off).
            WeaponPawnValue.ReapplyAll();
        }
    }
}
