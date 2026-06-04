# ProductTooltips — Developer Notes

## ModProductTooltips.cs

### What it does
Defines the module configuration (`ModProductTooltipsConfiguration`) and the module class (`ModProductTooltips`). The module is a purely additive, read-only UI enhancement — it appends discovered mix recipes to the product hover info panel. `Apply()` is intentionally empty; all work happens in the Harmony patch.

### Configuration fields
- **`Enabled`** — defaults to `true` (unlike most Lithium modules). Set `"Enabled": false` in `ProductTooltips.json` to restore vanilla tooltips.
- **`MixesHeader`** — header string printed above the recipe list. The value is automatically wrapped in `<b></b>` at render time, so bold tags must not be added in the config.
- **`Arrow`** — separator between mixer ingredient and result, e.g. `"→"`. Default `"→"`.
- **`Bullet`** — bullet prefix for each recipe line. Set to `""` for no bullet. Default `"• "`.
- **`MaxLines`** — caps how many recipe lines are shown. `0` = unlimited. Safety bound for panel size; a product can only combine with a handful of valid mix ingredients.
- **`FontSize`** — font size for the mixes block. `0` = use the label's vanilla size. Lowering to e.g. `11` fits more lines.
- **`IconSize`** — pixel size of the mixer/result icons per row. Default `24f`.
- **`RowHeight`** — vertical pixel height of each recipe row. Default `28f`.

---

## Patches/ProductItemInfoContentPatch.cs

### What it does
Postfix on `ItemInfoPanel.Open(ItemInstance, RectTransform)`. After the vanilla panel opens for a hovered item, this patch appends a block of icon rows — `[mixer icon] [arrow] [result icon] Result Name` — listing all forward mix recipes for the product that the player has already discovered.

### Patched game method
`ItemInfoPanel.Open` (overload taking `ItemInstance` + `RectTransform`).

### Panel layout investigation (tooltip-diag.log)
- The product info panel uses **purely manual layout** — no `ContentSizeFitter` or `LayoutGroup`. The container is sized from `ItemInfoContent.Height`.
- `ItemInfoPanel.Open` fires **more than once per hover** on a pooled content instance. To stay idempotent: (a) the last displayed product ID is cached per content `instanceId` in `_lastProduct`; (b) rows are only rebuilt when the product actually changes.
- All injected rows live under a single child `GameObject` named `"LithiumMixes"` so they can be found and cleared in one `transform.Find` call.

### Anchor position logic (non-obvious)
The mixes block is anchored below the **lowest active, non-empty effect label** (`MeasureEffectsBottom`), not below the panel's fixed height. Reason: the vanilla panel pre-allocates space for all possible effect slots, so a product with few effects would leave a large gap above the injected block if the panel height were used naively. `MeasureEffectsBottom` iterates `productContent.PropertyLabels`, skips inactive/empty ones, and converts the world-space bottom of the lowest label to content-local units via `lossyScale.y`.

### Discovery gate
Only recipes whose **result** the player has already discovered are shown, sourced from `ProductManager.DiscoveredProducts`. This prevents the tooltip from spoiling products the player has never made.

### Recipe resolution
- Mix-ingredient identities are resolved from `ProductManager.ValidMixIngredients` (a registered list of `PropertyItemDefinition`s). Their IDs are collected into `mixerIds`.
- For each `StationRecipe` in `ProductManager.mixRecipes`, the code splits ingredients into "the mixer ingredient" (first one whose ID is in `mixerIds`) and "the base product" (everything else). A recipe matches the hovered product when `baseItem.ID == product.ID`.
- Recipes where the output is the same product as the input are skipped (self-loops).

### Style template
The vanilla `DescriptionLabel` on `ProductItemInfoContent` is cloned via `Instantiate` as the text style template (font, color, material). The label is always hidden for products in the vanilla UI, making it a safe style source.

### Constants / magic numbers
- `RootName = "LithiumMixes"` — the `GameObject` name used as an anchor for find/clear.
- `TopMargin = 4f` — pixel gap between the effects block and the injected mixes block.
- `HeaderHeight = 16f` — height reserved for the header row.
- `LeftPad = 6f` — left indent for all rows.
- `arrowW = 12f` — width of the arrow text element.
- `gap = 2f` — horizontal spacing between icon and arrow/text elements.
- `scaleY > 0.0001f` — guard against near-zero scale before dividing in world-to-local conversion.
- `rowHeight` defaults to `28f` and `iconSize` defaults to `24f` if config values are zero or negative.
- `corners[1].y` — top-left world Y of the content rect (Unity `GetWorldCorners` returns corners in order: bottom-left, top-left, top-right, bottom-right).
- `Mathf.Min(corners[0].y, corners[3].y)` — bottom Y of a label rect (min of bottom-left and bottom-right corners, robust to rotation).

### Gotchas
- The patch is `internal static` and decorated with `[HarmonyPatch]` directly; no explicit `[HarmonyPatch]` on individual methods — the attribute is on the class and `[HarmonyPostfix]` is on the `Postfix` method.
- `TryCast<ProductItemInfoContent>()` is used (IL2CPP interop cast) rather than `as`, because IL2CPP types do not support C# `as` casts across the managed/native boundary.
- Errors are caught and logged via `Log.Error` so a tooltip failure is never fatal.
- `_lastProduct` is a static `Dictionary<int, string>` keyed by content `instanceId`; it never shrinks (pooled objects reuse the same IDs), which is fine since the pool is small and bounded.
