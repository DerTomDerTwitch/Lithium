using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Money;
using Il2CppTMPro;
using Property = Il2CppScheduleOne.Property.Property;

namespace Lithium.Modules.PropertyPrices
{
    public class ModPropertyPricesConfiguration : ModuleConfiguration
    {
        public override string Name => "Property Prices";
        public Dictionary<string, int> PropertyPrices { get; set; } = new Dictionary<string, int>
        {
            { "RV", 0 },
            { "Sewer Office", 0 },
            { "Motel Room", 75 },
            { "Sweatshop", 800 },
            { "Storage Unit", 5000 },
            { "Bungalow", 6000 },
            { "Barn", 25000 },
            { "Docks Warehouse", 50000 },
            { "Hyland Manor", 100000 },

            { "Laundromat", 4000 },
            { "Post Office", 10000 },
            { "Car Wash", 20000 },
            { "Taco Ticklers", 50000 },
        };
    }

    public class ModPropertyPrices : ModuleBase<ModPropertyPricesConfiguration>
    {
        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;
        }
    }
}
