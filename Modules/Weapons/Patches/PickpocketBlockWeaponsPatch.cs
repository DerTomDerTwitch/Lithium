using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.UI;

namespace Lithium.Modules.Weapons.Patches
{
    // Blocks looting weapons off police officers. PickpocketScreen.Open is the single loot chokepoint
    // for both conscious pickpocketing and "view inventory" on a downed officer. We run after Open has
    // assigned/unlocked the officer's inventory slots, then re-lock any weapon slot and hide its green
    // area: locking blocks the free-drag (unconscious) path, hiding the green area blocks the minigame
    // (conscious) path, since GetHoveredSlot only considers active green areas. The weapon stays on the
    // officer — it is simply un-takeable — so nothing is destroyed and there is no save impact.
    [HarmonyPatch(typeof(PickpocketScreen), nameof(PickpocketScreen.Open))]
    public class PickpocketBlockWeaponsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PickpocketScreen __instance, NPC _npc)
        {
            ModWeapons module = Core.Get<ModWeapons>();
            if (module == null || !module.Configuration.Enabled || !module.Configuration.BlockPoliceLooting)
                return;

            if (_npc == null || _npc.TryCast<PoliceOfficer>() == null)
                return;

            var slots = __instance.Slots;
            if (slots == null)
                return;

            for (int i = 0; i < slots.Length; i++)
            {
                ItemSlotUI slotUI = slots[i];
                ItemSlot slot = slotUI?.assignedSlot;
                ItemInstance item = slot?.ItemInstance;
                if (item == null)
                    continue;

                if (!WeaponMatcher.IsWeapon(item.Definition))
                    continue;

                // Re-lock add + removal (blocks dragging it out) and show the UI "Locked" overlay.
                __instance.SetSlotLocked(i, true);

                // Hide the green area so the pickpocket minigame can never target this slot.
                if (i < __instance.GreenAreas.Length && __instance.GreenAreas[i] != null)
                    __instance.GreenAreas[i].gameObject.SetActive(false);
            }
        }
    }
}
