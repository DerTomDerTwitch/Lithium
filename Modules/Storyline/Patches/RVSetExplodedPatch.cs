using HarmonyLib;
using Il2CppScheduleOne.Property;
using MelonLoader;
using UnityEngine;

namespace Lithium.Modules.Storyline.Patches
{
    // The game reworked the RV destruction flow: the old RV.SetExploded() was split into
    // BlowUp() (plays the explosion sequence) and SetDestroyed() (sets the destroyed state).
    // We intercept both so that, regardless of the code path, the explosion is skipped and the
    // wrecked-RV model is swapped in quietly.
    [HarmonyPatch(typeof(RV), nameof(RV.BlowUp))]
    public class RVBlowUpPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(RV __instance) => RVExplosionPrevention.Handle(__instance);
    }

    [HarmonyPatch(typeof(RV), nameof(RV.SetDestroyed))]
    public class RVSetDestroyedPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(RV __instance) => RVExplosionPrevention.Handle(__instance);
    }

    internal static class RVExplosionPrevention
    {
        // Returns true to run the original method, false to replace it with the quiet wreckage swap.
        public static bool Handle(RV __instance)
        {
            ModStorylineConfiguration config = Core.Get<ModStoryline>().Configuration;
            if (!config.Enabled || !config.PreventRVExplosion)
                return true;

            GameObject destroyedRV = GetChildGameObject(__instance.gameObject, "Destroyed RV");
            if (destroyedRV != null)
            {
                destroyedRV.SetActive(true);
                GameObject cartelNote = GetChildGameObject(destroyedRV, "CartelNote");
                if (cartelNote != null)
                {
                    cartelNote.SetActive(true);
                }

                GameObject destroyedRVChild = GetChildGameObject(destroyedRV, "destroyed rv");
                if (destroyedRVChild != null)
                {
                    destroyedRVChild.SetActive(false);
                }
            }
            else
            {
                MelonLogger.Msg("Destroyed RV not found");
            }

            return false;
        }

        private static GameObject GetChildGameObject(GameObject obj, string childName)
        {
            Transform transform = obj.transform.Find(childName);
            bool flag = transform != null;
            GameObject gameObject = flag ? transform.gameObject : null;
            return gameObject;
        }
    }
}
