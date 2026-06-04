using HarmonyLib;
using Il2CppScheduleOne.Property;

namespace Lithium.Modules.Banking.Patches
{
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
                && business.Capacity >= 0f)
            {
                __result = business.Capacity;
            }

            __result *= ModBanking.GetRankMultiplier(laundering.XpScaling, laundering.XpScaling.CapacityMultiplierByRank);
        }
    }
}
