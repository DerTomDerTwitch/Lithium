using Il2CppScheduleOne.ItemFramework;
using MelonLoader;

namespace Lithium.Modules.StackSizes
{
    public static class ItemRegistry
    {
        public static List<ItemDefinition> AllItemDefinitions = [];

        public static void UpdateEntireRegistry()
        {
            foreach (ItemDefinition itemDefinition in AllItemDefinitions)
                UpdateItemDefinition(itemDefinition);
        }

        public static void UpdateItemDefinition(ItemDefinition itemDefinition)
        {
            ModStackSizesConfiguration config = Core.Get<ModStackSizes>().Configuration;
            if (!config.Enabled)
                return;

            if (config.IgnoredItems.Contains(itemDefinition.ID))
                return;

            if (config.ItemOverrides.TryGetValue(itemDefinition.ID, out int overrideSize))
            {
                itemDefinition.StackLimit = overrideSize;
                return;
            }

            if (config.CategorySizes.TryGetValue(itemDefinition.Category, out overrideSize))
            {
                itemDefinition.StackLimit = overrideSize;
            }
            else
            {
                Log.Warning($"Found item for category {itemDefinition.Category}, but no category value");
            }
        }
    }
}
