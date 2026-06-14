using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.Persistence;
using Lithium.Modules.Customers.Architecture;

namespace Lithium.Helper
{
    /// <summary>
    /// Commits every <see cref="SaveSlotStore{TValue}"/>'s in-memory ("floating") state to disk exactly when
    /// the game writes a save. The stores otherwise never touch disk on mutation, so this hook is what makes
    /// runtime changes persist only on an actual save (manual or sleeping) — matching the game's own contract
    /// that quitting/returning to menu without saving reverts to the last saved state.
    ///
    /// <para><see cref="SaveManager.Save(string)"/> is the single chokepoint: the parameterless
    /// <c>Save()</c> delegates to it, and both manual saves and sleep/auto saves route through it. It is
    /// host-only (guarded by <c>IsServer</c> inside the method) and cannot be inlined (it contains a save
    /// coroutine), so it is a reliable patch point in IL2CPP.</para>
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new Type[] { typeof(string) })]
    public static class SaveFlushPatch
    {
        [HarmonyPrefix]
        public static void Prefix(SaveManager __instance)
        {
            // Mirror the game's own preconditions so a rejected call (not host / already mid-save) doesn't
            // flush. The remaining bail cases the game checks (no game loaded, tutorial) are harmless here:
            // with no loaded save the stores can't resolve a slot key, so Flush() no-ops anyway.
            if (!InstanceFinder.IsServer || __instance.IsSaving)
                return;

            try
            {
                SaveSlotStores.FlushAll();
            }
            catch (Exception e)
            {
                Log.Warning($"[Lithium] Save flush failed: {e.Message}");
            }
        }
    }
}
