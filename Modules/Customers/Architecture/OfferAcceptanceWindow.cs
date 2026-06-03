using UnityEngine;

namespace Lithium.Modules.Customers.Architecture
{
    /// <summary>
    /// Computes how long the player is given to accept a contract offer. A per-unit bonus is added on top
    /// of the game's own default window for larger orders, then the whole window is scaled by the
    /// configured DurationMultiplier (e.g. +50%) so every offer gets more answer time and bulk orders
    /// comfortably span multiple in-game days.
    /// </summary>
    internal static class OfferAcceptanceWindow
    {
        // Returns the acceptance window (in-game minutes) for an order of <paramref name="quantity"/>
        // units, given the game's default window <paramref name="currentWindowMinutes"/>. The result never
        // drops below the game's default and never exceeds the configured cap.
        public static int Extend(int currentWindowMinutes, int quantity, AcceptanceWindow config)
        {
            // Per-unit bonus for orders above the base quantity (no size bonus at or below it).
            int extra = quantity > config.BaseQuantity
                ? Mathf.Max(0, Mathf.RoundToInt((quantity - config.BaseQuantity) * config.MinutesPerExtraUnit))
                : 0;

            // Scale the whole window (game default + size bonus) by the global multiplier so every offer —
            // not just large ones — gets the longer answer time.
            float multiplier = Mathf.Max(0f, config.DurationMultiplier);
            int scaled = Mathf.RoundToInt((currentWindowMinutes + extra) * multiplier);

            // Never below what the game already grants; cap, but never below that default either.
            scaled = Mathf.Max(scaled, currentWindowMinutes);
            return Mathf.Min(scaled, Mathf.Max(config.MaxWindowMinutes, currentWindowMinutes));
        }
    }
}
