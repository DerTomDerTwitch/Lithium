using Il2CppFishNet;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.Product;
using Lithium.Helper;

namespace Lithium.Modules.Customers
{
    /// <summary>
    /// Sends a single compact "here's where you stand" text from the Lithium contact shortly after a save
    /// loads: dealers on payroll, unlocked customers, listed products and overall effect coverage. Driven
    /// by a delayed coroutine in <see cref="ModCustomers.Apply"/> so the world is fully loaded first.
    /// </summary>
    public static class LithiumStartupReport
    {
        // The save has settled enough to text: messaging is up and the customer roster has populated.
        public static bool WorldReady() =>
            MessagingManager.Instance != null &&
            Customer.UnlockedCustomers != null && Customer.UnlockedCustomers.Count > 0;

        public static void Send()
        {
            if (!InstanceFinder.IsServer)
                return;

            List<Customer> customers = Customer.UnlockedCustomers.ToList()
                .Where(c => c != null && c.CustomerData != null && c.NPC != null)
                .ToList();

            List<Customer> coverable = customers
                .Where(c => c.CustomerData.PreferredProperties.ToList().Count > 0)
                .ToList();

            HashSet<string> covered = ProductCoverageNotifier.ComputeCoveredIds();
            int coveredCount = coverable.Count(c => covered.Contains(c.NPC.ID));
            int pct = coverable.Count > 0 ? (int)Math.Round(coveredCount * 100.0 / coverable.Count) : 0;
            int uncovered = coverable.Count - coveredCount;

            int dealers = Dealer.AllPlayerDealers.ToList().Count(d => d != null && d.IsRecruited);
            int listed = ProductManager.ListedProducts.ToList().Count;

            string msg =
                $"Welcome back. {dealers} {(dealers == 1 ? "dealer" : "dealers")} on payroll, " +
                $"{customers.Count} {(customers.Count == 1 ? "customer" : "customers")} unlocked, " +
                $"{listed} {(listed == 1 ? "product" : "products")} listed. " +
                $"Effect coverage: {pct}%" +
                (uncovered > 0
                    ? $" — {uncovered} {(uncovered == 1 ? "customer is" : "customers are")} still uncovered."
                    : " — everyone's covered.");

            LithiumContact.Send(msg);
        }
    }
}
