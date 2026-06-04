namespace Lithium.Modules.EndOfDayFreeze
{
    public class ModEndOfDayFreezeConfiguration : ModuleConfiguration
    {
        public override string Name => "EndOfDayFreeze";

        public ModEndOfDayFreezeConfiguration()
        {
            Enabled = true;
        }
    }

    public class ModEndOfDayFreeze : ModuleBase<ModEndOfDayFreezeConfiguration>
    {
        public override void Apply()
        {
        }
    }
}
