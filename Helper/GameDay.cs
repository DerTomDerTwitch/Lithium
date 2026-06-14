using Il2CppScheduleOne.GameTime;

namespace Lithium.Helper
{
    /// <summary>
    /// The "play day" — the day boundary the player actually experiences. The game advances
    /// <see cref="TimeManager.ElapsedDays"/> / <see cref="TimeManager.CurrentDay"/> at <b>midnight</b>, but
    /// time freezes at the 4 AM end-of-day (<see cref="TimeManager.EndOfDay"/> = 400) and only resumes once
    /// the player sleeps — so a new day is only really reached after 4 AM. The 00:00–03:59 window (player
    /// still awake, before sleeping) therefore still belongs to the previous play-day.
    ///
    /// These helpers map the raw game date onto that play-day so day-keyed UI/state — the phone app's Daily
    /// orders tab and its completion/catch-up tracking (<c>DailyOrderTracker</c>) — doesn't flip a day early
    /// at midnight in the middle of a session, only after the 4 AM rollover.
    /// </summary>
    public static class GameDay
    {
        // TimeManager.EndOfDay (4 AM): time freezes here and the play-day turns over, not at midnight.
        // Hard-coded rather than read from the IL2CPP static (it is a const in the game) — the value is fixed.
        private const int RolloverTime = 400;

        // True while the clock is in the post-midnight, pre-4 AM window that still counts as the previous day.
        private static bool BeforeRollover(TimeManager time) => time.CurrentTime < RolloverTime;

        /// <summary>
        /// Absolute play-day count: <see cref="TimeManager.ElapsedDays"/> minus one while before the 4 AM
        /// rollover. A stable per-day key (equality-only use). <see cref="int.MinValue"/> if no TimeManager.
        /// </summary>
        public static int CurrentIndex
        {
            get
            {
                TimeManager time = TimeManager.Instance;
                if (time == null)
                    return int.MinValue;
                return BeforeRollover(time) ? time.ElapsedDays - 1 : time.ElapsedDays;
            }
        }

        /// <summary>
        /// The current play-day as a weekday (<see cref="EDay"/>), rolled back one day while before 4 AM.
        /// Falls back to <c>default</c> when no <see cref="TimeManager"/> exists.
        /// </summary>
        public static EDay CurrentDay
        {
            get
            {
                TimeManager time = TimeManager.Instance;
                if (time == null)
                    return default;
                int index = (int)time.CurrentDay;
                if (BeforeRollover(time))
                    index = (index - 1 + 7) % 7;
                return (EDay)index;
            }
        }
    }
}
