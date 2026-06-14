namespace Lithium.Modules.LabOven
{
    public class ModLabOvenConfiguration : ModuleConfiguration
    {
        public override string Name => "LabOven";

        /// <summary>
        /// Total in-game minutes a full cook should take when the module is enabled, regardless of the
        /// ingredient's vanilla cook time. Replaces the old <c>Speed</c> multiplier.
        /// </summary>
        public float CookDurationMinutes = 60f;
    }
    public class ModLabOven : ModuleBase<ModLabOvenConfiguration>
    {
        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;
        }
    }
}
