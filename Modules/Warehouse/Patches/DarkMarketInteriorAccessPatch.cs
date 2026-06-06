using System;
using HarmonyLib;
using Il2CppScheduleOne.Doors;

namespace Lithium.Modules.Warehouse.Patches
{
    // Closes the "skateboard glitch": a player clips outside the warehouse but can still trigger the
    // interior door handle to open the after-hours (exit-only) Dark Market door from the inside.
    //
    // DoorController.CanPlayerAccess(EDoorSide, out string) is the shared gate for both the hover UI
    // (InteriorHandleHovered) and the actual open (InteriorHandleInteracted). For an exit-only door it
    // returns true for the Interior side regardless of where the player physically is. We postfix it:
    // when the door is exit-only and the request is for the Interior side, we deny it unless the local
    // player is genuinely standing in the warehouse-side door sensor. Leaving the result false with an
    // empty reason makes the interior handle show as disabled rather than openable.
    //
    // Scope is limited to the Dark Market's own doors (resolved via DarkMarketAccessZone.Doors); every
    // other door in the game falls straight through. We only act while the door is ExitOnly, so once
    // the market is fully open (PlayerAccess == Open) normal behaviour is untouched.
    [HarmonyPatch(typeof(DoorController), nameof(DoorController.CanPlayerAccess),
        new Type[] { typeof(EDoorSide), typeof(string) }, new[] { ArgumentType.Normal, ArgumentType.Out })]
    public class DarkMarketInteriorAccessPatch
    {
        [HarmonyPostfix]
        public static void Postfix(DoorController __instance, EDoorSide side, ref bool __result)
        {
            if (!__result || side != EDoorSide.Interior)
                return;

            ModWarehouse module = Core.Get<ModWarehouse>();
            if (module == null || !module.Configuration.Enabled)
                return;

            // Only the exit-only (after-hours / locked-but-exitable) state is exploitable. When the
            // market is fully open the door is meant to work from either side, so leave it alone.
            if (__instance.PlayerAccess != EDoorAccess.ExitOnly)
                return;

            if (module.ShouldBlockInteriorOpen(__instance))
                __result = false;
        }
    }
}
