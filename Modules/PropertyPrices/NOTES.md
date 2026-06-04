# PropertyPrices Module — Notes

## ModPropertyPrices.cs

No comments in this file. The configuration class (`ModPropertyPricesConfiguration`) holds a `Dictionary<string, int>` named `PropertyPrices` keyed by the in-game display name of each property/business. The default values are:

| Property / Business | Default Price |
|---|---|
| RV | 0 |
| Sewer Office | 0 |
| Motel Room | 75 |
| Sweatshop | 800 |
| Storage Unit | 5 000 |
| Bungalow | 6 000 |
| Barn | 25 000 |
| Docks Warehouse | 50 000 |
| Hyland Manor | 100 000 |
| Laundromat | 4 000 |
| Post Office | 10 000 |
| Car Wash | 20 000 |
| Taco Ticklers | 50 000 |

The `Apply()` override does nothing beyond the enabled guard — all runtime logic is in the patch.

---

## Patches/ForcePropertyPrices.cs

**Patched method:** `Player.NetworkInitialize___Early` (Harmony prefix).

**Why this method:** It fires early in the networking/player-init lifecycle, when `Property` objects already exist in the scene but before prices are meaningfully consumed by UI or purchase logic. This makes it a reliable point to override prices and refresh the for-sale signage in one shot.

**What the patch does:**

1. `ChangePropertyPrices` — iterates every `Property` object in the scene via `FindObjectsOfType<Property>()`.
   - Looks up `prop.PropertyName` in the config dictionary.
   - **Unknown/new property handling:** if a property is not in the dictionary (e.g. added by a game update), it reads the live price from `prop.Price`, seeds it into the config dictionary under that name, and logs it. `PatchPrices` then calls `configuration.SaveConfiguration()` after both helpers run, so the new entry is persisted to `PropertyPrices.json` for the user to edit on the next run. This prevents newly added properties from being silently skipped.
   - Sets `prop.Price` to the resolved price.
   - Updates TextMeshPro `Price` label on both `prop.ForSaleSign` and `prop.ListingPoster` (null-checked) using `MoneyManager.FormatAmount`.

2. `UpdateMissMingDialog` — finds all `DialogueController_Ming` components in the scene. Miss Ming's dialogue contains hardcoded price references that must be kept in sync with the overridden property prices. The patch resolves which NPC is which by the parent GameObject name:
   - Parent named `"Ming"` → `Sweatshop` price.
   - Parent named `"Donna"` → `Motel Room` price.
   - Any other parent name → leave `item.Price` unchanged.

3. After both helpers, `configuration.SaveConfiguration()` is called to persist any newly discovered properties.
