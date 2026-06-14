namespace Lithium.Modules.Products
{
    public class ModProductsConfiguration : ModuleConfiguration
    {
        public override string Name => "Products";

        public ModProductsConfiguration()
        {
            Enabled = true;
        }

        // --- Mix-recipe tooltips (formerly the ProductTooltips module) -------------------------------
        public bool ShowMixRecipes { get; set; } = true;

        public string MixesHeader { get; set; } = "Mix recipes:";

        public string Arrow { get; set; } = "→";

        public string Bullet { get; set; } = "• ";

        public int MaxLines { get; set; } = 0;

        public float FontSize { get; set; } = 0f;

        public float IconSize { get; set; } = 24f;

        public float RowHeight { get; set; } = 28f;

        // --- Product Manager app filtering ----------------------------------------------------------
        // Adds a search field + an effects multi-select to the phone's Products app, letting the player
        // filter the discovered-product list by name and by required effects.
        public bool EnableListFilter { get; set; } = true;

        public string SearchPlaceholder { get; set; } = "Search products...";

        public string EffectsButtonLabel { get; set; } = "Effects";
    }

    public class ModProducts : ModuleBase<ModProductsConfiguration>
    {
        public override void Apply()
        {
            // The product list UI is rebuilt per-save by the game; drop any stale references so the
            // filter bar is reconstructed cleanly against the new ProductManagerApp instance.
            ProductListFilter.Reset();
        }

        // Forwarded from Core.OnUpdate: keep the Products-app filter bar hidden unless that app is open.
        public void DriveUpdate()
        {
            ProductListFilter.DriveVisibility();
        }
    }
}
