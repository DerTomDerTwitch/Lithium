using HarmonyLib;
using Il2CppScheduleOne.Property;

namespace Lithium.Modules.Banking.Patches
{
    /// <summary>
    /// Time-skip (sleep) counterpart to <see cref="LaunderCutPatch"/>. Laundering operations that finish
    /// during a clock skip complete through <c>Business.TimeSkipped → MinsPass(minsPassed)</c>, not the
    /// per-minute <c>MinPass</c>, so the cut must be charged here too. <c>TimeSkipped</c> is a real
    /// <c>onTimeSkip</c> subscriber (un-inlinable). Per-minute and skip are mutually exclusive each tick,
    /// so the two patches never double-charge.
    /// </summary>
    [HarmonyPatch(typeof(Business), nameof(Business.TimeSkipped))]
    public class LaunderCutSkipPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Business __instance, int minsPassed)
        {
            LaunderCutSettler.Snapshot(__instance, minsPassed);
        }

        [HarmonyPostfix]
        public static void Postfix(Business __instance)
        {
            LaunderCutSettler.Settle(__instance);
        }
    }
}
