namespace Lithium.Modules.MixingStations
{
    public class ModMixingStationsConfiguration : ModuleConfiguration
    {
        public override string Name => "MixingStation";

        // Standard Mixing Station
        public int InputCapacity { get; set; } = 20;
        public int MixStepsPerSecond { get; set; } = 1;

        // Mixing Station MK II
        public int Mk2InputCapacity { get; set; } = 20;
        public int Mk2MixStepsPerSecond { get; set; } = 1;
    }

    public class ModMixingStations : ModuleBase<ModMixingStationsConfiguration>
    {
        public override void Apply()
        {
            if(!Configuration.Enabled)
                return;
        }
    }
}
