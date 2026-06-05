using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.PlayerScripts;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Lithium.Modules.Warehouse
{
    public class ModWarehouseConfiguration : ModuleConfiguration
    {
        public override string Name => "Warehouse";

        /// <summary>
        /// The rank the player must reach before the warehouse (Dark Market) stays open 24/7.
        /// Below this rank the vanilla opening hours apply. Combined with <see cref="RequiredRankTier"/>
        /// (e.g. Hoodlum + tier 2 = "Hoodlum II").
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ERank RequiredRank = ERank.Hoodlum;

        /// <summary>
        /// The tier (I–V, i.e. 1–5) within <see cref="RequiredRank"/> that must be reached.
        /// </summary>
        public int RequiredRankTier = 1;

        /// <summary>
        /// When true, the warehouse still closes while any player is being pursued by police
        /// (vanilla behaviour). When false, it stays open even during a pursuit once the rank
        /// requirement is met.
        /// </summary>
        public bool CloseDuringPursuit = true;

        public override void Validate()
        {
            if (RequiredRankTier < 1)
                RequiredRankTier = 1;
            if (RequiredRankTier > 5)
                RequiredRankTier = 5;
        }
    }

    /// <summary>
    /// Keeps the warehouse (the Dark Market black market run by Oscar/Igor) open around the clock
    /// once the player reaches a configured rank, removing its vanilla after-hours-only restriction.
    /// <para>
    /// The market's open state is governed by two time checks: <c>DarkMarket.ShouldBeOpen()</c>
    /// (drives <c>IsOpen</c> — i.e. whether the vendor/deliveries are active) and
    /// <c>DarkMarketAccessZone.GetIsOpen()</c> (drives the door locks). Both are patched (see
    /// Patches/) to bypass the time-of-day check once <see cref="RequirementMet"/> is true, while
    /// preserving the unlock requirement and (optionally) the police-pursuit lockout.
    /// </para>
    /// </summary>
    public class ModWarehouse : ModuleBase<ModWarehouseConfiguration>
    {
        public override void Apply()
        {
        }

        /// <summary>
        /// True when the local player's rank is at or above the configured requirement.
        /// Uses <see cref="FullRank.ToFloat"/> for comparison (matching the codebase convention)
        /// to avoid constructing an IL2CPP <see cref="FullRank"/> struct just to compare it.
        /// </summary>
        public bool RequirementMet()
        {
            LevelManager lvl = LevelManager.Instance;
            if (lvl == null)
                return false;

            // FullRank.ToFloat() == (float)Rank + Tier / 5f.
            float requiredFloat = (float)Configuration.RequiredRank + Configuration.RequiredRankTier / 5f;
            return lvl.GetFullRank().ToFloat() >= requiredFloat;
        }

        /// <summary>
        /// True when any player currently has an active police pursuit. Mirrors the loop in the
        /// game's <c>DarkMarket.ShouldBeOpen()</c>.
        /// </summary>
        public static bool AnyPlayerPursued()
        {
            for (int i = 0; i < Player.PlayerList.Count; i++)
            {
                Player player = Player.PlayerList[i];
                if (player != null && player.CrimeData != null &&
                    player.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None)
                    return true;
            }

            return false;
        }
    }
}
