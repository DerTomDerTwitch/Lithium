using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Property;

namespace Lithium.Modules.Storyline.Patches
{
    // Prevents looting the RV's *original* starter furniture. BuildableItem.CanBePickedUp is the single
    // chokepoint for the right-click "hold to pick up" path (InteractionManager.CheckRightClick), which
    // is the only way furniture ends up back in the player's inventory (and from there, sold).
    //
    // We short-circuit it to false only for the specific starter pieces, identified by their per-instance
    // GUID. These GUIDs are baked into the new-game template save and are identical across fresh games
    // (verified via the F10 RVFurnitureDebug dump over two separate new games), so they're a stable,
    // precise key: a bought-and-placed copy of the same item gets a freshly generated GUID and stays
    // lootable. If a GUID ever fails to match (e.g. a game update regenerates them) the failure mode is
    // safe — that piece simply reverts to vanilla lootable behaviour, nothing breaks.
    //
    // Gated on !PreventRVExplosion: the furniture lock only applies while RV-explosion prevention is off.
    // Nothing is destroyed or modified — the item just reports as un-pickable with a crosshair reason —
    // so there is no save impact and a live config toggle (Ctrl+Shift+F8) fully restores vanilla behaviour.
    [HarmonyPatch(typeof(BuildableItem), nameof(BuildableItem.CanBePickedUp))]
    public class RVFurniturePickupPatch
    {
        // The 9 starter furniture pieces shipped inside the RV, keyed by their stable per-instance GUID.
        private static readonly HashSet<string> StarterFurnitureGuids = new(StringComparer.OrdinalIgnoreCase)
        {
            "98648644-f1a1-4106-9197-cec0c7ed1a73", // LED Grow Light
            "6544dc8e-adcd-4cd5-bf1f-8f579c04da6c", // LED Grow Light
            "7ba51973-2431-4e48-96da-79539be4c90f", // Trash Can
            "4b341e8d-8538-428c-8694-399a328f06b9", // Suspension Rack
            "011cc508-c95a-409d-b2fb-37bf19b8f110", // Suspension Rack
            "78ec1f47-07da-424d-82a9-1e83dad20de3", // Plastic Pot
            "46b275ec-6cfd-4263-aa6d-6fe8c76b1e52", // Plastic Pot
            "4915398b-5135-4d64-8887-0faa09392534", // Packaging Station
            "3a757d1a-2278-4431-8517-a64c74c6391f", // Medium Storage Rack
        };

        [HarmonyPrefix]
        public static bool Prefix(BuildableItem __instance, ref string reason, ref bool __result)
        {
            ModStorylineConfiguration config = Core.Get<ModStoryline>().Configuration;
            if (!config.Enabled || !config.PreventFurnitureLooting || config.PreventRVExplosion)
                return true;

            Property parent = __instance.ParentProperty;
            if (parent == null || parent.TryCast<RV>() == null)
                return true;

            if (!StarterFurnitureGuids.Contains(__instance.GUID.ToString()))
                return true;

            reason = "Can't take the RV's furniture";
            __result = false;
            return false;
        }
    }
}
