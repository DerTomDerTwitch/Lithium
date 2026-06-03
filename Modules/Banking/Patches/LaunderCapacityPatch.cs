using HarmonyLib;
using Il2CppScheduleOne.Property;

namespace Lithium.Modules.Banking.Patches
{
    /// <summary>
    /// Controls the laundering capacity cap per business. <c>Business.appliedLaunderLimit</c> is the effective
    /// ceiling on cash in flight that the laundering UI and start-operation logic check, so adjusting its getter
    /// is the single chokepoint for caps. A per-business override (if set) replaces the base value, then the
    /// optional rank-based capacity multiplier is applied on top — so "scale the laundered amount with XP" is
    /// just a capacity multiplier that grows the cap as the player ranks up.
    /// </summary>
    [HarmonyPatch(typeof(Business), nameof(Business.appliedLaunderLimit), MethodType.Getter)]
    public class LaunderCapacityPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Business __instance, ref float __result)
        {
            ModBankingConfiguration config = Core.Get<ModBanking>().Configuration;
            if (!config.Enabled)
                return;

            LaunderingConfiguration laundering = config.Laundering;
            string name = __instance.PropertyName;

            if (!string.IsNullOrEmpty(name)
                && laundering.Businesses.TryGetValue(name, out BusinessLaunderingConfiguration business)
                && business.OverrideCapacity)
            {
                __result = business.Capacity;
            }

            __result *= ModBanking.GetRankMultiplier(laundering.XpScaling, laundering.XpScaling.CapacityMultiplierByRank);
        }
    }
}
