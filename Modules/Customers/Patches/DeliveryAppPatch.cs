using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.UI.Phone.Delivery;
using Lithium.Modules.Shops;
using Lithium.Helper;
using Lithium.Util;

namespace Lithium.Modules.Customers.Patches
{
    [HarmonyPatch(typeof(DeliveryApp), nameof(DeliveryApp.SetOpen))]
    public class DeliveryAppPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref bool open, DeliveryApp __instance)
        {
            ModShops modShops = Core.Get<ModShops>();
            if (modShops == null || !modShops.Configuration.Enabled)
                return;

            ModShopsConfiguration config = Core.Get<ModShops>().Configuration;

            config.Deliveries ??= new Dictionary<string, DeliverySettings>();

            bool added = false;
            foreach (DeliveryShop d in DeliveryApp.Instance.deliveryShops.ToList())
            {
                if (d?.MatchingShop == null || config.Deliveries.ContainsKey(d.MatchingShop.ShopName))
                    continue;

                config.Deliveries[d.MatchingShop.ShopName] = new DeliverySettings
                {
                    Availability = DeliveryAvailabilitySettings.Unchanged,
                    DeliveryFee = d.GetDeliveryFee(),
                    XPRequirement = 0
                };
                added = true;
            }

            if (added)
                config.SaveConfiguration();

            DeliveryUtils.ApplyDeliveryOverrides();
        }
    }
}
