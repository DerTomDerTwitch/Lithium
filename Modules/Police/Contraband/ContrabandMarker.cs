using Il2CppScheduleOne;
using Il2CppScheduleOne.Core.Items.Framework;
using Il2CppScheduleOne.ItemFramework;

namespace Lithium.Modules.Police.Contraband
{
    /// <summary>
    /// Writes an illegal <c>legalStatus</c> onto every item definition the <see cref="ContrabandMatcher"/> matches
    /// (the same approach as the "Illegal Seeds" mod, generalised and config-driven). The vanilla status is captured
    /// per item ID the first time we touch it, so a live config reload — including disabling the feature — restores
    /// the original values. Mirrors the Weapons module's <c>WeaponPawnValue</c> capture/restore pattern.
    /// </summary>
    public static class ContrabandMarker
    {
        private static readonly Dictionary<string, ELegalStatus> OriginalStatus = new();

        public static void ReapplyAll()
        {
            Registry registry = Registry.Instance;
            if (registry == null)
                return;

            ModPolice module = Core.Get<ModPolice>();
            if (module == null)
                return;

            bool enabled = module.Configuration.Enabled && module.Configuration.Contraband.Enabled;

            Il2CppSystem.Collections.Generic.List<ItemDefinition> allItems = registry.GetAllItems();
            if (allItems == null)
                return;

            int marked = 0;
            for (int i = 0; i < allItems.Count; i++)
            {
                ItemDefinition definition = allItems[i];
                if (definition == null)
                    continue;

                string id = definition.ID;

                // Capture the vanilla status the first time we see this definition.
                if (!OriginalStatus.ContainsKey(id))
                    OriginalStatus[id] = definition.legalStatus;

                ELegalStatus? severity = enabled ? ContrabandMatcher.GetSeverity(definition) : null;
                ELegalStatus target = severity ?? OriginalStatus[id];

                if (definition.legalStatus != target)
                    definition.legalStatus = target;

                if (severity.HasValue && severity.Value != ELegalStatus.Legal)
                    marked++;
            }

            if (enabled)
                Log.Info($"[Police] Marked {marked} item definition(s) as contraband.");
        }
    }
}
