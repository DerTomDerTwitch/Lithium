using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Levelling;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(CustomerData), nameof(CustomerData.GetOrderDays))]
    public class CustomerGetOrderDaysPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CustomerData __instance, ref Il2CppSystem.Collections.Generic.List<EDay> __result)
        {
            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            if (!config.Enabled || !config.Contracts.Enabled)
                return;

            if (config.OrderPatterns.Enabled && config.OrderPatterns.RankMet())
            {
                OrderPatternProfile profile = OrderPatternProfile.Create(
                    __instance.name, __instance.MinOrdersPerWeek, __instance.MaxOrdersPerWeek);

                if (profile.OrderDays != null && profile.OrderDays.Count > 0)
                    __result = profile.OrderDays.ToIL2CPPList();
            }

            if (config.Contracts.RetryNextDayOnRefusal &&
                ContractRetryTracker.HasPendingRetry(__instance.name, out EDay retryDay))
            {
                List<EDay> days = __result != null ? __result.ToList() : [];
                if (!days.Contains(retryDay))
                {
                    days.Add(retryDay);
                    __result = days.ToIL2CPPList();
                }
            }
        }
    }
}
