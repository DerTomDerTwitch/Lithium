using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Product;
using Lithium.Helper;

namespace Lithium.Modules.Customers
{
    public static class DealerCoverageNotifier
    {
        public static void ReportForDealer(Dealer dealer)
        {
            if (dealer == null)
                return;

            List<Customer> customers = dealer.AssignedCustomers.ToList()
                .Where(c => c.IsServeable())
                .ToList();
            if (customers.Count == 0)
                return;

            List<string> stocked = dealer.GetDealerStockedEffects();

            int covered = 0;
            List<string> uncoveredCustomers = [];
            HashSet<string> uncoveredEffects = [];

            foreach (Customer c in customers)
            {
                List<string> desires = ProductHelper.GetDesireNames(c.CustomerData);
                List<string> missing = desires.Except(stocked).ToList();

                if (desires.Count == 0 || missing.Count < desires.Count)
                    covered++;
                else
                    uncoveredCustomers.Add(c.NPC.fullName);

                foreach (string m in missing)
                    uncoveredEffects.Add(m);
            }

            int total = customers.Count;
            int pct = (int)Math.Round(covered * 100.0 / total);
            string dealerName = string.IsNullOrEmpty(dealer.FirstName) ? dealer.fullName : dealer.FirstName;
            string head = $"{dealerName} — {covered}/{total} customers covered ({pct}%).";

            if (uncoveredCustomers.Count == 0)
            {
                LithiumContact.Send($"{head} Stock covers everyone — nice work.");
                return;
            }

            LithiumContact.Send(
                $"{head} Uncovered: {uncoveredCustomers.OrderBy(n => n).SmartJoin(", ", " and ")}. " +
                $"Missing effects: {uncoveredEffects.OrderBy(e => e).SmartJoin(", ", " and ")}.");
        }

        private static HashSet<string> _knownNoDealerCovered;

        public static void ResetNoDealer() => _knownNoDealerCovered = null;

        public static void ReportNoDealerCustomers()
        {
            (HashSet<string> coveredIds, int total, List<string> uncovered) = ComputeNoDealer();
            _knownNoDealerCovered = coveredIds;
            SendNoDealer(coveredIds.Count, total, uncovered);
        }

        public static void ReportNoDealerChange()
        {
            (HashSet<string> coveredIds, int total, List<string> uncovered) = ComputeNoDealer();
            if (_knownNoDealerCovered != null && _knownNoDealerCovered.SetEquals(coveredIds))
                return;
            _knownNoDealerCovered = coveredIds;
            SendNoDealer(coveredIds.Count, total, uncovered);
        }

        private static (HashSet<string> coveredIds, int total, List<string> uncovered) ComputeNoDealer()
        {
            List<ProductDefinition> listed = ProductManager.ListedProducts.ToList();
            List<Customer> customers = Customer.UnlockedCustomers.ToList()
                .Where(c => c.IsServeable() && c.AssignedDealer == null)
                .ToList();

            HashSet<string> coveredIds = [];
            List<string> uncovered = [];

            foreach (Customer c in customers)
            {
                List<string> desires = ProductHelper.GetDesireNames(c.CustomerData);

                if (desires.Count == 0 || listed.Any(p => ProductHelper.ProductMatchesDesires(p, desires)))
                    coveredIds.Add(c.NPC.ID);
                else
                    uncovered.Add(c.NPC.fullName);
            }

            return (coveredIds, customers.Count, uncovered);
        }

        private static void SendNoDealer(int covered, int total, List<string> uncovered)
        {
            if (total == 0)
                return;

            int pct = (int)Math.Round(covered * 100.0 / total);
            string head = $"No-dealer customers — {covered}/{total} covered ({pct}%).";

            if (uncovered.Count == 0)
            {
                LithiumContact.Send($"{head} You've got them all.");
                return;
            }

            LithiumContact.Send($"{head} Uncovered: {uncovered.OrderBy(n => n).SmartJoin(", ", " and ")}.");
        }
    }
}
