using HarmonyLib;
using Il2CppScheduleOne.Map;

namespace Lithium.Modules.Warehouse.Patches
{
    // DarkMarket.ShouldBeOpen() is called every frame from Update() and drives DarkMarket.IsOpen,
    // which controls whether the market vendor/deliveries are active. Vanilla returns false outside
    // [AccessZone.OpenTime, AccessZone.CloseTime] OR while any player is being pursued. Once the
    // player meets the configured rank we skip the time-of-day check, keeping only the (configurable)
    // pursuit lockout. Below the rank requirement we run the original method unchanged.
    [HarmonyPatch(typeof(DarkMarket), nameof(DarkMarket.ShouldBeOpen))]
    public class DarkMarketShouldBeOpenPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            ModWarehouse module = Core.Get<ModWarehouse>();
            if (module == null || !module.Configuration.Enabled)
                return true;

            if (!module.RequirementMet())
                return true;

            if (module.Configuration.CloseDuringPursuit && ModWarehouse.AnyPlayerPursued())
            {
                __result = false;
                return false;
            }

            __result = true;
            return false;
        }
    }
}
