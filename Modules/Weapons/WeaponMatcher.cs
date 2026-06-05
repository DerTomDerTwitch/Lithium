using Il2CppScheduleOne.ItemFramework;

namespace Lithium.Modules.Weapons
{
    /// <summary>
    /// Shared "is this item a weapon?" decision used by both module features.
    /// Order: explicit ignore list wins, then explicit weapon-id list, then the CombatUtility match.
    /// </summary>
    public static class WeaponMatcher
    {
        public static bool IsWeapon(ItemDefinition definition)
        {
            if (definition == null)
                return false;

            ModWeapons module = Core.Get<ModWeapons>();
            if (module == null)
                return false;

            ModWeaponsConfiguration config = module.Configuration;
            string id = definition.ID;

            if (config.IgnoredItemIds.Contains(id))
                return false;

            if (config.ExtraWeaponIds.Contains(id))
                return true;

            if (config.MatchCombatItems)
            {
                // CombatUtility ([0..1]) lives on StorableItemDefinition; it is > 0 for weapons.
                StorableItemDefinition storable = definition.TryCast<StorableItemDefinition>();
                if (storable != null && storable.CombatUtility > 0f)
                    return true;
            }

            return false;
        }
    }
}
