namespace Lithium.Modules.ProductTooltips
{
    public class ModProductTooltipsConfiguration : ModuleConfiguration
    {
        public override string Name => "ProductTooltips";

        public ModProductTooltipsConfiguration()
        {
            Enabled = true;
        }

        public string MixesHeader { get; set; } = "Mix recipes:";

        public string Arrow { get; set; } = "→";

        public string Bullet { get; set; } = "• ";

        public int MaxLines { get; set; } = 0;

        public float FontSize { get; set; } = 0f;

        public float IconSize { get; set; } = 24f;

        public float RowHeight { get; set; } = 28f;
    }

    public class ModProductTooltips : ModuleBase<ModProductTooltipsConfiguration>
    {
        public override void Apply()
        {
        }
    }
}
