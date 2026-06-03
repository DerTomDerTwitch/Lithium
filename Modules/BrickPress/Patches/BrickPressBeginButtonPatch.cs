using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI.Stations;
using GameBrickPress = Il2CppScheduleOne.ObjectScripts.BrickPress;

namespace Lithium.Modules.BrickPress.Patches
{
    // The "Begin" button in the brick press UI normally spawns a UseBrickPress player task — the
    // interactive pour-then-press minigame performed at the machine. With this module enabled we
    // short-circuit that: the brick is produced straight away by calling the very same CompletePress
    // the minigame task invokes when it finishes. One click does the whole job.
    //
    // We deliberately leave the UI open afterwards. CompletePress deposits the finished brick into the
    // press's OutputSlot, which is only reachable through the open canvas (OutputSlotUI); the canvas
    // Update() runs every frame and refreshes itself from the press state, so it immediately shows the
    // populated output slot and disables Begin. Closing the UI here would hide the result and force the
    // player to re-open the press just to collect it.
    //
    // CompletePress *drains everything*: it consumes all loaded product and produces as many bricks as
    // that yields. That makes two things unsafe if we call it blindly, both of which would destroy
    // product the player loaded:
    //   1. Mixed inputs — if the product slots hold different items/qualities, only the dominant product
    //      should be pressed; the rest must be left untouched. We hold the non-matching slots aside
    //      (detach, press, re-attach) so the drain can only see the dominant product.
    //   2. Output overflow — if the resulting bricks don't all fit in the output slot (it's full, holds
    //      an incompatible item, or only has room for some), the excess would be voided. We can't press
    //      "only what fits" because a single CompletePress drains the lot and there is nowhere to stash a
    //      partial remainder without voiding it, so in that case we fall back to the vanilla minigame,
    //      which lets the player press exactly what they want with no risk of loss.
    [HarmonyPatch(typeof(BrickPressCanvas), nameof(BrickPressCanvas.BeginButtonPressed))]
    public class BrickPressBeginButtonPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(BrickPressCanvas __instance)
        {
            ModBrickPress module = Core.Get<ModBrickPress>();
            if (module == null || !module.Configuration.Enabled || !module.Configuration.InstantPress)
                return true; // run the vanilla minigame

            GameBrickPress press = __instance.Press;
            if (press == null)
                return true;

            // Only act when enough product is actually loaded; otherwise defer to vanilla, which simply
            // refuses to start. HasSufficientProduct also hands us the product that would be pressed.
            if (!press.HasSufficientProduct(out ProductItemInstance product) || product == null)
                return true;

            // The brick recipe: each brick consumes BrickPackaging.Quantity units of product. Without a
            // valid recipe we can't reason about overflow, so defer to vanilla.
            var brickPackaging = press.BrickPackaging;
            if (brickPackaging == null || brickPackaging.Quantity <= 0)
                return true;
            int unitsPerBrick = brickPackaging.Quantity;

            // The dominant input item and its total loaded quantity (the press's own aggregate across all
            // input slots — this is exactly what CompletePress will drain). Every product slot is judged
            // "matching" against the dominant item; the bricks are made of it too.
            press.GetMainInputs(out ItemInstance primaryItem, out int primaryQuantity, out _, out _);
            if (primaryItem == null)
                return true;

            var productSlots = press.ProductSlots;
            if (productSlots == null)
                return true;

            // Remember which slots hold something else (different item or quality) so we can keep them out
            // of the drain — CompletePress would otherwise consume them too.
            var foreignSlots = new List<ItemSlot>();
            for (int i = 0; i < productSlots.Length; i++)
            {
                ItemSlot slot = productSlots[i];
                ItemInstance stored = slot?.ItemInstance;
                if (stored != null && !stored.CanStackWith(primaryItem, false))
                    foreignSlots.Add(slot);
            }

            int bricksToProduce = primaryQuantity / unitsPerBrick;
            if (bricksToProduce <= 0)
                return true; // not enough of the dominant product for a whole brick — let vanilla decide

            // How many of these bricks the output slot can actually accept. Build a throwaway brick of the
            // dominant product (the real output form) so GetCapacityForItem accounts for the brick's stack
            // limit and rejects an output slot already holding an incompatible item.
            var brickProbe = product.GetCopy(1)?.TryCast<ProductItemInstance>();
            if (brickProbe == null)
                return true;
            brickProbe.SetPackaging(brickPackaging);
            int outputCapacity = press.OutputSlot != null ? press.OutputSlot.GetCapacityForItem(brickProbe) : 0;

            // If the full drain wouldn't fit, pressing would void the overflow. Hand off to the vanilla
            // minigame instead — it never auto-drains, so nothing is lost.
            if (outputCapacity < bricksToProduce)
                return true;

            // Committed to the instant press: hold the foreign slots aside so CompletePress only consumes
            // the dominant product, then restore them exactly as they were.
            var heldAside = new List<(ItemSlot slot, ItemInstance item, int quantity)>();
            foreach (ItemSlot slot in foreignSlots)
            {
                heldAside.Add((slot, slot.ItemInstance, slot.Quantity));
                slot.ClearStoredInstance();
            }

            press.CompletePress(product);

            foreach ((ItemSlot slot, ItemInstance item, int quantity) in heldAside)
            {
                slot.SetStoredItem(item);
                slot.SetQuantity(quantity);
            }

            // Leave the canvas open so the player can take the brick from the output slot (see above).
            return false; // skip spawning the UseBrickPress minigame task
        }
    }
}
