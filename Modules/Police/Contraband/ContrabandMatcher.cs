using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;

namespace Lithium.Modules.Police.Contraband
{
    /// <summary>
    /// Decides whether an item definition should be contraband and, if so, at what <see cref="ELegalStatus"/>.
    /// Order: ignore list wins, then the explicit per-ID list, then "all seeds", then "all products".
    /// Returns <c>null</c> when the item should keep its vanilla legal status.
    /// </summary>
    public static class ContrabandMatcher
    {
        public static ELegalStatus? GetSeverity(ItemDefinition definition)
        {
            if (definition == null)
                return null;

            ModPolice module = Core.Get<ModPolice>();
            if (module == null)
                return null;

            ContrabandSettings config = module.Configuration.Contraband;
            string id = definition.ID;

            if (config.IgnoredItemIds.Contains(id))
                return null;

            if (config.IllegalItems.TryGetValue(id, out ELegalStatus explicitSeverity))
                return explicitSeverity;

            if (config.MarkAllSeeds && definition.TryCast<SeedDefinition>() != null)
                return config.SeedSeverity;

            // ShroomDefinition derives from ProductDefinition, so this also covers mushrooms.
            if (config.MarkAllProducts && definition.TryCast<ProductDefinition>() != null)
                return config.ProductSeverity;

            return null;
        }
    }
}
