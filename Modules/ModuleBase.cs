namespace Lithium.Modules
{
    public abstract class ModuleBase
    {
        public abstract void Load();
        public abstract void Apply();
    }

    public abstract class ModuleBase<TConfiguration> : ModuleBase
        where TConfiguration : ModuleConfiguration
    {
        public TConfiguration Configuration { get; private set; }
        protected ModuleBase()
        {

        }

        protected virtual void OnBeforeConfigurationLoaded() {}

        public override void Load()
        {
            Configuration = Activator.CreateInstance<TConfiguration>();
            OnBeforeConfigurationLoaded();
            Configuration.LoadConfiguration();
            Configuration.Validate();
        }
    }
}
