using Il2CppScheduleOne.Levelling;

namespace Lithium.Helper
{
    /// <summary>
    /// Central helper for player-rank gating. Modules express a requirement as an
    /// <see cref="ERank"/> + tier (I–V) pair; this collapses that to the same
    /// <see cref="FullRank.ToFloat"/> comparison used throughout the codebase, in one place.
    /// </summary>
    public static class RankHelper
    {
        /// <summary>
        /// The <see cref="FullRank.ToFloat"/> value for a (rank, tier) requirement.
        /// Tier is clamped to the valid 1–5 range. <c>ToFloat() == (float)Rank + Tier / 5f</c>.
        /// </summary>
        public static float ToFloat(ERank rank, int tier) =>
            (float)rank + Math.Clamp(tier, 1, 5) / 5f;

        /// <summary>
        /// The local player's current rank as a <see cref="FullRank.ToFloat"/> value, or
        /// <paramref name="fallback"/> when the level manager is unavailable.
        /// </summary>
        public static float PlayerRankFloat(float fallback)
        {
            LevelManager lvl = LevelManager.Instance;
            return lvl == null ? fallback : lvl.GetFullRank().ToFloat();
        }

        /// <summary>
        /// True when the local player's rank is at or above the configured
        /// <paramref name="rank"/> / <paramref name="tier"/>. When the level manager is
        /// unavailable, returns <paramref name="defaultWhenUnavailable"/> (most gates default this
        /// to <c>true</c> so the override applies; opening-hour style gates pass <c>false</c>).
        /// </summary>
        public static bool PlayerRankAtLeast(ERank rank, int tier, bool defaultWhenUnavailable = true)
        {
            LevelManager lvl = LevelManager.Instance;
            if (lvl == null)
                return defaultWhenUnavailable;

            return lvl.GetFullRank().ToFloat() >= ToFloat(rank, tier);
        }
    }
}
