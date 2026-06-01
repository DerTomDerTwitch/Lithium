using HarmonyLib;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppTMPro;
using Il2CppScheduleOne.Property;

namespace Lithium.Modules.PropertyPrices.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.NetworkInitialize___Early))]
    public class ForcePropertyPrices
    {
        [HarmonyPrefix]
        public static void PatchPrices()
        {
            ModPropertyPricesConfiguration configuration = Core.Get<ModPropertyPrices>().Configuration;
            if (!configuration.Enabled)
                return;

            ChangePropertyPrices(configuration);
            UpdateMissMingDialog(configuration);
            configuration.SaveConfiguration();
        }

        private static void ChangePropertyPrices(ModPropertyPricesConfiguration configuration)
        {
            Property[] props = UnityEngine.Object.FindObjectsOfType<Property>();
            foreach (Property prop in props)
            {
                if (!configuration.PropertyPrices.TryGetValue(prop.PropertyName, out int price))
                {
                    Log.Warning(
                        $"Property {prop.PropertyName} not found in configuration. Skipping.");
                    continue;
                }

                prop.Price = price;

                if (prop.ForSaleSign != null)
                    prop.ForSaleSign.transform.Find("Price").GetComponent<TextMeshPro>().text =
                        MoneyManager.FormatAmount(price);
                if (prop.ListingPoster != null)
                    prop.ListingPoster.Find("Price").GetComponent<TextMeshPro>().text =
                        MoneyManager.FormatAmount(price);
            }
        }

        private static void UpdateMissMingDialog(ModPropertyPricesConfiguration configuration)
        {
            DialogueController_Ming[] dialogueControllers =
                UnityEngine.Object.FindObjectsOfType<DialogueController_Ming>();
            foreach (DialogueController_Ming item in dialogueControllers)
            {
                item.Price = item.gameObject.transform.parent?.name switch
                {
                    "Ming" => configuration.PropertyPrices["Sweatshop"],
                    "Donna" => configuration.PropertyPrices["Motel Room"],
                    _ => item.Price
                };
            }
        }
    }
}