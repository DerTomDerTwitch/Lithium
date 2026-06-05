namespace Lithium.Modules.Repairs
{
    public class ModRepairsConfiguration : ModuleConfiguration
    {
        public override string Name => "Repairs";

        /// <summary>
        /// How many in-game days a destroyed ATM / cuke vending machine stays broken
        /// before it auto-repairs. Vanilla is 1. Applies to both machine types.
        /// </summary>
        public int RepairDays = 1;

        public override void Validate()
        {
            if (RepairDays < 1)
                RepairDays = 1;
        }
    }

    /// <summary>
    /// Prolongs the repair period of destructible ATMs and cuke vending machines.
    /// On break the game runs <c>RpcLogic___SendBreak</c> which seeds <c>DaysUntilRepair = 0</c>;
    /// <c>DayPass</c> then decrements it once per night and repairs at <c>&lt;= 0</c> — so vanilla
    /// repairs after the first sleep. We overwrite that seed with <c>RepairDays</c> in a postfix
    /// (see Patches/); <c>RepairDays = 1</c> reproduces vanilla, higher values add extra nights.
    /// </summary>
    public class ModRepairs : ModuleBase<ModRepairsConfiguration>
    {
        public override void Apply()
        {
        }
    }
}
