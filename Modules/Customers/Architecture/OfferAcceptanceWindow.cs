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
        // Hard safety ceiling on the acceptance window. The game retires offers that roll over a full
        // in-game week, so a window of 7 days (10080 min) lands on that boundary and the deal is lost.
        // Cap at 6 in-game days so even a high configured MaxWindowMinutes can never reach the rollover.
        public const int AbsoluteMaxWindowMinutes = 6 * 1440; // 6 in-game days

        // Returns the acceptance window (in-game minutes) for an order of <paramref name="quantity"/>
        // units, given the game's default window <paramref name="currentWindowMinutes"/>. The result never
        // drops below the game's default and never exceeds the configured cap (itself bounded by the
        // 6-day AbsoluteMaxWindowMinutes ceiling).
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

            // Configured cap, but never wider than the 6-day rollover-safe ceiling.
            int cap = Mathf.Min(config.MaxWindowMinutes, AbsoluteMaxWindowMinutes);

            // Never below what the game already grants; cap, but never below that default either.
            scaled = Mathf.Max(scaled, currentWindowMinutes);
            return Mathf.Min(scaled, Mathf.Max(cap, currentWindowMinutes));
        }
    }
}
