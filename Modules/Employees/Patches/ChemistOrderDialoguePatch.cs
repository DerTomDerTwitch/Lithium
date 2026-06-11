using System;
using HarmonyLib;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Employees;
using Lithium.Modules.Employees.ProductionOrders;
using UnityEngine.Events;

namespace Lithium.Modules.Employees.Patches
{
    // Adds a "production order" dialogue choice to every chemist. Employee.Start is the MonoBehaviour lifecycle
    // method where the vanilla employee choices are built, so it is un-inlinable and the proven per-spawn hook.
    // The choice is added only while the feature is enabled (re-run on every save load). We do NOT use a managed
    // shouldShowCheck delegate: the game invokes ShouldShow() on every choice while rendering the dialogue, and a
    // throwing managed delegate there would blank out the entire choice list.
    [HarmonyPatch(typeof(Employee), nameof(Employee.Start))]
    internal static class ChemistOrderDialoguePatch
    {
        private const string ChoiceLabel = "I have a production order for you.";

        [HarmonyPostfix]
        private static void Postfix(Employee __instance)
        {
            try
            {
                Chemist chemist = __instance != null ? __instance.TryCast<Chemist>() : null;
                if (chemist == null)
                    return;

                ChemistOrdersConfiguration config = ChemistOrderService.Config;
                if (config == null || !config.Enabled || !config.AddDialogueOption)
                    return;

                DialogueHandler handler = chemist.DialogueHandler;
                if (handler == null)
                {
                    Log.Warning("[ChemistOrders] Chemist has no DialogueHandler; can't add the order choice.");
                    return;
                }
                DialogueController controller = handler.GetComponent<DialogueController>();
                if (controller == null)
                {
                    Log.Warning("[ChemistOrders] Chemist has no DialogueController; can't add the order choice.");
                    return;
                }

                // De-dup: Start re-runs on fresh NPC objects after a reload.
                var choices = controller.Choices;
                if (choices != null)
                {
                    for (int i = 0; i < choices.Count; i++)
                    {
                        if (choices[i] != null && choices[i].ChoiceText == ChoiceLabel)
                            return;
                    }
                }

                Chemist captured = chemist;
                DialogueController.DialogueChoice choice = new();
                choice.ChoiceText = ChoiceLabel;
                choice.Enabled = true;
                choice.onChoosen.AddListener((UnityAction)(Action)(() => ChemistOrderScreen.Request(captured)));
                controller.AddDialogueChoice(choice, 2);

                Log.Info($"[ChemistOrders] Added production-order dialogue choice to {SafeName(chemist)}.");
            }
            catch (Exception e)
            {
                Log.Warning($"[ChemistOrders] Failed to add dialogue choice: {e.Message}");
            }
        }

        private static string SafeName(Chemist chemist)
        {
            try { return chemist.fullName; }
            catch { return "chemist"; }
        }
    }
}
