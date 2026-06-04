using HarmonyLib;
using Il2CppScheduleOne.Property;
using UnityEngine;

namespace Lithium.Modules.Banking.Patches
{
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
