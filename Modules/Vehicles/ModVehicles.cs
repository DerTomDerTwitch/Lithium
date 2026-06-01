using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Vehicles;
using Il2CppScheduleOne.Vehicles.Modification;

namespace Lithium.Modules.Vehicles
{
    public class VehicleConfiguration
    {
        public bool OverrideSlotCount = false;
        public int SlotCount = 0;
        public bool OverridePrice = false;
        public int Price = 0;
        public bool OverrideDefaultColor = false;
        public EVehicleColor Color = EVehicleColor.White;
    }

    public class ModVehiclesConfiguration : ModuleConfiguration
    {
        public override string Name => "Vehicles";

        private string[] supportedVehicleCodes = ["shitbox", "bruiser", "hounddog", "dinkler", "cheetah", "veeper", "hotbox"];
        public Dictionary<string, VehicleConfiguration> Overrides = new() {
            { "shitbox", new() },
            { "bruiser", new() },
            { "hounddog", new() },
            { "dinkler", new() },
            { "cheetah", new() },
            { "veeper", new() },
            { "hotbox", new() }
        };
    }

    public class ModVehicles : ModuleBase<ModVehiclesConfiguration>
    {
        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;

            foreach (KeyValuePair<string, VehicleConfiguration> pair in Configuration.Overrides)
            {
                LandVehicle vehiclePrefab = NetworkSingleton<VehicleManager>.Instance.GetVehiclePrefab(pair.Key);
                if (pair.Value.OverridePrice)
                    vehiclePrefab.vehiclePrice = pair.Value.Price;
                
                if (pair.Value.OverrideSlotCount)
                    vehiclePrefab.Storage.SlotCount = pair.Value.SlotCount;
            }
        }
    }
}