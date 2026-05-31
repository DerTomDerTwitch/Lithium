using HarmonyLib;
using Il2CppScheduleOne.UI.Phone.Delivery;

namespace Lithium.Modules.Shops.Patches
{
    [HarmonyPatch(typeof(DeliveryShop), "GetDeliveryFee")]
    public class DeliveryShopFeePatch
    {
        [HarmonyPostfix]
        public static void Postfix(DeliveryShop __instance, ref float __result)
        {
            ModShops modShops = Core.Get<ModShops>();
            if (modShops == null || !modShops.Configuration.Enabled)
                return;

            Dictionary<string, DeliverySettings> deliveries = modShops.Configuration.Deliveries;
            if (deliveries == null || __instance.MatchingShop == null)
                return;

            if (deliveries.TryGetValue(__instance.MatchingShop.ShopName, out DeliverySettings settings)
                && settings.Availability != DeliveryAvailabilitySettings.Unchanged)
            {
                __result = settings.DeliveryFee;
            }
        }
    }
}
