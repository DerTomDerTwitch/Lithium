using System;
using HarmonyLib;
using Il2CppScheduleOne.Building.Doors;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Property;

namespace Lithium.Modules.Rent.Patches
{
    /// <summary>
    /// Enforces the rent lock-out at a property's doors. Patches the access check that the door uses to decide
    /// whether the player may open it; when the property is locked for non-payment we deny only the
    /// <see cref="EDoorSide.Exterior"/> side, so the player can still leave from the inside but cannot return
    /// from the outside — the same one-way behaviour the game uses when the police are pursuing the player.
    /// </summary>
    [HarmonyPatch(typeof(PropertyDoorController), nameof(PropertyDoorController.CanPlayerAccess),
        new Type[] { typeof(EDoorSide), typeof(string) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Out })]
    public class PropertyDoorAccessPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PropertyDoorController __instance, EDoorSide side, ref string reason, ref bool __result)
        {
            ModRent mod = Core.Get<ModRent>();
            if (mod == null || !mod.Configuration.Enabled)
                return true; // run the original

            try
            {
                if (side != EDoorSide.Exterior)
                    return true; // leaving from the inside is always allowed

                Property prop = __instance.Property;
                if (prop == null || !ModRent.IsLockedOut(prop.PropertyCode))
                    return true;

                reason = "The locks have been changed. Pay your overdue rent.";
                __result = false;
                return false; // skip the original — entry denied
            }
            catch (Exception e)
            {
                Log.Warning($"[Rent] Door access check failed: {e.Message}");
                return true;
            }
        }
    }
}
