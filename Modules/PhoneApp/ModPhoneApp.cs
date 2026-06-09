using Lithium.Helper;
using MelonLoader;

namespace Lithium.Modules.PhoneApp
{
    public class ModPhoneAppConfiguration : ModuleConfiguration
    {
        public override string Name => "PhoneApp";

        public ModPhoneAppConfiguration()
        {
            // Read-only informational app; on by default like the other UI-only modules.
            Enabled = true;
        }
    }

    // Adds a custom "Lithium" app to the in-game smartphone with tabbed pages: "Property" (rent +
    // electric-bill status per property) and "Daily" (today's ordering customers, see DailyOrdersPage).
    // The app UI is built from code (no AssetBundle) and its lifecycle is driven manually:
    // Apply() (re)creates it on each save load; DriveUpdate() is forwarded from Core.OnUpdate.
    public class ModPhoneApp : ModuleBase<ModPhoneAppConfiguration>
    {
        private LithiumPhoneApp _app;

        public override void Apply()
        {
            if (!Configuration.Enabled)
                return;

            // A config reload (Ctrl+Shift+F8) re-runs Apply() while still in-game. The existing app's
            // GameObjects are still alive then, so don't build a duplicate. A real scene reload destroys
            // them, so IsAlive goes false and we rebuild.
            if (_app != null && _app.IsAlive)
                return;

            _app = new LithiumPhoneApp();
            MelonCoroutines.Start(_app.BuildWhenReady());
        }

        public void DriveUpdate()
        {
            if (Configuration.Enabled)
                _app?.Update();
        }
    }
}
