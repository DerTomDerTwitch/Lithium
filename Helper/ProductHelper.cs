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
            List<ProductDefinition> products = dealerItems.Select(d => ProductManager.DiscoveredProducts.ToList().FirstOrDefault(p => p.ID.Equals(d.ID))).Distinct().ToList();
            List<string> dealerProducts = products.Where(p => p != null).SelectMany(p => p.Properties.ToList()).Select(p => p.Name).Distinct().ToList();
            return desires.Intersect(dealerProducts.Distinct().ToList()).Any();
        }

        public static List<string> GetDealerStockedEffects(this Dealer dealer)
        {
            List<ItemInstance> dealerItems = dealer.Inventory.ItemSlots.ToList()
                .Where(i => i.ItemInstance != null).Select(i => i.ItemInstance).ToList();
            List<ProductDefinition> products = dealerItems
                .Select(d => ProductManager.DiscoveredProducts.ToList().FirstOrDefault(p => p.ID.Equals(d.ID)))
                .Where(p => p != null).Distinct().ToList();
            return products.SelectMany(p => p.Properties.ToList()).Select(p => p.Name).Distinct().ToList();
        }

        public static bool IsServeable(this Customer customer) =>
            customer != null && customer.CustomerData != null && customer.NPC != null;

        public static List<string> GetDesireNames(CustomerData customerData, bool toLower = false)
        {
            IEnumerable<string> names = customerData.PreferredProperties.ToList().Select(p => p.Name);
            if (toLower)
                names = names.Select(n => n.ToLowerInvariant());
            return names.ToList();
        }

        public static int GetTotalQuantity(ProductList products) => products?.GetTotalQuantity() ?? 0;

        public static bool ProductMatchesDesires(ProductDefinition pd, List<string> desires) => pd.Properties.ToList().Select(p => p.Name).Intersect(desires).Any();

        public static int CoveredEffectCount(ProductDefinition pd, List<string> desires) =>
            pd.Properties.ToList().Select(p => p.Name).Intersect(desires).Count();

        // The customer's affinity (roughly [-1, 1]; positive = liked, negative = disliked) for the
        // product's drug type. Returns 0 (neutral) if the customer has no entry for that drug type.
        public static float DrugTypeAffinity(ProductDefinition pd, CustomerData customerData)
        {
            if (pd == null || customerData == null)
                return 0f;
            CustomerAffinityData affinities = customerData.DefaultAffinityData;
            if (affinities == null)
                return 0f;
            foreach (ProductTypeAffinity affinity in affinities.ProductAffinities)
            {
                if (affinity.DrugType == pd.DrugType)
                    return affinity.Affinity;
            }
            return 0f;
        }

        public static string FormatDesires(CustomerData customerData) => customerData.PreferredProperties.ToList().Select(p => p.Name).SmartJoin(", ", " or ");

        public static int GetMatchCount(ProductDefinition pd, List<string> desires)
        {
            return ProductManager.DiscoveredProducts.ToList()
                .Count(p => p.Properties.ToList().Select(p => p.Name).Intersect(desires).Any());
        }
    }
}
