namespace Lithium.Modules.ChemistryStation
{
    public class ModChemistryStationConfiguration : ModuleConfiguration
    {
        public override string Name => "ChemistryStation";

        /// <summary>
        /// Total in-game minutes a full cook should take when the module is enabled, regardless of the
        /// recipe's vanilla cook time. Replaces the old <c>Speed</c> multiplier.
        /// </summary>
        public float CookDurationMinutes = 60f;
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
