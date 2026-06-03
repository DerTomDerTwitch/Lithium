using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Levelling;

namespace Lithium.Modules.Customers.Architecture
{
    /// <summary>
    /// Decides whether a customer will entertain an off-schedule, player-initiated in-person offer.
    /// Order patterns turn customers into periodic bulk buyers; without this gate the player could keep
    /// walking up and selling a fresh batch every day, sidestepping the whole point of the bulk cadence
    /// (one large order that earns a week's affection/XP at once). A customer only considers an unscheduled
    /// offer once they're at least <see cref="DirectSales.MinIntervalFractionBeforeOffer"/> of the way
    /// through the wait for their next scheduled order — i.e. they've had time to run low.
    /// </summary>
    internal static class OfferTimingGate
    {
        // True if the customer will consider an in-person offer right now. Returns true (no gating) when
        // the feature is off, when order patterns aren't reshaping this customer (same XP/enabled gate the
        // order-pattern patches use), or when the game state needed to judge timing is unavailable.
        public static bool AcceptsOfferNow(Customer customer, ModCustomersConfiguration config)
        {
            float threshold = config.DirectSales.MinIntervalFractionBeforeOffer;
            if (threshold <= 0f)
                return true;

            // Only bulk-pattern customers are restricted — mirrors CustomerGetOrderDaysPatch /
            // CustomerContractGenerationPatch so the gate is in effect exactly when orders are bulked.
            if (!config.Contracts.Enabled || !config.OrderPatterns.Enabled ||
                LevelManager.Instance == null || LevelManager.Instance.TotalXP < config.Contracts.XPRequired)
                return true;

            if (customer == null || customer.CustomerData == null || TimeManager.Instance == null)
                return true;

            OrderPatternProfile profile = OrderPatternProfile.Create(
                customer.CustomerData.name,
                customer.CustomerData.MinOrdersPerWeek,
                customer.CustomerData.MaxOrdersPerWeek);

            float elapsed = profile.IntervalFractionElapsed(WeekPosition());
            return elapsed >= threshold;
        }

        // Continuous position within the Mon–Sun week: whole day index + fraction of the day elapsed, so
        // the gate advances smoothly through the day instead of snapping at midnight.
        private static float WeekPosition()
        {
            int day = (int)TimeManager.Instance.CurrentDay;
            int minutesIntoDay = TimeManager.GetMinSumFrom24HourTime(TimeManager.Instance.CurrentTime);
            float frac = minutesIntoDay / 1440f;
            if (frac < 0f) frac = 0f;
            else if (frac > 0.999f) frac = 0.999f;
            return day + frac;
        }
    }
}
