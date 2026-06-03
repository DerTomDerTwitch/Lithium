namespace Lithium.Modules.BrickPress
{
    public class ModBrickPressConfiguration : ModuleConfiguration
    {
        public override string Name => "BrickPress";

        /// <summary>
        /// When true, clicking the brick press "Begin" button produces the brick immediately and skips
        /// the interactive pour/press minigame. The output is identical to finishing the minigame by
        /// hand — only the manual pouring and lever-pulling steps are removed.
        /// </summary>
        public bool InstantPress = true;
    }

    public class ModBrickPress : ModuleBase<ModBrickPressConfiguration>
    {
        public override void Apply()
        {
        }
    }
}
