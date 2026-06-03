using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.NPCs;
using HarmonyLib;

namespace Lithium.Modules.Employees.Patches
{
    [HarmonyPatch(typeof(NPCInventory), nameof(NPCInventory.Awake))]
    public class NPCInventoryAwakePatch
    {
        [HarmonyPrefix]
        private static void NPCSlotPatch(NPCInventory __instance)
        {
            ModEmployees mod = Core.Get<ModEmployees>();
            if (mod == null || !mod.Configuration.Enabled)
                return;

            ModEmployeesConfiguration config = mod.Configuration;

            if (__instance.GetComponentInParent<Botanist>())
                __instance.SlotCount = config.Botanists.InventorySlotCount;
            else if (__instance.GetComponentInParent<Chemist>())
                __instance.SlotCount = config.Chemists.InventorySlotCount;
            else if (__instance.GetComponentInParent<Cleaner>())
                __instance.SlotCount = config.Cleaners.InventorySlotCount;
            else if (__instance.GetComponentInParent<Dealer>())
                __instance.SlotCount = config.Dealers.InventorySlotCount;
            else if (__instance.GetComponentInParent<Packager>())
                __instance.SlotCount = config.Packagers.InventorySlotCount;
        }
    }
}
