using Il2CppScheduleOne.Core.Items.Framework;

namespace Lithium.Modules.StackSizes
{
    public class ModStackSizesConfiguration : ModuleConfiguration
    {
        public override string Name => "StackSizes";

        /// <summary>
        /// EXPERIMENTAL — off by default. The game hard-caps a cash stack at $1000 via the native
        /// constant CashInstance.MAX_BALANCE, which cannot be written at runtime. Enabling this makes
        /// Lithium reimplement the cash balance/drag clamps to use <see cref="CashMaxBalance"/> instead.
        /// Because the native money-transfer logic can't be fully inspected, test on a BACKUP save and
        /// watch your total money — a discrepancy means you should turn this back off.
        /// </summary>
        public bool ExperimentalCashStacking { get; set; } = false;

        /// <summary>
        /// Maximum money a single cash stack can hold when <see cref="ExperimentalCashStacking"/> is on.
        /// </summary>
        public int CashMaxBalance { get; set; } = 100000;

        public Dictionary<EItemCategory, int> CategorySizes { get; set; } = new Dictionary<EItemCategory, int>
        {
            { EItemCategory.Product, 20 },
            { EItemCategory.Packaging, 20 },
            { EItemCategory.Agriculture, 20 },
            { EItemCategory.Tools, 10 },
            { EItemCategory.Furniture, 10 },
            { EItemCategory.Lighting, 10 },
            { EItemCategory.Cash, 1000 },
            { EItemCategory.Consumable, 20 },
            { EItemCategory.Equipment, 20 },
            { EItemCategory.Ingredient, 20 },
            { EItemCategory.Decoration, 10 },
            { EItemCategory.Clothing, 10 },
            { EItemCategory.Storage, 10 },
        };

        public Dictionary<string, int> ItemOverrides { get; set; } = [];
        public List<string> IgnoredItems { get; set; } = [];
    }

    public class ModStackSizes : ModuleBase<ModStackSizesConfiguration>
    {
        public override void Apply()
        {
            // The cash balance cap (CashInstance.MAX_BALANCE) is applied from CashInstanceMaxBalancePatch
            // instead of here: writing that static field from an un-initialised-class context (scene load
            // or Registry.Start) throws an uncatchable AccessViolation, so it must be set from inside a
            // live CashInstance method where the il2cpp class is guaranteed to be initialised.
            if (!Configuration.Enabled)
                return;
        }

    }
}
