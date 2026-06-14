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
using Lithium.Modules.Products;
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

[assembly: MelonInfo(typeof(Lithium.Core), "Lithium", "1.3.0", "DerTomDer & YukiSora", null)]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]
// Soft dependency: the in-game debug menu (LithiumModMenu) is hosted by the "Mod Manager & Phone App"
// mod. Declaring it optional makes MelonLoader load that mod first when present, so our reflection hook
// resolves reliably — but Lithium (and all its gameplay modules) still load if it is absent.
[assembly: MelonOptionalDependencies("ModManager&PhoneApp")]
                                                                              
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
            new ModProducts(),
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

            // Surface the debug toggle + authoring actions in the in-game Mod Manager app.
            LithiumModMenu.Initialize();

            Log.Info("Lithium initialized");
        }

        private bool _isFirstStart = true;

        /// <summary>True while a save is loaded (scene == "Main"). Read by LithiumModMenu actions.</summary>
        public static bool IsInMainScene { get; private set; }

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
                IsInMainScene = true;
            }
            else if (sceneName.Equals("Menu", StringComparison.OrdinalIgnoreCase) && !_isFirstStart)
            {
                _isFirstStart = true;
                IsInMainScene = false;
            }
        }

        /// <summary>Invoked by MelonLoader whenever MelonPreferences are saved (e.g. the Mod Manager app's Save button).</summary>
        public override void OnPreferencesSaved() => LithiumModMenu.OnPreferencesSaved();

        public static void ReloadConfiguration()
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

            if (IsInMainScene)
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

            Log.Warning(IsInMainScene
                ? "[Lithium] Configuration reloaded and reapplied."
                : "[Lithium] Configuration reloaded (no save loaded — runtime reapply skipped).");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            Get<ModPhoneApp>()?.DriveUpdate();
            Get<ModPolice>()?.DriveUpdate();
            Get<ModElectricBill>()?.DriveUpdate();
            Get<ModRent>()?.DriveUpdate();
            Get<ModProducts>()?.DriveUpdate();
        }
    }
}
