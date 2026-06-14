namespace Lithium.Modules.MixingStations
{
    public class ModMixingStationsConfiguration : ModuleConfiguration
    {
        public override string Name => "MixingStation";

        public int InputCapacity { get; set; } = 20;

        // Minutes spent per item in the batch; the game computes total mix time as MixTimePerItem * Quantity
        // (vanilla default 15). Replaces the old MixStepsPerSecond speed multiplier.
        public int MixTimePerItem { get; set; } = 15;

        public int Mk2InputCapacity { get; set; } = 20;
        public int Mk2MixTimePerItem { get; set; } = 15;
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
