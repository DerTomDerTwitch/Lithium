using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.EndOfDayFreeze
{
    internal static class EndOfDayGate
    {
        public static bool ShouldFreeze()
        {
            ModEndOfDayFreeze module = Core.Get<ModEndOfDayFreeze>();
            if (module == null || !module.Configuration.Enabled)
                return false;

            TimeManager time = TimeManager.Instance;
            return time != null && time.IsEndOfDay;
        }
    }
}
