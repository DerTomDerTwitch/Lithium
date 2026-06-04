using UnityEngine;

namespace Lithium.Modules.Customers.Architecture
{
    internal static class OfferAcceptanceWindow
    {
        public const int AbsoluteMaxWindowMinutes = 6 * 1440;

        public static int Extend(int currentWindowMinutes, int quantity, AcceptanceWindow config)
        {
            int extra = quantity > config.BaseQuantity
                ? Mathf.Max(0, Mathf.RoundToInt((quantity - config.BaseQuantity) * config.MinutesPerExtraUnit))
                : 0;

            float multiplier = Mathf.Max(0f, config.DurationMultiplier);
            int scaled = Mathf.RoundToInt((currentWindowMinutes + extra) * multiplier);

            int cap = Mathf.Min(config.MaxWindowMinutes, AbsoluteMaxWindowMinutes);

            scaled = Mathf.Max(scaled, currentWindowMinutes);
            return Mathf.Min(scaled, Mathf.Max(cap, currentWindowMinutes));
        }
    }
}
