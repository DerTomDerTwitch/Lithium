using HarmonyLib;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Shop;
using Il2CppTMPro;
using Lithium.Helper;
using MelonLoader;
using UnityEngine;

namespace Lithium.Modules.Shops.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.NetworkInitialize__Late))]
    public class SupplierStartPatch
    {
        [HarmonyPrefix]
        public static void PatchPrices()
        {
            ModShopsConfiguration configuration = Core.Get<ModShops>().Configuration;
            if (!configuration.Enabled)
                return;

            ApplyShopOverrides();
            //ApplySupplierOverrides();
            ApplySkateboardOverrides();
            configuration.SaveConfiguration();
        }

        private static void ApplySkateboardOverrides()
        {
            ModShopsConfiguration configuration = Core.Get<ModShops>().Configuration;

            DialogueController_SkateboardSeller[] dialogueControllers = UnityEngine.Object.FindObjectsOfType<DialogueController_SkateboardSeller>();
            foreach (DialogueController_SkateboardSeller shop in dialogueControllers)
            {

                if (!configuration.SkateboardShops.TryGetValue(shop.gameObject.transform.parent?.name, out SkateboardShopSettings settings))
                {
                    MelonLogger.Warning($"Skate store, {shop.gameObject.transform.parent?.name}, not found in configuration");
                    continue;
                }

                foreach (DialogueController_SkateboardSeller.Option listing in shop.Options)
                {
                    if (!settings.SkateboardOverrides.TryGetValue(listing.Item.ID, out SkateboardListingOverride overrideBoard))
                    {
                        MelonLogger.Warning($"Skateboard, {listing.Item.ID}, not found in configuration");
                        continue;
                    }

                    listing.Price = overrideBoard.Price;
                    listing.IsAvailable = overrideBoard.IsAvailable;
                    UIUpdates(listing.Name, settings, overrideBoard);
                }
            }

            static void UIUpdates(String name, SkateboardShopSettings shop, SkateboardListingOverride skateboard)
            {
                GameObject sign = UnityEngine.GameObject.Find($"{shop.UIContainer}/{skateboard.UIPath}");
                if (sign == null)
                {
                    MelonLogger.Warning($"Unable to find skateboard sign path: {shop.UIContainer}/{skateboard.UIPath}");
                    return;
                }
                if (!sign.TryGetComponent<TextMeshPro>(out TextMeshPro priceText))
                {
                    MelonLogger.Warning($"Unable to find text component for: {shop.UIContainer}/{skateboard.UIPath}");
                    return;
                }
                priceText.SetText($"{name}\n{MoneyManager.FormatAmount(skateboard.Price)}");
            }
        }


        private static void ApplySupplierOverrides()
        {
            void AssertSupplierConfigEntryExists(ref SupplierListingOverride configuration, Supplier supplier)
            {
                configuration ??= new()
                {
                    PriceOverrides = supplier.OnlineShopItems.ToDictionary(
                        listing => listing.Item.ID,
                        listing => listing.Price)
                };
            }

            void ApplySupplierConfigValues(SupplierListingOverride configuration, Supplier supplier)
            {
                foreach (PhoneShopInterface.Listing listing in supplier.OnlineShopItems)
                {
                    if (configuration.PriceOverrides.TryGetValue(listing.Item.ID, out float @override))
                    {
                        listing.Item.BasePurchasePrice = @override;
                    }
                }
            }

            ModShopsConfiguration configuration = Core.Get<ModShops>().Configuration;
            Albert albert = UnityEngine.Object.FindObjectOfType<Albert>();
            AssertSupplierConfigEntryExists(ref configuration.Albert, albert);
            ApplySupplierConfigValues(configuration.Albert, albert);

            Shirley shirley = UnityEngine.Object.FindObjectOfType<Shirley>();
            AssertSupplierConfigEntryExists(ref configuration.Shirley, shirley);
            ApplySupplierConfigValues(configuration.Shirley, shirley);

            Salvador salvador = UnityEngine.Object.FindObjectOfType<Salvador>();
            AssertSupplierConfigEntryExists(ref configuration.Salvador, salvador);
            ApplySupplierConfigValues(configuration.Salvador, salvador);
        }

        private static void ApplyShopOverrides()
        {
            List<ShopInterface> shop = UnityEngine.Object.FindObjectsOfType<ShopInterface>().ToList();
            ModShopsConfiguration configuration = Core.Get<ModShops>().Configuration;
            foreach (ShopInterface shopInterface in shop)
            {
                if (shopInterface == null)
                    continue;
                List<ShopListing> listings = shopInterface.Listings.ToList();
                if (listings == null)
                    continue;
                switch (shopInterface.ShopCode)
                {
                    case "thrifty_threads":
                        AssertConfigurationEntries(ref configuration.ThriftyThreads, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.ThriftyThreads);
                        break;
                    case "coke_shop":
                        AssertConfigurationEntries(ref configuration.CokeSupplier, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.CokeSupplier);
                        break;
                    case "meth_shop":
                        AssertConfigurationEntries(ref configuration.MethSupplier, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.MethSupplier);
                        break;
                    case "weed_shop":
                        AssertConfigurationEntries(ref configuration.WeedSupplier, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.WeedSupplier);
                        break;
                    case "boutique":
                        AssertConfigurationEntries(ref configuration.Boutique, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.Boutique);
                        break;
                    case "dark_market_shop":
                        AssertConfigurationEntries(ref configuration.DarkMarket, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.DarkMarket);
                        break;
                    case "gas_mart_west":
                        AssertConfigurationEntries(ref configuration.GasStation, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.GasStation);
                        break;
                    case "gas_mart_central":
                        AssertConfigurationEntries(ref configuration.CentralGasStation, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.CentralGasStation);
                        break;
                    case "dans_hardware":
                        AssertConfigurationEntries(ref configuration.DansHardware, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.DansHardware);
                        break;
                    case "handy_hanks":
                        AssertConfigurationEntries(ref configuration.HandyHanks, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.HandyHanks);
                        break;
                }
            }
        }

        private static void AssertConfigurationEntries(ref ShopListingSettings configSetting,
            ShopInterface shopInterface, List<ShopListing> listings)
        {
            configSetting ??= new()
            {
                Override = false,
                DefaultStock = -1,
                PaymentType = shopInterface.PaymentType,
                ItemOverrides = listings.ToDictionary(
                    listing => listing.Item.ID,
                    listing => new ItemListingOverride
                    {
                        Price = listing.Price,
                        Stock = listing.LimitedStock ? listing.DefaultStock : -1,
                        RestockRate = listing.RestockRate,
                    }),
            };
            shopInterface.RefreshShownItems();
            shopInterface.RefreshUnlockStatus();
        }

        private static void ApplyShopSettings(List<ShopListing> listings, ShopInterface shopInterface,
            ShopListingSettings shopSettings)
        {
            if (!shopSettings.Override)
                return;

            shopInterface.PaymentType = shopSettings.PaymentType;
            foreach (ShopListing listing in listings)
            {
                if (!shopSettings.ItemOverrides.TryGetValue(listing.Item.ID, out ItemListingOverride overrideItem))
                    continue;

                listing.LimitedStock = overrideItem.Stock >= 0;
                listing.DefaultStock = overrideItem.Stock >= 0 ? overrideItem.Stock : shopSettings.DefaultStock;
                listing.CurrentStock = listing.DefaultStock;
                listing.OverridePrice = true;
                listing.OverriddenPrice = overrideItem.Price;
                listing.RestockRate = overrideItem.RestockRate;
            }
        }
    }
}
