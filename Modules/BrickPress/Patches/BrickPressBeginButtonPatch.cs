using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI.Stations;
using GameBrickPress = Il2CppScheduleOne.ObjectScripts.BrickPress;

namespace Lithium.Modules.BrickPress.Patches
{
    [HarmonyPatch(typeof(BrickPressCanvas), nameof(BrickPressCanvas.BeginButtonPressed))]
    public class BrickPressBeginButtonPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(BrickPressCanvas __instance)
        {
            ModBrickPress module = Core.Get<ModBrickPress>();
            if (module == null || !module.Configuration.Enabled || !module.Configuration.InstantPress)
                return true;

            GameBrickPress press = __instance.Press;
            if (press == null)
                return true;

            if (!press.HasSufficientProduct(out ProductItemInstance product) || product == null)
                return true;

            var brickPackaging = press.BrickPackaging;
            if (brickPackaging == null || brickPackaging.Quantity <= 0)
                return true;
            int unitsPerBrick = brickPackaging.Quantity;

            press.GetMainInputs(out ItemInstance primaryItem, out int primaryQuantity, out _, out _);
            if (primaryItem == null)
                return true;

            var productSlots = press.ProductSlots;
            if (productSlots == null)
                return true;

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
                return true;

            var brickProbe = product.GetCopy(1)?.TryCast<ProductItemInstance>();
            if (brickProbe == null)
                return true;
            brickProbe.SetPackaging(brickPackaging);
            int outputCapacity = press.OutputSlot != null ? press.OutputSlot.GetCapacityForItem(brickProbe) : 0;

            if (outputCapacity < bricksToProduce)
                return true;

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

            return false;
        }
    }
}
