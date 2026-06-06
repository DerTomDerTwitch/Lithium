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
    /// On break the game seeds <c>DaysUntilRepair = 0</c> and <c>DayPass</c> then decrements it once per
    /// night, repairing at <c>&lt;= 0</c> — so vanilla repairs after the first sleep. We reimplement
    /// <c>DayPass</c> in a prefix (see Patches/) as an up-counter: while broken, <c>DaysUntilRepair</c>
    /// counts the nights since the break and we repair once it reaches <c>RepairDays</c>
    /// (<c>1</c> reproduces vanilla, higher values add extra nights). We patch <c>DayPass</c> rather
    /// than the break because it is delegate-bound (un-inlinable, so reliably patchable in IL2CPP) and
    /// because reusing the field the game already persists lets an in-progress window survive save/load.
    /// </summary>
    public class ModRepairs : ModuleBase<ModRepairsConfiguration>
    {
        public override void Apply()
        {
        }
    }
}
