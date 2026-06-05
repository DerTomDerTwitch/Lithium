using Il2CppScheduleOne;
using Il2CppScheduleOne.ItemFramework;

namespace Lithium.Modules.Weapons
{
    /// <summary>
    /// Applies the "weapons worth $0 at the pawn shop" lever by overwriting the shared
    /// <see cref="StorableItemDefinition.ResellMultiplier"/> on weapon definitions (the pawn shop
    /// values items as <c>BasePurchasePrice * ResellMultiplier * Quantity</c>). Originals are
    /// captured per item ID so a live config reload can restore vanilla values when the lever is
    /// turned off. This mirrors the StackSizes module's approach of editing item definitions.
    /// </summary>
    public static class WeaponPawnValue
    {
        // Captured original ResellMultiplier per item ID, so toggling the feature off restores it.
        private static readonly Dictionary<string, float> OriginalResell = new();

        // Captured from the Registry.Start patch; reused on live config reloads.
        public static Registry RegistryInstance;

        public static void ReapplyAll()
        {
            if (RegistryInstance == null)
                return;

            ModWeapons module = Core.Get<ModWeapons>();
            if (module == null)
                return;

            ModWeaponsConfiguration config = module.Configuration;
            bool zeroing = config.Enabled && config.ZeroPawnValue;
            int affected = 0;

            foreach (Registry.ItemRegister register in RegistryInstance.ItemRegistry)
            {
                StorableItemDefinition definition = register.Definition.TryCast<StorableItemDefinition>();
                if (definition == null)
                    continue;

                string id = definition.ID;

                // Capture the vanilla value the first time we touch this definition.
                if (!OriginalResell.ContainsKey(id))
                    OriginalResell[id] = definition.ResellMultiplier;

                bool target = zeroing && WeaponMatcher.IsWeapon(definition);
                float value = target ? 0f : OriginalResell[id];

                if (definition.ResellMultiplier != value)
                    definition.ResellMultiplier = value;

                if (target)
                    affected++;
            }

            if (zeroing)
                Log.Info($"[Weapons] Zeroed pawn value on {affected} weapon definition(s).");
        }
    }
}
