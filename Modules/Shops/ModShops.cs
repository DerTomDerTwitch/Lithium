using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Phone.Delivery;
using Il2CppScheduleOne.UI.Shop;
using Lithium.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Lithium.Modules.Shops
{
    public class SupplierListingOverride
    {
        public Dictionary<string, float> PriceOverrides { get; set; } = [];
    }

    // Shared rank-requirement knobs. Rank gating lives on the global item definition
    // (StorableItemDefinition.RequiresLevelToPurchase / RequiredRank), so it is applied by item ID via the
    // Registry — independent of whether the selling shop's GameObject is discovered.
    //   RequiresRank null  → leave the item's vanilla rank gating untouched (default; also what existing
    //                        configs deserialize to, so upgrading never silently strips requirements).
    //   RequiresRank false → lift the requirement entirely (item purchasable from the start).
    //   RequiresRank true  → gate the item behind RequiredRank + RequiredRankTier.
    public interface IRankRequirement
    {
        bool? RequiresRank { get; set; }
        ERank RequiredRank { get; set; }
        int RequiredRankTier { get; set; }
    }

    public class ItemListingOverride : IRankRequirement
    {
        // Overridden purchase price. Negative = leave the item's native price untouched (handy for
        // rank-only overrides where you don't want to change the price).
        public float Price { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public ShopListing.ERestockRate RestockRate { get; set; } = ShopListing.ERestockRate.Daily;
        public int Stock { get; set; } = -1;

        // Optional rank-requirement override for the underlying item (e.g. the brick press). See
        // IRankRequirement. New entries are auto-populated with the item's live values.
        public bool? RequiresRank { get; set; } = null;
        [JsonConverter(typeof(StringEnumConverter))]
        public ERank RequiredRank { get; set; } = ERank.Street_Rat;
        public int RequiredRankTier { get; set; } = 0;
    }

    // A brand-new listing to inject into a shop that doesn't sell the item natively (e.g. putting the
    // brick press on Oscar's Dark Market). Keyed by item ID in ShopListingSettings.AddedItems.
    public class AddedListing : IRankRequirement
    {
        // Purchase price. Negative = use the item's own base purchase price (the usual choice).
        public float Price { get; set; } = -1f;
        [JsonConverter(typeof(StringEnumConverter))]
        public ShopListing.ERestockRate RestockRate { get; set; } = ShopListing.ERestockRate.Daily;
        public int Stock { get; set; } = -1;

        // Optional rank gating for the added item, same semantics as ItemListingOverride.RequiresRank:
        // null = leave the item's vanilla requirement, false = no requirement, true = gate behind the rank.
        public bool? RequiresRank { get; set; } = null;
        [JsonConverter(typeof(StringEnumConverter))]
        public ERank RequiredRank { get; set; } = ERank.Street_Rat;
        public int RequiredRankTier { get; set; } = 0;
    }

    public class ShopListingSettings
    {
        [JsonProperty(Order = 1)]
        public bool Override { get; set; } = false;
        [JsonProperty(Order = 2)]
        public int DefaultStock { get; set; } = -1;

        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty(Order = 3)]
        public ShopInterface.EPaymentType PaymentType { get; set; }

        [JsonProperty(Order = 6)]
        public Dictionary<string, ItemListingOverride> ItemOverrides = [];

        // Items the shop does NOT normally sell, injected as new listings. Requires Override = true.
        [JsonProperty(Order = 7)]
        public Dictionary<string, AddedListing> AddedItems = [];
    }

    public enum DeliveryAvailabilitySettings { Unchanged, Never, Always, AfterReachingXP }

    public class DeliverySettings
    {
        [JsonConverter(typeof(StringEnumConverter))] 
        public DeliveryAvailabilitySettings Availability { get; set; } = DeliveryAvailabilitySettings.Unchanged;
        public float DeliveryFee { get; set; } = 200;
        public int XPRequirement { get; set; } = 0;
    }

    public class ModShopsConfiguration : ModuleConfiguration
    {
        public override string Name => "Shops";

        public ShopListingSettings ThriftyThreads = new();
        public ShopListingSettings CokeSupplier = new();
        public ShopListingSettings MethSupplier = new();
        public ShopListingSettings WeedSupplier = new();
        public ShopListingSettings ShroomSupplier = new();
        public ShopListingSettings Boutique = new();
        public ShopListingSettings DarkMarket = new();
        public ShopListingSettings GasStation = new();
        public ShopListingSettings CentralGasStation = new();
        public ShopListingSettings DansHardware = new();
        public ShopListingSettings HandyHanks = new();

        public SupplierListingOverride Albert = new();
        public SupplierListingOverride Shirley = new();
        public SupplierListingOverride Salvador = new();
        public SupplierListingOverride Phil = new();

        public Dictionary<string, DeliverySettings> Deliveries { get; set; } = new();
    }

    public class ModShops : ModuleBase<ModShopsConfiguration>
    {
        public override void Apply()
        {
        }

        
    }
}
