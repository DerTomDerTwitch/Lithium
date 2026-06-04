namespace Lithium.Modules.BrickPress
{
    public class ModBrickPressConfiguration : ModuleConfiguration
    {
        public override string Name => "BrickPress";

        public bool InstantPress = true;
    }

    public class ModBrickPress : ModuleBase<ModBrickPressConfiguration>
    {
        public override void Apply()
        {
        }
    }
}
