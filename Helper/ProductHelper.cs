using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Property;

namespace Lithium.Helper
{
    public static class ProductHelper
    {
        public static bool DealerHasSuitableProduct(this Customer customer, out List<ItemInstance> dealerItems)
        {
            List<string> desires = customer.CustomerData.PreferredProperties
                .ToList()
                .Select(p => p.Name)
                .ToList();

            if (desires.Count == 0)
            {
                dealerItems = [];
                return true;
            }

            dealerItems = customer.AssignedDealer.Inventory.ItemSlots.ToList().Where(i => i.ItemInstance != null).Select(i => i.ItemInstance).ToList();
            // FirstOrDefault, not Single: a dealer can hold items whose product isn't in
            // DiscoveredProducts (throws "Sequence contains no matching element"); nulls are filtered next.
            List<ProductDefinition> products = dealerItems.Select(d => ProductManager.DiscoveredProducts.ToList().FirstOrDefault(p => p.ID.Equals(d.ID))).Distinct().ToList();
            List<string> dealerProducts = products.Where(p => p != null).SelectMany(p => p.Properties.ToList()).Select(p => p.Name).Distinct().ToList();
            return desires.Intersect(dealerProducts.Distinct().ToList()).Any();
        }

        public static bool ProductMatchesDesires(ProductDefinition pd, List<string> desires) => pd.Properties.ToList().Select(p => p.Name).Intersect(desires).Any();

        public static string FormatDesires(CustomerData customerData) => customerData.PreferredProperties.ToList().Select(p => p.Name).SmartJoin(", ", " or ");

        public static int GetMatchCount(ProductDefinition pd, List<string> desires)
        {
            return ProductManager.DiscoveredProducts.ToList()
                .Count(p => p.Properties.ToList().Select(p => p.Name).Intersect(desires).Any());
        }
    }
}
