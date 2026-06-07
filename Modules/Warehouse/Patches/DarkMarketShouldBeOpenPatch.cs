using HarmonyLib;
using Il2CppScheduleOne.Map;

namespace Lithium.Modules.Warehouse.Patches
{
    // Keeps the Dark Market open past its vanilla hours once the player meets the configured rank.
    //
    // DarkMarket.IsOpen is driven solely by `IsOpen = ShouldBeOpen();` inside Update(). ShouldBeOpen is a
    // private one-liner-caller helper that IL2CPP can inline into Update, which would bypass a patch on it.
    // Update() is a Unity message (un-inlinable), so this postfix overrides IsOpen there after vanilla has
    // set it: above the rank requirement it leaves vanilla untouched; at/above it, the market is forced
    // open except (configurably) while a player is being pursued.
    [HarmonyPatch(typeof(DarkMarket), "Update")]
    public class DarkMarketShouldBeOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DarkMarket __instance)
        {
            ModWarehouse module = Core.Get<ModWarehouse>();
            if (module == null || !module.Configuration.Enabled)
                return;

            if (!module.RequirementMet())
                return;

            if (module.Configuration.CloseDuringPursuit && ModWarehouse.AnyPlayerPursued())
            {
                __instance.IsOpen = false;
                return;
            }

            __instance.IsOpen = true;
        }
    }
}
