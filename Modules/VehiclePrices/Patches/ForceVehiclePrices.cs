using HarmonyLib;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppTMPro;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Vehicles;
using Il2CppScheduleOne.Tools;
using Il2CppSystem.IO.Compression;
using Microsoft.VisualBasic;
using Unity.Collections;
using Lithium.Helper;

namespace Lithium.Modules.VehiclePrices.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.NetworkInitialize__Late))]
    public class ForceVehiclePrices
    {
        [HarmonyPrefix]
        public static void PatchPrices()
        {
            ModVehiclePricesConfiguration configuration = Core.Get<ModVehiclePrices>().Configuration;
            if (!configuration.Enabled)
                return;

            ChangeVehiclePrices(configuration);
            configuration.SaveConfiguration();
        }

        private static void ChangeVehiclePrices(ModVehiclePricesConfiguration configuration)
        {
            void UpdateSign(LandVehicle vehicle)
            {
                bool found = false;

                foreach (VehicleSaleSign sign in UnityEngine.Object.FindObjectsOfType<VehicleSaleSign>())
                {
                    if (sign.NameLabel.text == vehicle.vehicleName)
                    {
                        found = true;
                        sign.transform.Find("Price").GetComponent<TextMeshPro>().text =
                            MoneyManager.FormatAmount(vehicle.vehiclePrice);
                        break;
                    }
                }
                if (!found)
                {
                    MelonLoader.MelonLogger.Warning($"Vehicle Sale Sign for {vehicle.vehicleName} not found in game.");
                }
            }

            VehicleManager manager = UnityEngine.Object.FindObjectOfType<VehicleManager>();
            foreach (LandVehicle vehicle in manager.VehiclePrefabs)
            {
                if (configuration.VehiclePrices.TryGetValue(vehicle.vehicleName, out int price))
                {
                    vehicle.vehiclePrice = price;
                    UpdateSign(vehicle);
                }
                else
                {
                    MelonLoader.MelonLogger.Warning($"Vehicle {vehicle.vehicleName} not found in configuration. Skipping.");
                    continue;
                }
            }
        }
    }
}