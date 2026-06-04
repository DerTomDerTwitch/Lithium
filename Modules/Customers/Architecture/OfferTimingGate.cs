using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Levelling;

namespace Lithium.Modules.Customers.Architecture
{
    internal static class OfferTimingGate
    {
        public static bool AcceptsOfferNow(Customer customer, ModCustomersConfiguration config)
        {
            float threshold = config.DirectSales.MinIntervalFractionBeforeOffer;
            if (threshold <= 0f)
                return true;

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
