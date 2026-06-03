using Il2CppScheduleOne.ItemFramework;
using Lithium.Helper;

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
            { nameof(EQuality.Standard), 720 },
            { nameof(EQuality.Premium), 800 },
            { nameof(EQuality.Heavenly), 1200 },
        };

        public override void Validate()
        {
            Capacity = ConfigValidator.AtLeast(Name, nameof(Capacity), Capacity, 1);
            foreach (string tier in PerQualityDryTimes.Keys.ToList())
                PerQualityDryTimes[tier] = ConfigValidator.AtLeast(Name, $"{nameof(PerQualityDryTimes)}[{tier}]", PerQualityDryTimes[tier], 0);
        }
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
