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
            // Gated identically to CustomerContractGenerationPatch so the frequency reshaping and the
            // quantity scaling switch on together (otherwise sub-XP customers get fewer order days
            // without the matching volume conservation).
            if (!config.Enabled || !config.Contracts.Enabled || !config.OrderPatterns.Enabled)
                return;

            if (LevelManager.Instance.TotalXP < config.Contracts.XPRequired)
                return;

            OrderPatternProfile profile = OrderPatternProfile.Create(
                __instance.name, __instance.MinOrdersPerWeek, __instance.MaxOrdersPerWeek);

            if (profile.OrderDays == null || profile.OrderDays.Count == 0)
                return;

            __result = profile.OrderDays.ToIL2CPPList();
        }
    }
}
