using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Lithium.Modules.Dealers.Architecture;
using UnityEngine;

namespace Lithium.Modules.Dealers.Patches
{
    [HarmonyPatch(typeof(Dealer), nameof(Dealer.TryRobDealer))]
    public class DealerRobberyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Dealer __instance)
        {
            ModDealers mod = Core.Get<ModDealers>();
            if (mod == null || !mod.Configuration.Enabled || !mod.Configuration.Robbery.PreventWhenArmed)
                return true;

            switch (DealerWeaponInspector.Classify(__instance))
            {
                case WeaponStatus.Adequate:
                    Log.Info($"[Dealers] Robbery prevented: {__instance.fullName} is adequately armed.");
                    return false;

                case WeaponStatus.Outdated:
                    if (UnityEngine.Random.value < mod.Configuration.Robbery.OutdatedWeaponImmunityChance)
                    {
                        Log.Info($"[Dealers] Robbery prevented ({__instance.fullName}'s outdated weapon held them off).");
                        return false;
                    }
                    return true;

                default:
                    return true;
            }
        }
    }
}
