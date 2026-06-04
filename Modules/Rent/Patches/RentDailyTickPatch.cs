using System;
using HarmonyLib;
using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.Rent.Patches
{
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.PassMinute))]
    public class RentDailyTickPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                Core.Get<ModRent>()?.Tick();
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Daily tick failed: {e.Message}");
            }
        }
    }
}
