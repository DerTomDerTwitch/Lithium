using System;
using HarmonyLib;
using Il2CppScheduleOne.Building.Doors;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Property;

namespace Lithium.Modules.Rent.Patches
{
    [HarmonyPatch(typeof(PropertyDoorController), nameof(PropertyDoorController.CanPlayerAccess),
        new Type[] { typeof(EDoorSide), typeof(string) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Out })]
    public class PropertyDoorAccessPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PropertyDoorController __instance, EDoorSide side, ref string reason, ref bool __result)
        {
            ModRent mod = Core.Get<ModRent>();
            if (mod == null || !mod.Configuration.Enabled)
                return true;

            try
            {
                if (side != EDoorSide.Exterior)
                    return true;

                Property prop = __instance.Property;
                if (prop == null || !ModRent.IsLockedOut(prop.PropertyCode))
                    return true;

                reason = "The locks have been changed. Pay your overdue rent.";
                __result = false;
                return false;
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Door access check failed: {e.Message}");
                return true;
            }
        }
    }
}
