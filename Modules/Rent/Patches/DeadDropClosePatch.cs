using System;
using HarmonyLib;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;

namespace Lithium.Modules.Rent.Patches
{
    /// <summary>
    /// When any storage UI is closed, check whether it was a rent location's dead drop and, if so, collect
    /// the rent from the cash inside it. Runs as a prefix so <c>OpenedStorageEntity</c> is still set; all the
    /// real work (and matching against the dead drops) happens in <see cref="ModRent.ProcessPayment"/>.
    /// </summary>
    [HarmonyPatch(typeof(StorageMenu), nameof(StorageMenu.CloseMenu))]
    public class DeadDropClosePatch
    {
        [HarmonyPrefix]
        public static void Prefix(StorageMenu __instance)
        {
            ModRent mod = Core.Get<ModRent>();
            if (mod == null || !mod.Configuration.Enabled)
                return;

            try
            {
                StorageEntity storage = __instance.OpenedStorageEntity;
                if (storage != null)
                    mod.ProcessPayment(storage);
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Dead drop close handling failed: {e.Message}");
            }
        }
    }
}
