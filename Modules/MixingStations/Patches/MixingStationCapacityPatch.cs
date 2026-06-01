using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;

namespace Lithium.Modules.MixingStations.Patches
{
    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.Start))]
    public class MixingStationCapacityPatch
    {
        [HarmonyPostfix]
        public static void MixingStationCapacity(MixingStation __instance)
        {
            ModMixingStationsConfiguration config = Core.Get<ModMixingStations>().Configuration;
            if (!config.Enabled)
                return;

            // MixingStationMk2 derives from MixingStation and inherits Start, so this postfix fires for
            // both tiers; TryCast returns non-null only for the MK II so we can apply its own capacity.
            bool isMk2 = __instance.TryCast<MixingStationMk2>() != null;
            __instance.MaxMixQuantity = isMk2 ? config.Mk2InputCapacity : config.InputCapacity;
        }
    }
}
