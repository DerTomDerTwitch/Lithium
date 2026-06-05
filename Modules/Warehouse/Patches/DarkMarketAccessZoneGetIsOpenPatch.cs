using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Map;

namespace Lithium.Modules.Warehouse.Patches
{
    // DarkMarketAccessZone.GetIsOpen() drives the warehouse door locks (via AccessZone.SetIsOpen,
    // called once per in-game minute). Vanilla body:
    //     if (!DarkMarket.IsOpen || !DarkMarket.Unlocked) return false;
    //     return base.GetIsOpen();   // the time-of-day check
    // The DarkMarket.IsOpen state already reflects our ShouldBeOpen patch (so it's true 24/7 once
    // the rank requirement is met, modulo the pursuit lockout). Here we drop the base time check so
    // the doors track that state instead of relocking after hours, while still honouring the
    // Unlocked gate. Below the rank requirement we run the original method unchanged.
    [HarmonyPatch(typeof(DarkMarketAccessZone), nameof(DarkMarketAccessZone.GetIsOpen))]
    public class DarkMarketAccessZoneGetIsOpenPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            ModWarehouse module = Core.Get<ModWarehouse>();
            if (module == null || !module.Configuration.Enabled)
                return true;

            if (!module.RequirementMet())
                return true;

            DarkMarket market = NetworkSingleton<DarkMarket>.Instance;
            if (market == null)
                return true;

            __result = market.IsOpen && market.Unlocked;
            return false;
        }
    }
}
