using System.Text;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Product;
using Lithium.Helper;
using Lithium.Modules.Customers.Architecture;
using MelonLoader.Utils;

namespace Lithium.Modules.Customers
{
    public static class OrderPatternDebug
    {
        public static void Dump()
        {
            try
            {
                ModCustomersConfiguration config = Core.Get<ModCustomers>().Configuration;
                StringBuilder sb = new StringBuilder();

                sb.AppendLine("=== Lithium Order Pattern Dump ===");
                sb.AppendLine($"Module.Enabled            : {config.Enabled}");
                sb.AppendLine($"Contracts.Enabled         : {config.Contracts.Enabled}");
                sb.AppendLine($"OrderPatterns.Enabled     : {config.OrderPatterns.Enabled}");

                int totalXp = LevelManager.Instance != null ? LevelManager.Instance.TotalXP : -1;
                sb.AppendLine($"XP (current / required)   : {totalXp} / {config.Contracts.XPRequired}");

                bool patternsActive = config.Enabled && config.Contracts.Enabled && config.OrderPatterns.Enabled
                    && totalXp >= config.Contracts.XPRequired;
                sb.AppendLine($"Patterns ACTIVE in-game   : {patternsActive}{(patternsActive ? "" : "  (profiles below are still shown for inspection)")}");

                EDay currentDay = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : default;
                int elapsedDays = TimeManager.Instance != null ? TimeManager.Instance.ElapsedDays : -1;
                sb.AppendLine($"Current day / elapsed     : {currentDay} / {elapsedDays}");
                sb.AppendLine();

                List<Customer> customers = [];
                customers.AddRange(Customer.UnlockedCustomers.ToList());
                customers.AddRange(Customer.LockedCustomers.ToList());

                sb.AppendLine($"Total customers: {customers.Count} (Unlocked: {Customer.UnlockedCustomers.Count}, Locked: {Customer.LockedCustomers.Count})");
                sb.AppendLine();

                foreach (Customer customer in customers)
                {
                    DumpCustomer(sb, customer, currentDay);
                }

                string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "Lithium");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "OrderPatternsDump.txt");
                File.WriteAllText(path, sb.ToString());

                Log.Info($"[OrderPatternDebug] Wrote {customers.Count} customers to {path}");
            }
            catch (Exception ex)
            {
                Log.Error($"[OrderPatternDebug] Dump failed: {ex}");
            }
        }

        private static void DumpCustomer(StringBuilder sb, Customer customer, EDay currentDay)
        {
            try
            {
                CustomerData data = customer.CustomerData;
                string npcName = customer.NPC != null ? customer.NPC.fullName : "<no npc>";
                bool unlocked = Customer.UnlockedCustomers.Contains(customer);

                sb.AppendLine($"- {npcName}  [{(unlocked ? "Unlocked" : "Locked")}]");

                if (data == null)
                {
                    sb.AppendLine("    <no CustomerData>");
                    sb.AppendLine();
                    return;
                }

                string seedSource = data.name;
                int seed = StableHash.Compute(seedSource);
                int referenceOrdersPerWeek = Math.Clamp((int)Math.Round((data.MinOrdersPerWeek + data.MaxOrdersPerWeek) / 2.0), 1, 7);

                OrderPatternProfile profile = OrderPatternProfile.Create(seedSource, data.MinOrdersPerWeek, data.MaxOrdersPerWeek);
                OrderPatternProfile profile2 = OrderPatternProfile.Create(seedSource, data.MinOrdersPerWeek, data.MaxOrdersPerWeek);
                bool deterministic = profile.Archetype == profile2.Archetype && string.Join(",", profile.OrderDays) == string.Join(",", profile2.OrderDays);

                string dealer = customer.AssignedDealer != null ? customer.AssignedDealer.FirstName : "<none>";
                List<string> desireNames = data.PreferredProperties != null
                    ? ProductHelper.GetDesireNames(data)
                    : [];
                int desireCount = desireNames.Count;
                string desires = desireCount > 0 ? string.Join(", ", desireNames) : "<none>";

                string affinities = data.DefaultAffinityData != null
                    ? string.Join(", ", data.DefaultAffinityData.ProductAffinities.ToList().Select(a => $"{a.DrugType}={a.Affinity:0.##}"))
                    : "<none>";

                string coverageInfo;
                if (desireCount == 0)
                {
                    coverageInfo = "no desires -> effect gate N/A (accepts on quality/drug)";
                }
                else
                {
                    int best = 0;
                    string bestProduct = "<none>";
                    foreach (ProductDefinition pd in ProductManager.ListedProducts.ToList())
                    {
                        int cov = pd.Properties.ToList().Select(p => p.Name).Intersect(desireNames).Count();
                        if (cov > best)
                        {
                            best = cov;
                            bestProduct = pd.Name;
                        }
                    }
                    coverageInfo = $"{best}/{desireCount} covered (best listed: '{bestProduct}', {(best * 100 / desireCount)}% -> sample base before quality/drug)";
                }

                bool ordersToday = profile.OrderDays.Contains(currentDay);

                sb.AppendLine($"    CustomerData.name (seed): {seedSource}  (hash {seed})");
                sb.AppendLine($"    OrdersPerWeek min/max   : {data.MinOrdersPerWeek}/{data.MaxOrdersPerWeek}  (reference {referenceOrdersPerWeek})");
                sb.AppendLine($"    WeeklySpend min/max     : {data.MinWeeklySpend:0.##}/{data.MaxWeeklySpend:0.##}");
                sb.AppendLine($"    Standards               : {data.Standards}");
                sb.AppendLine($"    Desired effects ({desireCount})      : {desires}");
                sb.AppendLine($"    Drug affinities         : {affinities}");
                sb.AppendLine($"    Effect coverage (listed): {coverageInfo}");
                sb.AppendLine($"    Assigned dealer         : {dealer}");
                sb.AppendLine($"    >> Archetype            : {profile.Archetype}");
                sb.AppendLine($"    >> Order days ({profile.OrderDays.Count})       : {string.Join(", ", profile.OrderDays)}");
                sb.AppendLine($"    >> Quantity multiplier  : {profile.QuantityMultiplier:0.###}  (per-order = base x mult; weekly volume conserved)");
                sb.AppendLine($"    >> Orders today ({currentDay})? : {ordersToday}");
                sb.AppendLine($"    >> Deterministic        : {(deterministic ? "OK" : "MISMATCH!")}");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    <error dumping customer: {ex.Message}>");
                sb.AppendLine();
            }
        }
    }
}
