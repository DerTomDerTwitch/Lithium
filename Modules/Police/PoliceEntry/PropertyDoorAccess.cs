using Il2CppScheduleOne.Building.Doors;
using UnityEngine;

namespace Lithium.Modules.Police.PoliceEntry
{
    /// <summary>
    /// Lets NPCs (notably pursuing police) walk through the player's property doors. Property doors normally
    /// open for the player but have <c>OpenableByNPCs = false</c>, so NPCs stop at the threshold. Flipping it to
    /// <c>true</c> makes the door open for an approaching NPC; because an owned property's door has
    /// <c>PlayerAccess == Open</c>, the door's <c>PlayerBlocker</c> collider stays disabled, so the NPC can pass.
    ///
    /// Note: whether a cop can then path <em>into</em> the interior also depends on the interior NavMesh
    /// connecting through the doorway. Employees already navigate property interiors, so this generally works,
    /// but it is the part most likely to vary by property — treat as best-effort. Originals are captured so a
    /// live config reload that disables the feature restores vanilla behaviour.
    /// </summary>
    public static class PropertyDoorAccess
    {
        private static readonly Dictionary<int, bool> OriginalOpenableByNpcs = new();

        public static void Apply(PoliceEntrySettings settings)
        {
            var doors = UnityEngine.Object.FindObjectsOfType<PropertyDoorController>(true);
            if (doors == null)
                return;

            bool enable = settings != null && settings.Enabled;
            int changed = 0;

            for (int i = 0; i < doors.Length; i++)
            {
                PropertyDoorController door = doors[i];
                if (door == null)
                    continue;

                int id = door.GetInstanceID();
                if (!OriginalOpenableByNpcs.ContainsKey(id))
                    OriginalOpenableByNpcs[id] = door.OpenableByNPCs;

                bool target = enable ? true : OriginalOpenableByNpcs[id];
                if (door.OpenableByNPCs != target)
                {
                    door.OpenableByNPCs = target;
                    changed++;
                }
            }

            if (enable)
                Log.Info($"[Police] NPC entry enabled on property doors ({doors.Length} found, {changed} changed).");
        }
    }
}
