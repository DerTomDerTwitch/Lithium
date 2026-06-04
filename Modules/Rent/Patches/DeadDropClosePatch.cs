using System;
using HarmonyLib;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;

namespace Lithium.Modules.Rent.Patches
{
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
