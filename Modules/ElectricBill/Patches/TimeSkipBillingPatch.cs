using System;
using HarmonyLib;
using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.ElectricBill.Patches
{
    // Bills the electricity used during a time-skip (sleeping, story skips). The game advances each
    // station's cook by Abs(minSum(newTime) - minSum(oldTime)) minutes via onTimeSkip, so we meter the
    // same count. Runs as a prefix to sample appliance state *before* operations advance — i.e. what was
    // left running when the skip began.
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.OnTimeSkip_Client))]
    public class TimeSkipBillingPatch
    {
        [HarmonyPrefix]
        public static void Prefix(int oldTime, int newTime)
        {
            try
            {
                int minutes = Math.Abs(TimeManager.GetMinSumFrom24HourTime(newTime)
                                       - TimeManager.GetMinSumFrom24HourTime(oldTime));
                if (minutes > 0)
                    Core.Get<ModElectricBill>()?.AccrueTimeSkip(minutes);
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] Time-skip billing failed: {e.Message}");
            }
        }
    }
}
