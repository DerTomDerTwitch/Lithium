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

            // Order-pattern reshaping (frequency). Gated like CustomerContractGenerationPatch so the
            // frequency reshaping and the quantity scaling switch on together (otherwise sub-XP customers
            // get fewer order days without the matching volume conservation).
            if (config.OrderPatterns.Enabled && LevelManager.Instance.TotalXP >= config.Contracts.XPRequired)
            {
                OrderPatternProfile profile = OrderPatternProfile.Create(
                    __instance.name, __instance.MinOrdersPerWeek, __instance.MaxOrdersPerWeek);

                if (profile.OrderDays != null && profile.OrderDays.Count > 0)
                    __result = profile.OrderDays.ToIL2CPPList();
            }

            // Retry-next-day: make sure a refused/expired customer is scheduled to re-attempt on their
            // retry day, even if that weekday isn't part of their normal (or pattern) schedule. Runs
            // independently of order patterns so the retry works either way. (CustomerContractGenerationPatch
            // clears the flag once the customer actually gets a fresh offer.)
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
