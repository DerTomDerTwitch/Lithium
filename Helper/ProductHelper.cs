using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Property;

namespace Lithium.Helper
{
    public static class ProductHelper
    {
        //public static bool PlayerHasSuitableProduct(this Customer customer)
        //{
        //    List<Property> desires = customer.CustomerData.PreferredProperties.ToList();

        //    if (desires.Count == 0)
        //        return true;

        //    int suitableProducts = ProductManager.DiscoveredProducts.ToList()
        //        .Count(pd => GetMatchCount(pd, desires.Select(p => p.Name).ToList()) > 0);
            
        //    return suitableProducts > 0 || desires.Count <= 0;
        //}

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
            List<ProductDefinition> products = dealerItems.Select(d => ProductManager.DiscoveredProducts.ToList().Single(p => p.ID.Equals(d.ID))).Distinct().ToList();
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

        //public static float GetMatchCount(List<Property> source, List<string> desires) => source.Count(p => desires.Contains(p.Name));
    }
}
