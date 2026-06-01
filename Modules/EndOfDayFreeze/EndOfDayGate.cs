using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.EndOfDayFreeze
{
    /// <summary>
    /// Shared predicate for the freeze patches. All four station patches return <c>!ShouldFreeze()</c>
    /// from a high-priority prefix, so when the day has ended their per-minute tick (and any other
    /// module's patch on the same method) is skipped entirely.
    /// </summary>
    internal static class EndOfDayGate
    {
        // True only while the module is enabled and the game clock has hit the end-of-day freeze
        // (4 AM). TimeManager.IsEndOfDay flips on exactly when the clock freezes and back off once the
        // player sleeps into a new day, so production resumes automatically the next morning.
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
