using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Vehicles;
using Lithium.Helper;
using Lithium.Modules;
using Lithium.Modules.Banking;
using Lithium.Modules.BrickPress;
using Lithium.Modules.ChemistryStation;
using Lithium.Modules.Customers;
using Lithium.Modules.Dealers;
using Lithium.Modules.DryingRacks;
using Lithium.Modules.EffectCombos;
using Lithium.Modules.ElectricBill;
using Lithium.Modules.Employees;
using Lithium.Modules.EndOfDayFreeze;
using Lithium.Modules.LabOven;
using Lithium.Modules.MixingStations;
using Lithium.Modules.PhoneApp;
using Lithium.Modules.PlantGrowth;
using Lithium.Modules.Police;
using Lithium.Modules.ProductTooltips;
using Lithium.Modules.PropertyPrices;
using Lithium.Modules.Rent;
using Lithium.Modules.Repairs;
using Lithium.Modules.Shops;
using Lithium.Modules.StackSizes;
using Lithium.Modules.Storyline;
using Lithium.Modules.TrashGrabber;
using Lithium.Modules.Vehicles;
using Lithium.Modules.Warehouse;
using Lithium.Modules.WateringCans;
using Lithium.Modules.Weapons;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(Lithium.Core), "Lithium", "1.3.0", "DerTomDer & YukiSora", null)]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]
                                                                              
namespace Lithium
{
    public class Core : MelonMod
    {
        public static readonly List<ModuleBase> Modules = 
        [
            new ModPropertyPrices(),
            new ModPlants(),
            new ModDryingRacks(),
            new ModCustomers(),
            new ModStackSizes(),
            new ModLabOven(),
            new ModTrashGrabber(),
            new ModMixingStations(),
            new ModStoryline(),
            new ModShops(),
            new ModEmployees(),
            new ModChemistryStation(),
            new ModWateringCan(),
            new ModEffectCombos(),
            new ModVehicles(),
            new ModEndOfDayFreeze(),
            new ModProductTooltips(),
            new ModBanking(),
            new ModElectricBill(),
            new ModRent(),
            new ModBrickPress(),
            new ModDealers(),
            new ModRepairs(),
            new ModWeapons(),
            new ModWarehouse(),
            new ModPolice(),
            new ModPhoneApp()
        ];

        public static T Get<T>() where T : ModuleBase => Modules.OfType<T>().FirstOrDefault();

        public static MelonLogger.Instance Logger { get; set; }
    
        public override void OnInitializeMelon()
        {
            Logger = LoggerInstance;
            LithiumConfig.Load();

            foreach (ModuleBase module in Modules)
            {
                Log.Info($"Loading {module.GetType().Name}");
                module.Load();
            }

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("com.lithium");
            harmony.PatchAll();

            Log.Info("Lithium initialized");
        }

        private bool _isFirstStart = true;

        private bool _sceneIsMain;

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (sceneName == "Main")
            {
                foreach (ModuleBase module in Modules)
                {
                    Log.Info($"Loading {module.GetType().Name}");
                    module.Apply();
                }

                _isFirstStart = false;
                _sceneIsMain = true;
            }
            else if (sceneName.Equals("Menu", StringComparison.OrdinalIgnoreCase) && !_isFirstStart)
            {
                _isFirstStart = true;
                _sceneIsMain = false;
            }
        }

        public void ReloadConfiguration()
        {
            Log.Warning("[Lithium] Reloading all configuration...");

            LithiumConfig.Load();

            foreach (ModuleBase module in Modules)
            {
                try
                {
                    module.Load();
                }
                catch (Exception e)
                {
                    Log.Error($"[Lithium] {module.GetType().Name}.Load() failed during reload: {e}");
                }
            }

            if (_sceneIsMain)
            {
                foreach (ModuleBase module in Modules)
                {
                    try
                    {
                        module.Apply();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[Lithium] {module.GetType().Name}.Apply() failed during reload: {e}");
                    }
                }
            }

            Log.Warning(_sceneIsMain
                ? "[Lithium] Configuration reloaded and reapplied."
                : "[Lithium] Configuration reloaded (no save loaded — runtime reapply skipped).");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            Get<ModPhoneApp>()?.DriveUpdate();
            Get<ModPolice>()?.DriveUpdate();

            LithiumConfig config = LithiumConfig.Instance;

            if (Input.GetKeyDown(KeyCode.F8))
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (ctrl && shift)
                {
                    if (config.HotkeyCtrlShiftF8ReloadConfig)
                        ReloadConfiguration();
                }
                else if (config.HotkeyF8RentDump)
                {
                    RentDebug.Dump();
                }
            }

            if (config.HotkeyF9BuildablesDump && Input.GetKeyDown(KeyCode.F9))
            {
                BuildablesDebug.Dump();
            }

            if (config.HotkeyF10RVFurnitureDump && Input.GetKeyDown(KeyCode.F10))
            {
                RVFurnitureDebug.Dump();
            }

            if (config.HotkeyF5ExpandVeeperStorage && Input.GetKeyDown(KeyCode.F5))
            {
                LandVehicle[] array2 = VehicleManager.Instance.AllVehicles.ToArray()
                    .Where(v => v.IsPlayerOwned)
                    .Where(v => v.VehicleCode == "veeper")
                    .ToArray();
                foreach (LandVehicle vehicle in array2)
                {
                    vehicle.Storage.SlotCount = 20;
                    for (int i = vehicle.Storage.ItemSlots.Count; i <= vehicle.Storage.SlotCount; i++)
                    {
                        vehicle.Storage.ItemSlots.Add(new());
                    }
                }
            }

            if (config.HotkeyF6OrderPatternDump && Input.GetKeyDown(KeyCode.F6))
            {
                OrderPatternDebug.Dump();
            }

            if (config.HotkeyF7NpcRosterDump && Input.GetKeyDown(KeyCode.F7))
            {
                NpcRosterDebug.Dump();
            }
        }
    }
}
