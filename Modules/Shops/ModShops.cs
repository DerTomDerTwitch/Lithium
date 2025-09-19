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

    public class SkateboardShopSettings
    {
        public string UIContainer;
        public Dictionary<string, SkateboardListingOverride> SkateboardOverrides = [];
    }

    public class SkateboardListingOverride
    {
        public string UIPath;
        public float Price;
        public bool IsAvailable { get; set; } = true;
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

        public ShopListingSettings ThriftyThreads;
        public ShopListingSettings CokeSupplier;
        public ShopListingSettings MethSupplier;
        public ShopListingSettings WeedSupplier;
        public ShopListingSettings Boutique;
        public ShopListingSettings DarkMarket;
        public ShopListingSettings GasStation;
        public ShopListingSettings CentralGasStation;
        public ShopListingSettings DansHardware;
        public ShopListingSettings HandyHanks;

        public SupplierListingOverride Albert;
        public SupplierListingOverride Shirley;
        public SupplierListingOverride Salvador;

        public Dictionary<string, DeliverySettings> Deliveries { get; set; }

        public Dictionary<string, SkateboardShopSettings> SkateboardShops { get; set; }
    }

    public class ModShops : ModuleBase<ModShopsConfiguration>
    {
        public override void Apply()
        {
        }


    }
}
