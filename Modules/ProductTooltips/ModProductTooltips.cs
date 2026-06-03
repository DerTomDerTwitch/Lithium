namespace Lithium.Modules.ProductTooltips
{
    public class ModProductTooltipsConfiguration : ModuleConfiguration
    {
        public override string Name => "ProductTooltips";

        public ModProductTooltipsConfiguration()
        {
            // On by default: this is a purely additive, read-only UI enhancement. Set
            // "Enabled": false in ProductTooltips.json to restore vanilla tooltips.
            Enabled = true;
        }

        // Header printed above the list of mixes in the product info panel. Supports TextMeshPro
        // rich text (the value is wrapped in <b></b> automatically, so don't add bold tags here).
        public string MixesHeader { get; set; } = "Mix recipes:";

        // Separator drawn between the mixing ingredient and the resulting product, e.g. "Banana → Cola Cubes".
        public string Arrow { get; set; } = "→";

        // Bullet prefix for each recipe line. Set to "" for no bullet.
        public string Bullet { get; set; } = "• ";

        // Caps how many recipe lines shown (0 = unlimited). A product can only be combined with the
        // handful of valid mix ingredients, so this is just a safety bound for the panel size.
        public int MaxLines { get; set; } = 0;

        // Font size for the mixes block. 0 = leave the label's vanilla size. Lower (e.g. 11) fits more.
        public float FontSize { get; set; } = 0f;

        // Pixel size of the mixer/result icons in each row.
        public float IconSize { get; set; } = 24f;

        // Vertical pixel height of each recipe row.
        public float RowHeight { get; set; } = 28f;
    }

    /// <summary>
    /// Extends the product hover info panel (<c>ProductItemInfoContent</c>) with the list of mixing
    /// ingredients the player has already discovered that turn this product into another known product,
    /// e.g. "Banana → Cola Cubes". Only discovered mix recipes are shown — see
    /// <see cref="Patches.ProductItemInfoContentPatch"/>.
    /// </summary>
    public class ModProductTooltips : ModuleBase<ModProductTooltipsConfiguration>
    {
        public override void Apply()
        {
        }
    }
}
