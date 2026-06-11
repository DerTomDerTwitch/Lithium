using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Variables;

namespace Lithium.Helper
{
    /// <summary>
    /// Guards the host→client replication channel (<see cref="HostStateSync"/>). A mod variable's value is
    /// authoritative on the host only; but the game's <c>Variable&lt;T&gt;</c> constructor unconditionally
    /// replicates its initial value, so when a <b>client</b> creates one of our variables (to be able to
    /// receive the host's value) its ctor would fire a <c>SendValue</c> ServerRpc and broadcast the default,
    /// clobbering the host's value on every peer. This prefix drops any client-side <c>SendValue</c> whose
    /// variable name carries the Lithium prefix, so our variables only ever flow host→client. Game variables
    /// and host-side sends are untouched.
    /// </summary>
    [HarmonyPatch(typeof(VariableDatabase), nameof(VariableDatabase.SendValue))]
    public static class VariableSyncGuardPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(string variableName)
        {
            if (!InstanceFinder.IsServer
                && variableName != null
                && variableName.StartsWith(HostStateSync.Prefix, StringComparison.OrdinalIgnoreCase))
                return false; // swallow the client's replication attempt

            return true;
        }
    }
}
