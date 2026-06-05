namespace Lithium.Modules.Storyline
{
    public class ModStorylineConfiguration : ModuleConfiguration
    {
        public override string Name => "Storyline";

        public bool PreventRVExplosion { get; set; } = true;

        // Blocks the player from picking up (and then selling) the RV's original starter furniture
        // (identified by stable per-instance GUID, so bought-and-placed copies stay lootable).
        // Only takes effect while PreventRVExplosion is off. Set false to allow vanilla pickup.
        public bool PreventFurnitureLooting { get; set; } = true;
    }

    public class ModStoryline : ModuleBase<ModStorylineConfiguration>
    {
        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;
        }
    }
}
