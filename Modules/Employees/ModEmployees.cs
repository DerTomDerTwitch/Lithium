using Il2CppScheduleOne.Employees;

namespace Lithium.Modules.Employees
{
    public class BotanistConfiguration
    {
        public int MaxAssignedPots = 8;
        public float WalkSpeed = 1.2f;
        public int DailyWage = 200;
        public int InventorySlotCount = 5;
        public int InventoryRowCount = 1;
    }

    public class ChemistConfiguration
    {
        public int MaxStations = 6;
        public int DailyWage = 300;
        public float WalkSpeed = 1.2f;
        public int InventorySlotCount = 5;
        public int InventoryRowCount = 1;
    }

    public class PackagerConfiguration
    {
        public int MaxStations = 3;
        public int MaxRoutes = 10;
        public float PackagingSpeedMultiplier = 2f;
        public int DailyWage = 200;
        public float WalkSpeed = 1.2f;
        public int InventorySlotCount = 5;
        public int InventoryRowCount = 1;
    }

    public class CleanerConfiguration
    {
        public int MaxBins = 3;
        public int DailyWage = 100;
        public float WalkSpeed = 1.2f;
        public int InventorySlotCount = 5;
        public int InventoryRowCount = 1;
    }

    public class DealerConfiguration
    {
        public int InventorySlotCount = 5;
        public int InventoryRowCount = 1;
    }

    public class ModEmployeesConfiguration : ModuleConfiguration
    {
        public override string Name => "Employees";

        public bool PreventWorkStoppage = false;

        public BotanistConfiguration Botanists = new BotanistConfiguration();
        public ChemistConfiguration Chemists = new ChemistConfiguration();
        public PackagerConfiguration Packagers = new PackagerConfiguration();
        public CleanerConfiguration Cleaners = new CleanerConfiguration();
        public DealerConfiguration Dealers = new DealerConfiguration();
    }

    public class ModEmployees : ModuleBase<ModEmployeesConfiguration>
    {
        public static readonly HashSet<Employee> ConfiguredEmployees = [];

        public static bool TryBeginConfigure(Employee employee, out ModEmployeesConfiguration config)
        {
            config = null;
            ModEmployees mod = Core.Get<ModEmployees>();
            if (mod == null || !mod.Configuration.Enabled)
                return false;
            if (!ConfiguredEmployees.Add(employee))
                return false;
            config = mod.Configuration;
            return true;
        }

        public override void Apply()
        {
        }
    }
}
