# Modules/Shops — Notes

Extracted from comments in the source files. Documents intent, non-obvious design decisions, magic numbers, and gotchas.

---

## ModShops.cs

### `IRankRequirement` interface
Rank gating lives on the **global item definition** (`StorableItemDefinition.RequiresLevelToPurchase` / `RequiredRank`), not on the shop listing. It is therefore applied by item ID via the Registry — independent of whether the selling shop's GameObject is discovered.

Three-state `RequiresRank` semantics:
- `null` — leave the item's vanilla rank gating untouched (default; also what existing configs deserialize to, so upgrading never silently strips requirements).
- `false` — lift the requirement entirely (item purchasable from the start).
- `true` — gate the item behind `RequiredRank` + `RequiredRankTier`.

### `ItemListingOverride.Price`
Negative value = leave the item's native price untouched. Useful for rank-only overrides where you don't want to change the price.

### `AddedListing`
Represents a brand-new listing injected into a shop that doesn't sell the item natively (e.g. putting the brick press on Oscar's Dark Market). Keyed by item ID in `ShopListingSettings.AddedItems`. Requires `Override = true` on the shop.

`Price = -1` (default) = use the item's own base purchase price (the usual choice).

`RequiresRank` semantics are identical to `ItemListingOverride.RequiresRank` (`null` / `false` / `true`).

---

## SupplierStartPatch.cs

### Patch target
`[HarmonyPatch(typeof(Player), nameof(Player.NetworkInitialize__Late))]` — fires on player network init (i.e. when a save loads and the game world is live).

### `ConfigRef<T>` delegate
A by-ref accessor to one of the config's named override fields. Lets a single loop both auto-populate (`??= new()`, writing the field back) and read each entry — replacing what would otherwise be an 11-case shop switch and 4× copy-pasted supplier block.

### `ShopConfigByCode`
Maps each in-game shop code string to its config field:
- `"thrifty_threads"` → `ThriftyThreads`
- `"coke_shop"` → `CokeSupplier`
- `"meth_shop"` → `MethSupplier`
- `"weed_shop"` → `WeedSupplier`
- `"shrooms_shop"` → `ShroomSupplier`
- `"boutique"` → `Boutique`
- `"dark_market_shop"` → `DarkMarket`
- `"gas_mart_west"` → `GasStation`
- `"gas_mart_central"` → `CentralGasStation`
- `"dans_hardware"` → `DansHardware`
- `"handy_hanks"` → `HandyHanks`

### `Suppliers` array
Each entry is a `(Func<Supplier> Find, ConfigRef<SupplierListingOverride> Config)` tuple: how to find the live NPC via `FindObjectOfType`, and which config field it maps to (Albert, Shirley, Salvador, Phil).

### `PatchPrices` — ordering of rank vs. shop overrides
Rank gating is applied **first** (before `ApplyShopOverrides`), because rank is global on the item definition. This means `RefreshUnlockStatus` called inside `ApplyShopOverrides` will already reflect the lifted/changed requirements for any shop discovered in that pass.

### Population vs. application — always populate, conditionally apply
Config is always populated with live shop/supplier values (so the user has a ready-to-edit template) even when the module or a shop's `Override` flag is disabled. The two gating steps are independent: populate always runs; apply only runs when `Enabled`. This ensures the JSON template is written on first load.

### `AssertConfigurationEntries` — auto-seeding rank fields
When a new `ItemListingOverride` is auto-created for a listing, the `RequiresRank`, `RequiredRank`, and `RequiredRankTier` fields are seeded from the item's live values. This makes the JSON show real values to edit while leaving them effectively a no-op until the user changes them.

A freshly-created `ShopListingSettings` entry adopts the shop's real `PaymentType` only when `ItemOverrides.Count == 0` (i.e. first population).

### `ApplyAddedListings` — category derivation
Added listings carry no explicit category. The shop derives the tab from the item's own `ShopCategories`, so the added item appears under its natural category (and "All").

### `ApplyAddedListings` — double-add guard
If the shop already lists an item (natively or from a previous load this session), the item is skipped; tweaks for it belong in `ItemOverrides` instead.

### `ApplyShopSettings` — negative price handling
A negative price in `ItemListingOverride.Price` means "leave the item's native price alone." Auto-populated entries always carry the real price, so this only triggers when the user explicitly sets it negative (e.g. for a rank-only override like lifting the brick press requirement without knowing its base price).

### `ApplyItemRankOverrides` — decoupled from shop discovery
Rank overrides are applied via the Registry by item ID, so they work even when the selling shop's GameObject is not discovered at load time (e.g. Oscar's Dark Market). Iterates all shops in `ShopConfigByCode`, skipping those with `Override = false` or null settings. Processes both `ItemOverrides` and `AddedItems`.

---

## DeliveryShopFeePatch.cs

### Patch target
`[HarmonyPatch(typeof(DeliveryShop), "GetDeliveryFee")]` — postfix that overrides the delivery fee result.

### Logic
Looks up the shop by `__instance.MatchingShop.ShopName` in `configuration.Deliveries`. Only overrides the fee when the shop is found **and** `Availability != Unchanged`. Early-exits if the module is disabled, deliveries dict is null, or `MatchingShop` is null.

---

## ForceUpdateShopPrices.cs

### Patch target
`[HarmonyPatch(typeof(ShopInterface), nameof(ShopInterface.SetIsOpen))]` — postfix on shop open/close.

### Purpose
After `SetIsOpen`, iterates all `ListingUI` entries on the shop and calls `UpdateLockStatus`, `Update`, `UpdatePrice`, `UpdateStock`, and `UpdateButtons` on each. Forces the UI to reflect any price/stock/rank changes applied by Lithium at load time, ensuring the shop display is correct when the player first opens it.

No notes — no unusual comments beyond the patch registration.
