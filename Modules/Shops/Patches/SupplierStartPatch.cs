using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Shop;
using Lithium.Helper;

namespace Lithium.Modules.Shops.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.NetworkInitialize__Late))]
    public class SupplierStartPatch
    {
        [HarmonyPrefix]
        public static void PatchPrices()
        {
            ModShopsConfiguration configuration = Core.Get<ModShops>().Configuration;

            // Always populate the config with the live shop/supplier values so the user has a
            // ready-to-edit template, even while the module (or an individual shop's Override flag)
            // is disabled. The actual overrides are only applied when Enabled — the population and
            // application steps are gated independently inside the two helpers below.
            ApplyShopOverrides();
            ApplySupplierOverrides();
            configuration.SaveConfiguration();
        }

        private static void ApplySupplierOverrides()
        {
            void AssertSupplierConfigEntryExists(ref SupplierListingOverride configuration, Supplier supplier)
            {
                configuration ??= new SupplierListingOverride();
                if (supplier == null)
                    return;

                // Add a price override for every online item we don't already track, filling the
                // default (empty) config and any items added by later game patches.
                foreach (PhoneShopInterface.Listing listing in supplier.OnlineShopItems)
                {
                    if (!configuration.PriceOverrides.ContainsKey(listing.Item.ID))
                        configuration.PriceOverrides[listing.Item.ID] = listing.Price;
                }
            }

            void ApplySupplierConfigValues(SupplierListingOverride configuration, Supplier supplier)
            {
                if (supplier == null)
                    return;

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
            if (configuration.Enabled)
                ApplySupplierConfigValues(configuration.Albert, albert);

            Shirley shirley = UnityEngine.Object.FindObjectOfType<Shirley>();
            AssertSupplierConfigEntryExists(ref configuration.Shirley, shirley);
            if (configuration.Enabled)
                ApplySupplierConfigValues(configuration.Shirley, shirley);

            Salvador salvador = UnityEngine.Object.FindObjectOfType<Salvador>();
            AssertSupplierConfigEntryExists(ref configuration.Salvador, salvador);
            if (configuration.Enabled)
                ApplySupplierConfigValues(configuration.Salvador, salvador);

            Phil phil = UnityEngine.Object.FindObjectOfType<Phil>();
            AssertSupplierConfigEntryExists(ref configuration.Phil, phil);
            if (configuration.Enabled)
                ApplySupplierConfigValues(configuration.Phil, phil);
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
                    case "shrooms_shop":
                        AssertConfigurationEntries(ref configuration.ShroomSupplier, shopInterface, listings);
                        if (configuration.Enabled)
                            ApplyShopSettings(listings, shopInterface, configuration.ShroomSupplier);
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
            configSetting ??= new ShopListingSettings();

            // A freshly-created entry has no item overrides yet, so adopt the shop's real payment type.
            if (configSetting.ItemOverrides.Count == 0)
                configSetting.PaymentType = shopInterface.PaymentType;

            // Add an override for every listing we don't already track. This populates the default
            // (empty) config the first time a save is loaded and picks up items added by later game
            // patches, while leaving any existing user-edited overrides untouched.
            foreach (ShopListing listing in listings)
            {
                if (configSetting.ItemOverrides.ContainsKey(listing.Item.ID))
                    continue;

                configSetting.ItemOverrides[listing.Item.ID] = new ItemListingOverride
                {
                    Price = listing.Price,
                    Stock = listing.LimitedStock ? listing.DefaultStock : -1,
                    RestockRate = listing.RestockRate,
                };
            }

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
