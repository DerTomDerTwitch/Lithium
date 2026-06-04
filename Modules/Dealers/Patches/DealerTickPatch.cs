using System;
using HarmonyLib;
using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.Dealers.Patches
{
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.PassMinute))]
    public class DealerTickPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                Core.Get<ModDealers>()?.Tick();
            }
            catch (Exception e)
            {
                Log.Warning($"[Dealers] Tick failed: {e.Message}");
            }
        }
    }
}
