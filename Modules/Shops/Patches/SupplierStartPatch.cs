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
        // A by-ref accessor to one of the config's named override fields, so a single loop can both
        // auto-populate (`??= new()`, which writes the field back) and read each entry — replacing the
        // 11-case shop switch and the 4× copy-pasted supplier block.
        private delegate ref T ConfigRef<T>(ModShopsConfiguration configuration);

        // Maps each in-game shop code to its config field.
        private static readonly Dictionary<string, ConfigRef<ShopListingSettings>> ShopConfigByCode = new()
        {
            ["thrifty_threads"] = c => ref c.ThriftyThreads,
            ["coke_shop"] = c => ref c.CokeSupplier,
            ["meth_shop"] = c => ref c.MethSupplier,
            ["weed_shop"] = c => ref c.WeedSupplier,
            ["shrooms_shop"] = c => ref c.ShroomSupplier,
            ["boutique"] = c => ref c.Boutique,
            ["dark_market_shop"] = c => ref c.DarkMarket,
            ["gas_mart_west"] = c => ref c.GasStation,
            ["gas_mart_central"] = c => ref c.CentralGasStation,
            ["dans_hardware"] = c => ref c.DansHardware,
            ["handy_hanks"] = c => ref c.HandyHanks,
        };

        // Each online supplier: how to find its live NPC, and its config field.
        private static readonly (Func<Supplier> Find, ConfigRef<SupplierListingOverride> Config)[] Suppliers =
        {
            (() => UnityEngine.Object.FindObjectOfType<Albert>(), c => ref c.Albert),
            (() => UnityEngine.Object.FindObjectOfType<Shirley>(), c => ref c.Shirley),
            (() => UnityEngine.Object.FindObjectOfType<Salvador>(), c => ref c.Salvador),
            (() => UnityEngine.Object.FindObjectOfType<Phil>(), c => ref c.Phil),
        };

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
            foreach ((Func<Supplier> find, ConfigRef<SupplierListingOverride> config) in Suppliers)
            {
                Supplier supplier = find();
                AssertSupplierConfigEntryExists(ref config(configuration), supplier);
                if (configuration.Enabled)
                    ApplySupplierConfigValues(config(configuration), supplier);
            }
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
                if (!ShopConfigByCode.TryGetValue(shopInterface.ShopCode, out ConfigRef<ShopListingSettings> config))
                    continue;

                AssertConfigurationEntries(ref config(configuration), shopInterface, listings);
                if (configuration.Enabled)
                    ApplyShopSettings(listings, shopInterface, config(configuration));
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
