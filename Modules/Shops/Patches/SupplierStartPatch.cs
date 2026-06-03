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

            // Rank gating is global (on the item definition), so apply it first and by item ID via the
            // Registry — this works even for shops whose GameObject we don't discover (e.g. Oscar's Dark
            // Market). Doing it before ApplyShopOverrides means the per-shop RefreshUnlockStatus below
            // reflects the lifted/changed requirements for any shop we do find.
            if (configuration.Enabled)
                ApplyItemRankOverrides(configuration);

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
                {
                    ApplyShopSettings(listings, shopInterface, config(configuration));
                    ApplyAddedListings(shopInterface, config(configuration));
                }
            }
        }

        // Injects listings for items the shop doesn't natively sell (config: AddedItems). Gated behind the
        // shop's Override flag, like the rest of the active overrides.
        private static void ApplyAddedListings(ShopInterface shopInterface, ShopListingSettings shopSettings)
        {
            if (!shopSettings.Override || shopSettings.AddedItems == null || shopSettings.AddedItems.Count == 0)
                return;

            bool added = false;
            foreach (KeyValuePair<string, AddedListing> entry in shopSettings.AddedItems)
            {
                string itemId = entry.Key;
                AddedListing spec = entry.Value;

                // Don't double-add: if the shop already lists it (natively or from a previous load this
                // session), tweaks belong in ItemOverrides instead.
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
                    // The listing carries no category itself — the shop derives the tab from the item's own
                    // ShopCategories, so the added item shows under its natural category (and "All").
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

                    // Negative price = keep the item's own base price (see ItemListingOverride.Price).
                    if (spec.Price >= 0f)
                    {
                        listing.OverridePrice = true;
                        listing.OverriddenPrice = spec.Price;
                    }

                    // Rank gating for the item is applied globally in ApplyItemRankOverrides.

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

                FullRank requiredRank = listing.Item.RequiredRank;
                configSetting.ItemOverrides[listing.Item.ID] = new ItemListingOverride
                {
                    Price = listing.Price,
                    Stock = listing.LimitedStock ? listing.DefaultStock : -1,
                    RestockRate = listing.RestockRate,
                    // Seed the rank fields with the item's live gating so the JSON shows real values to
                    // edit, while leaving them effectively a no-op until the user changes them.
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

                // A negative price means "leave the item's native price alone" — useful for rank-only
                // overrides (e.g. lifting the brick press requirement without knowing its base price).
                // Auto-populated entries always carry the real price, so this only triggers when the user
                // explicitly sets it negative.
                if (overrideItem.Price >= 0f)
                {
                    listing.OverridePrice = true;
                    listing.OverriddenPrice = overrideItem.Price;
                }

                // Rank gating is applied globally in ApplyItemRankOverrides (it lives on the shared item
                // definition, not the listing), so it is intentionally not handled here.
            }
        }

        // Applies rank-requirement overrides directly to item definitions via the Registry, for every
        // opted-in (RequiresRank != null) entry in any active (Override) shop's ItemOverrides or AddedItems.
        // Decoupled from shop discovery so it works even when the selling shop's GameObject isn't found.
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
