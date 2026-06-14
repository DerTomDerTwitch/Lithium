using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using UnityEngine;

namespace Lithium.Modules.MixingStations.Patches
{
    /// <summary>
    /// Sets the per-item mix duration on each station at <c>Start</c>, replacing the old speed multiplier.
    ///
    /// The game computes total mix time as <c>MixTimePerItem * Quantity</c> (see
    /// <c>GetMixTimeForCurrentOperation</c>), so overriding the instance field directly makes a mix of N items
    /// take <c>MixTimePerItem * N</c> in-game minutes. Unlike scaling the per-minute tick, this also keeps the
    /// station's on-screen clock accurate — it counts down the real configured time, since the clock reads the
    /// same field.
    ///
    /// Set in a <c>Start</c> postfix (the same chokepoint <see cref="MixingStationCapacityPatch"/> uses), which
    /// covers both the base station and the Mk II (which inherits <c>MixingStation.Start</c>); the Mk II is
    /// selected by casting the live instance. <c>MixTimePerItem</c> is a plain instance field (not an inlined
    /// method or a const), so writing it is honoured. Like the capacity override, a live config reload only
    /// takes effect on stations that (re)start afterwards.
    /// </summary>
    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.Start))]
    public class MixingStationDurationPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MixingStation __instance)
        {
            ModMixingStationsConfiguration config = Core.Get<ModMixingStations>().Configuration;
            if (!config.Enabled)
                return;

            bool isMk2 = __instance.TryCast<MixingStationMk2>() != null;
            __instance.MixTimePerItem = Mathf.Max(1, isMk2 ? config.Mk2MixTimePerItem : config.MixTimePerItem);
        }
    }
}
