using HarmonyLib;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
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
        private delegate ref T ConfigRef<T>(ModShopsConfiguration configuration);

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

            if (configuration.Enabled)
                ApplyItemRankOverrides(configuration);

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
                {
                    ApplyShopSettings(listings, shopInterface, config(configuration));
                    ApplyAddedListings(shopInterface, config(configuration));
                }
            }
        }

        private static void ApplyAddedListings(ShopInterface shopInterface, ShopListingSettings shopSettings)
        {
            if (!shopSettings.Override || shopSettings.AddedItems == null || shopSettings.AddedItems.Count == 0)
                return;

            bool added = false;
            foreach (KeyValuePair<string, AddedListing> entry in shopSettings.AddedItems)
            {
                string itemId = entry.Key;
                AddedListing spec = entry.Value;

                if (shopInterface.GetListing(itemId) != null)
                    continue;

                StorableItemDefinition item = Registry.GetItem<StorableItemDefinition>(itemId);
                if (item == null)
                {
                    Log.Warning($"[Shops] Cannot add '{itemId}' to '{shopInterface.ShopCode}': " +
                        "no such item in the registry. Check the item ID.");
                    continue;
                }

                try
                {
                    ShopListing listing = new ShopListing
                    {
                        name = item.Name,
                        Item = item,
                        Shop = shopInterface,
                        LimitedStock = spec.Stock >= 0,
                        DefaultStock = spec.Stock >= 0 ? spec.Stock : shopSettings.DefaultStock,
                        RestockRate = spec.RestockRate,
                    };
                    listing.CurrentStock = listing.DefaultStock;

                    if (spec.Price >= 0f)
                    {
                        listing.OverridePrice = true;
                        listing.OverriddenPrice = spec.Price;
                    }

                    shopInterface.Listings.Add(listing);
                    shopInterface.CreateListingUI(listing);
                    added = true;
                }
                catch (Exception ex)
                {
                    Log.Error($"[Shops] Failed to add '{itemId}' to '{shopInterface.ShopCode}': {ex.Message}");
                }
            }

            if (added)
            {
                shopInterface.RefreshShownItems();
                shopInterface.RefreshUnlockStatus();
            }
        }

        private static void AssertConfigurationEntries(ref ShopListingSettings configSetting,
            ShopInterface shopInterface, List<ShopListing> listings)
        {
            configSetting ??= new ShopListingSettings();

            if (configSetting.ItemOverrides.Count == 0)
                configSetting.PaymentType = shopInterface.PaymentType;

            foreach (ShopListing listing in listings)
            {
                if (configSetting.ItemOverrides.ContainsKey(listing.Item.ID))
                    continue;

                FullRank requiredRank = listing.Item.RequiredRank;
                configSetting.ItemOverrides[listing.Item.ID] = new ItemListingOverride
                {
                    Price = listing.Price,
                    Stock = listing.LimitedStock ? listing.DefaultStock : -1,
                    RestockRate = listing.RestockRate,
                    RequiresRank = listing.Item.RequiresLevelToPurchase,
                    RequiredRank = requiredRank.Rank,
                    RequiredRankTier = requiredRank.Tier,
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
                listing.RestockRate = overrideItem.RestockRate;

                if (overrideItem.Price >= 0f)
                {
                    listing.OverridePrice = true;
                    listing.OverriddenPrice = overrideItem.Price;
                }

            }
        }

        private static void ApplyItemRankOverrides(ModShopsConfiguration configuration)
        {
            foreach (ConfigRef<ShopListingSettings> configRef in ShopConfigByCode.Values)
            {
                ShopListingSettings shop = configRef(configuration);
                if (shop == null || !shop.Override)
                    continue;

                if (shop.ItemOverrides != null)
                    foreach (KeyValuePair<string, ItemListingOverride> entry in shop.ItemOverrides)
                        ApplyItemRank(entry.Key, entry.Value);

                if (shop.AddedItems != null)
                    foreach (KeyValuePair<string, AddedListing> entry in shop.AddedItems)
                        ApplyItemRank(entry.Key, entry.Value);
            }
        }

        private static void ApplyItemRank(string itemId, IRankRequirement spec)
        {
            if (spec == null || !spec.RequiresRank.HasValue)
                return;

            StorableItemDefinition item = Registry.GetItem<StorableItemDefinition>(itemId);
            if (item == null)
            {
                Log.Warning($"[Shops] Cannot set rank requirement for '{itemId}': not found in the registry.");
                return;
            }

            item.RequiresLevelToPurchase = spec.RequiresRank.Value;
            FullRank required = item.RequiredRank;
            required.Rank = spec.RequiredRank;
            required.Tier = spec.RequiredRankTier;
            item.RequiredRank = required;

            if (Log.DebugEnabled)
                Log.Info($"[Shops] '{itemId}' rank requirement set: RequiresLevel={spec.RequiresRank.Value}, " +
                    $"Rank={spec.RequiredRank} Tier={spec.RequiredRankTier}");
        }
    }
}
