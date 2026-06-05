using System;
using HarmonyLib;
using Il2CppScheduleOne.GameTime;

namespace Lithium.Modules.ElectricBill.Patches
{
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.PassMinute))]
    public class ElectricBillTickPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                Core.Get<ModElectricBill>()?.Tick();
            }
            catch (Exception e)
            {
                Log.Warning($"[ElectricBill] Tick failed: {e.Message}");
            }
        }
    }
}
