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

        // Distinct effect names across every product currently in the dealer's inventory. Mirrors the
        // product->effect resolution in DealerHasSuitableProduct (FirstOrDefault tolerates undiscovered items).
        public static List<string> GetDealerStockedEffects(this Dealer dealer)
        {
            List<ItemInstance> dealerItems = dealer.Inventory.ItemSlots.ToList()
                .Where(i => i.ItemInstance != null).Select(i => i.ItemInstance).ToList();
            List<ProductDefinition> products = dealerItems
                .Select(d => ProductManager.DiscoveredProducts.ToList().FirstOrDefault(p => p.ID.Equals(d.ID)))
                .Where(p => p != null).Distinct().ToList();
            return products.SelectMany(p => p.Properties.ToList()).Select(p => p.Name).Distinct().ToList();
        }

        // A customer we can reason about: present, with customer data and a backing NPC. Replaces the
        // repeated `c != null && c.CustomerData != null && c.NPC != null` guard in the coverage notifiers.
        public static bool IsServeable(this Customer customer) =>
            customer != null && customer.CustomerData != null && customer.NPC != null;

        // The customer's desired-effect names. Pass toLower: true when comparing against already
        // lower-cased effect names (e.g. EffectCoverageBonus). Replaces the repeated
        // PreferredProperties.ToList().Select(p => p.Name).ToList() chain across the module.
        public static List<string> GetDesireNames(CustomerData customerData, bool toLower = false)
        {
            IEnumerable<string> names = customerData.PreferredProperties.ToList().Select(p => p.Name);
            if (toLower)
                names = names.Select(n => n.ToLowerInvariant());
            return names.ToList();
        }

        // Total units across a contract's product list, via the game's own ProductList.GetTotalQuantity().
        // Null-safe (returns 0), replacing the repeated `Products.entries.ToList().Sum(e => e.Quantity)`.
        public static int GetTotalQuantity(ProductList products) => products?.GetTotalQuantity() ?? 0;

        public static bool ProductMatchesDesires(ProductDefinition pd, List<string> desires) => pd.Properties.ToList().Select(p => p.Name).Intersect(desires).Any();

        // How many of the customer's desired effects this product carries (0 = covers none).
        public static int CoveredEffectCount(ProductDefinition pd, List<string> desires) =>
            pd.Properties.ToList().Select(p => p.Name).Intersect(desires).Count();

        public static string FormatDesires(CustomerData customerData) => customerData.PreferredProperties.ToList().Select(p => p.Name).SmartJoin(", ", " or ");

        public static int GetMatchCount(ProductDefinition pd, List<string> desires)
        {
            return ProductManager.DiscoveredProducts.ToList()
                .Count(p => p.Properties.ToList().Select(p => p.Name).Intersect(desires).Any());
        }
    }
}
