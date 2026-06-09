# Products — Developer Notes

The `Products` module (formerly `ProductTooltips`) bundles two additive, read-only UI enhancements for
products. Config file: `UserData/Lithium/Products.json`. `Enabled` defaults to `true`.

> **Rename note:** this module was renamed from `ProductTooltips`. The config `Name` changed to
> `"Products"`, so a fresh `Products.json` is written on first run; an old `ProductTooltips.json` is left
> orphaned (harmless, can be deleted).

## ModProducts.cs

### Configuration fields
**Mix-recipe tooltips** (the original feature):
- **`Enabled`** — module master switch, defaults `true`.
- **`ShowMixRecipes`** — toggles the tooltip feature independently (default `true`). The tooltip patch is
  gated on `Enabled && ShowMixRecipes`.
- **`MixesHeader`** — header above the recipe list (auto-wrapped in `<b></b>`).
- **`Arrow`** — mixer→result separator (default `"→"`).
- **`Bullet`** — bullet prefix (default `"• "`).
- **`MaxLines`** — caps recipe rows. `0` = unlimited.
- **`FontSize`** — `0` = use the label's vanilla size.
- **`IconSize`** / **`RowHeight`** — per-row icon px / row px.

**Product list filter** (the new feature):
- **`EnableListFilter`** — toggles the search/effects bar on the phone Products app (default `true`).
- **`SearchPlaceholder`** — placeholder text in the search field.
- **`EffectsButtonLabel`** — base label of the effects multi-select button (a `(n)` count is appended).

### Apply()
Calls `ProductListFilter.Reset()` so the filter bar is rebuilt cleanly against the new
`ProductManagerApp` instance each time a save loads.

---

## Patches/ProductItemInfoContentPatch.cs
Unchanged from the original `ProductTooltips` module aside from the rename. Postfix on
`ItemInfoPanel.Open(ItemInstance, RectTransform)` that appends discovered forward mix recipes
(`[mixer] → [result] Name`) under the product hover panel. See the inline comments / the original design
for the anchor-below-effects logic, discovery gating, and idempotent rebuild via `_lastProduct`.

---

## Product list filter (new)

### What it does
Overlays a toolbar on the phone's **Products app** (`ProductManagerApp`):
1. A **search field** (top-left) — typing filters the list to products whose name contains the text
   (case-insensitive substring).
2. An **Effects** button (top-right) that toggles a scrollable multi-select panel. Selecting effects
   filters the list to products that contain **all** selected effects (AND). The button shows a `(n)`
   count; a **Clear** button resets the selection.

Both filters combine (name AND effects). Filtering hides/shows the existing `ProductEntry` GameObjects via
`SetActive`, so the vanilla layout groups collapse the hidden rows.

### Files
- **`ProductFilterUi.cs`** — stateless uGUI builder helpers (font resolve, image/text/button, scroll list,
  and `CloneSearchInput` which clones a vanilla `InputField`). Independent of the `PhoneApp` module on
  purpose (same proven patterns, no cross-module coupling).
- **`ProductListFilter.cs`** — static controller: builds the bar once per app instance, gathers the effect
  options, holds filter state (`_nameFilter`, `_selectedEffectIds`), and `ApplyFilter()`.
- **`Patches/ProductManagerAppFilterPatch.cs`** — Harmony patches wiring it in.

### Patch points (why these)
- **`ProductManagerApp.Start` (postfix)** — builds the bar after the app's own entries are created.
  `Start` is a MonoBehaviour lifecycle method (Unity must keep it callable → not inlined → reliable).
- **`ProductManagerApp.SetOpen(bool)` (postfix, open only)** — re-applies the filter when the app opens
  (also a safety net to build if `Start` was missed). Child active-states persist across the app's
  own `gameObject.SetActive` toggles, so this is mostly redundant but cheap.
- **`ProductManagerApp.CreateEntry(ProductDefinition)` (postfix)** — re-applies the filter when a product
  is discovered mid-session so the new entry respects the active filter. `ApplyFilter()` early-returns
  while the bar isn't built yet, so the Start-time entry storm is a no-op (the bar applies once after).

### Key game surfaces
- `ProductManagerApp.appContainer` (exposed by the IL2CPP wrapper as a public property even though it's a
  protected `[SerializeField]` on `App<T>`) is the visible panel root the bar/panel are parented to, so
  they show/hide with the app.
- Entries are read live from the public `FavouritesContainer.Container` + each
  `ProductTypeContainers[i].Container`'s children → `GetComponent<ProductEntry>()` → `.Definition`. No
  reliance on the app's private `entries`/`favouriteEntries` lists.
- Effect options come from `NetworkSingleton<ProductManager>.Instance.AllProducts` (the full product set,
  stable for the session), unioned over `ProductDefinition.Properties` (`List<Effect>`), keyed by
  `Effect.ID`, labelled by `Effect.Name`. Effect membership is tested by `Effect.ID` string match.

### Non-obvious bits
- The search field is **cloned** from `DetailPanel.ValueLabel` (a price `InputField`), so its
  `contentType` is reset to `Standard` / `characterValidation` to `None` — otherwise it would reject
  letters. The placeholder `Graphic` is cast to `Text` to set the prompt string.
- App-instance identity is tracked by `GetInstanceID()` (an int), not IL2CPP `==`, to decide when to
  rebuild for a new save.
- `Reset()` is called from `ModProducts.Apply()` (each `Main` scene load) and lazily when `EnsureBuilt`
  sees a new instance id.

### Known minor gaps
- Favouriting a product mid-session creates a favourites-list entry via the app's private
  `CreateFavouriteEntry` (not patched); it isn't re-filtered until the next `ApplyFilter` (any filter
  change, app re-open, or new discovery). The same product in the main type list is filtered normally.
- The per-type-container "None" placeholder uses the vanilla `childCount == 0` check, so it won't appear
  just because a section is fully filtered out (cosmetic only).
