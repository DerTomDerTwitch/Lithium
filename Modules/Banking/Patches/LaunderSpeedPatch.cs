using HarmonyLib;
using Il2CppScheduleOne.Property;
using UnityEngine;

namespace Lithium.Modules.Banking.Patches
{
    /// <summary>
    /// Controls laundering speed. Each laundering job is a <see cref="LaunderingOperation"/> whose
    /// <c>completionTime_Minutes</c> (set in its constructor) is how long it takes to finish. Patching the
    /// constructor is the single chokepoint for every job regardless of how it was started. The effective
    /// speed multiplier is the per-business value times the optional rank-based speed multiplier; a higher
    /// multiplier means a shorter completion time.
    /// </summary>
    [HarmonyPatch(typeof(LaunderingOperation), MethodType.Constructor, new[] { typeof(Business), typeof(float), typeof(int) })]
    public class LaunderSpeedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LaunderingOperation __instance)
        {
            ModBankingConfiguration config = Core.Get<ModBanking>().Configuration;
            if (!config.Enabled)
                return;

            Business business = __instance.business;
            if (business == null)
                return;

            LaunderingConfiguration laundering = config.Laundering;
            float multiplier = 1f;

            string name = business.PropertyName;
            if (!string.IsNullOrEmpty(name)
                && laundering.Businesses.TryGetValue(name, out BusinessLaunderingConfiguration b))
            {
                multiplier *= b.SpeedMultiplier;
            }

            multiplier *= ModBanking.GetRankMultiplier(laundering.XpScaling, laundering.XpScaling.SpeedMultiplierByRank);

            if (multiplier <= 0f || Mathf.Approximately(multiplier, 1f))
                return;

            __instance.completionTime_Minutes = Mathf.Max(1, Mathf.RoundToInt(__instance.completionTime_Minutes / multiplier));
        }
    }
}
