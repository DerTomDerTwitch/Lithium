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

    public class ItemListingOverride
    {
        public float Price { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public ShopListing.ERestockRate RestockRate { get; set; } = ShopListing.ERestockRate.Daily;
        public int Stock { get; set; } = -1;
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
