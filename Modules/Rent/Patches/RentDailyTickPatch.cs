using System;
using HarmonyLib;
using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.Rent.Patches
{
    /// <summary>
    /// Drives the rent system's daily logic. <c>TimeManager.PassMinute</c> fires every in-game minute; the
    /// module compares the elapsed-day counter and only does work when the day actually rolls over (handling
    /// multi-day jumps from sleeping). Other modules already hang per-minute work off this method.
    /// </summary>
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
