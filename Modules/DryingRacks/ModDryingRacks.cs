using Il2CppScheduleOne.ItemFramework;

namespace Lithium.Modules.DryingRacks
{
    public class ModDryingRacksConfiguration : ModuleConfiguration
    {
        public override string Name => "DryingRacks";
        public int Capacity { get; set; } = 20;
        // Minutes spent at each starting tier before the operation advances to the next quality.
        // Graduated so reaching higher tiers takes progressively longer (Heavenly is the cap).
        public Dictionary<string, int> PerQualityDryTimes = new()
        {
            { nameof(EQuality.Trash), 240 },
            { nameof(EQuality.Poor), 360 },
            { nameof(EQuality.Standard), 480 },
            { nameof(EQuality.Premium), 600 },
            { nameof(EQuality.Heavenly), 720 },
        };
    }

    public class ModDryingRacks : ModuleBase<ModDryingRacksConfiguration>
    {
        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;
        }
    }
}
