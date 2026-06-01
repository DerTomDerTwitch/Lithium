using UnityEngine;

namespace Lithium.Modules.Customers.Architecture
{
    /// <summary>
    /// Computes how long the player is given to accept a contract offer, scaling with the order size.
    /// The extra time is added on top of the game's own default window, so small orders behave exactly
    /// as vanilla and only larger orders are extended.
    /// </summary>
    internal static class OfferAcceptanceWindow
    {
        // Returns the acceptance window (in-game minutes) for an order of <paramref name="quantity"/>
        // units, given the game's default window <paramref name="currentWindowMinutes"/>. Orders at or
        // below the configured base quantity are returned unchanged. The result never drops below the
        // game's default and never exceeds the configured cap.
        public static int Extend(int currentWindowMinutes, int quantity, AcceptanceWindow config)
        {
            if (quantity <= config.BaseQuantity)
                return currentWindowMinutes;

            int extra = Mathf.RoundToInt((quantity - config.BaseQuantity) * config.MinutesPerExtraUnit);
            int extended = currentWindowMinutes + Mathf.Max(0, extra);

            // Cap, but never below what the game already grants (in case a default exceeds the cap).
            return Mathf.Min(extended, Mathf.Max(config.MaxWindowMinutes, currentWindowMinutes));
        }
    }
}
