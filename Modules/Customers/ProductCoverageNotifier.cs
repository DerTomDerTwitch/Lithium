using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Product;
using Lithium.Helper;

namespace Lithium.Modules.Customers
{
    /// <summary>
    /// Tracks which customers are "covered" — i.e. at least one currently listed product matches their
    /// desired effects — and texts the player (via the Lithium contact) when a product listing change
    /// newly covers or uncovers customers, with the overall coverage percentage.
    ///
    /// A single canonical <see cref="_knownCovered"/> snapshot is kept rather than diffing per call:
    /// SetProductListed fires twice on the host (local call + RPC path), and diffing against the known
    /// state means the second invocation sees no further change and stays silent.
    /// </summary>
    public static class ProductCoverageNotifier
    {
        private static HashSet<string> _knownCovered;

        // Called on save/scene load so the baseline re-snapshots for the new game state.
        public static void Reset() => _knownCovered = null;

        // Snapshot the current coverage before a listing change, if we don't have one yet.
        public static void EnsureBaseline() => _knownCovered ??= ComputeCoveredIds();

        public static HashSet<string> ComputeCoveredIds()
        {
            HashSet<string> covered = [];
            List<ProductDefinition> listed = ProductManager.ListedProducts.ToList();

            foreach (Customer c in Customer.UnlockedCustomers.ToList())
            {
                if (c == null || c.CustomerData == null || c.NPC == null)
                    continue;

                List<string> desires = c.CustomerData.PreferredProperties.ToList().Select(p => p.Name).ToList();
                if (desires.Count == 0)
                    continue;

                if (listed.Any(p => ProductHelper.ProductMatchesDesires(p, desires)))
                    covered.Add(c.NPC.ID);
            }

            return covered;
        }

        public static void ReportChange()
        {
            HashSet<string> after = ComputeCoveredIds();

            if (_knownCovered == null)
            {
                // No baseline (e.g. EnsureBaseline didn't run) — adopt current state silently.
                _knownCovered = after;
                return;
            }

            // Map ids -> names and collect the coverable population (unlocked customers with desires).
            Dictionary<string, string> idToName = [];
            List<string> coverableIds = [];
            foreach (Customer c in Customer.UnlockedCustomers.ToList())
            {
                if (c == null || c.CustomerData == null || c.NPC == null)
                    continue;
                if (c.CustomerData.PreferredProperties.ToList().Count == 0)
                    continue;
                coverableIds.Add(c.NPC.ID);
                idToName[c.NPC.ID] = c.NPC.fullName;
            }

            string Name(string id) => idToName.TryGetValue(id, out string n) ? n : id;

            List<string> newlyCovered = after.Except(_knownCovered).Select(Name).OrderBy(n => n).ToList();
            List<string> newlyUncovered = _knownCovered.Except(after).Select(Name).OrderBy(n => n).ToList();

            // Always advance the known state so the duplicate invocation reports nothing.
            _knownCovered = after;

            if (newlyCovered.Count == 0 && newlyUncovered.Count == 0)
                return;

            ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
            int coverable = coverableIds.Count;
            int pct = coverable > 0 ? (int)Math.Round(after.Count * 100.0 / coverable) : 0;
            string suffix = BuildUncoveredSuffix(config, coverableIds, after, idToName);

            if (newlyCovered.Count > 0)
                LithiumContact.Send($"You now cover {newlyCovered.SmartJoin(", ", " and ")} with your products. Overall: {pct}%{suffix}");

            if (newlyUncovered.Count > 0)
                LithiumContact.Send($"{newlyUncovered.SmartJoin(", ", " and ")} {(newlyUncovered.Count == 1 ? "is" : "are")} no longer covered. Overall: {pct}%{suffix}");
        }

        private static string BuildUncoveredSuffix(
            ModCustomersConfiguration config, List<string> coverableIds, HashSet<string> covered, Dictionary<string, string> idToName)
        {
            if (!config.Coverage.ListUncovered)
                return string.Empty;

            List<string> uncovered = coverableIds
                .Where(id => !covered.Contains(id))
                .Select(id => idToName[id])
                .OrderBy(n => n)
                .ToList();

            if (uncovered.Count == 0)
                return " Everyone is covered!";

            return $" Still uncovered: {uncovered.SmartJoin(", ", " and ")}";
        }
    }
}
