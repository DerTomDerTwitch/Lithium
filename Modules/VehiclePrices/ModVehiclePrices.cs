namespace Lithium.Modules.VehiclePrices
{
    public class ModVehiclePricesConfiguration : ModuleConfiguration
    {
        public override string Name => "Vehicle Prices";
        public Dictionary<string, int> VehiclePrices { get; set; }
    }

    public class ModVehiclePrices : ModuleBase<ModVehiclePricesConfiguration>
    {
        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;
        }
    }
}
