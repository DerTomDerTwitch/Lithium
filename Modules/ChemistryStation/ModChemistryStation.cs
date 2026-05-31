namespace Lithium.Modules.ChemistryStation
{
    public class ModChemistryStationConfiguration : ModuleConfiguration
    {
        public override string Name => "ChemistryStation";

        /// <summary>
        /// Cook speed multiplier. 1 = vanilla, &gt;1 speeds the cook up, &lt;1 slows it down, 0 pauses it.
        /// </summary>
        public float Speed = 1f;
    }
    public class ModChemistryStation : ModuleBase<ModChemistryStationConfiguration>
    {
        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;
        }
    }
}
