using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI;

namespace Lithium.Modules.Employees.Patches
{
    [HarmonyPatch(typeof(StorageMenu), nameof(StorageMenu.Open), typeof(IItemSlotOwner), typeof(string), typeof(string))]
    public class StorageMenuRowPatch
    {
        [HarmonyPostfix]
        private static void Postfix(StorageMenu __instance, IItemSlotOwner __0)
        {
            ModEmployees mod = Core.Get<ModEmployees>();
            if (mod == null || !mod.Configuration.Enabled)
                return;

            NPCInventory npcInventory = __0.TryCast<NPCInventory>();
            if (npcInventory == null)
                return;

            ModEmployeesConfiguration config = mod.Configuration;
            int? rows = null;
            if (npcInventory.GetComponentInParent<Botanist>())
                rows = config.Botanists.InventoryRowCount;
            else if (npcInventory.GetComponentInParent<Chemist>())
                rows = config.Chemists.InventoryRowCount;
            else if (npcInventory.GetComponentInParent<Cleaner>())
                rows = config.Cleaners.InventoryRowCount;
            else if (npcInventory.GetComponentInParent<Dealer>())
                rows = config.Dealers.InventoryRowCount;
            else if (npcInventory.GetComponentInParent<Packager>())
                rows = config.Packagers.InventoryRowCount;

            if (rows.HasValue)
                __instance.SlotGridLayout.constraintCount = rows.Value;
        }
    }
}
