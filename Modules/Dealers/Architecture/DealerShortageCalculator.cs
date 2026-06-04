using System.Collections.Generic;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;

namespace Lithium.Modules.Dealers.Architecture
{
    internal sealed class Shortfall
    {
        public string ProductName;
        public int Deficit;
        public int DealAbsMinute;
    }

    internal static class DealerShortageCalculator
    {
        public static List<Shortfall> Compute(Dealer dealer)
        {
            List<Shortfall> result = new();
            if (dealer == null)
                return result;

            TimeManager time = TimeManager.Instance;
            if (time == null)
                return result;

            int nowAbs = time.GetDateTime().GetMinSum();
            int nowMinOfDay = TimeManager.GetMinSumFrom24HourTime(time.CurrentTime);

            Dictionary<string, int> stock = new();
            Il2CppSystem.Collections.Generic.List<ItemSlot> slots = dealer.GetAllSlots();
            if (slots != null)
            {
                foreach (ItemSlot slot in slots)
                {
                    ItemInstance inst = slot?.ItemInstance;
                    ItemDefinition def = inst?.Definition;
                    if (def == null)
                        continue;
                    string id = def.ID;
                    if (string.IsNullOrEmpty(id))
                        continue;
                    stock[id] = (stock.TryGetValue(id, out int q) ? q : 0) + slot.Quantity;
                }
            }

            List<(Contract contract, int dealAbs)> deals = new();
            Il2CppSystem.Collections.Generic.List<Contract> contracts = dealer.ActiveContracts;
            if (contracts != null)
            {
                foreach (Contract c in contracts)
                {
                    if (c == null || c.ProductList == null)
                        continue;
                    deals.Add((c, DueAbsMinute(c, nowAbs, nowMinOfDay)));
                }
            }
            deals.Sort((a, b) => a.dealAbs.CompareTo(b.dealAbs));

            HashSet<string> flagged = new();
            foreach ((Contract contract, int dealAbs) in deals)
            {
                foreach (ProductList.Entry entry in contract.ProductList.entries)
                {
                    if (entry == null)
                        continue;
                    string id = entry.ProductID;
                    if (string.IsNullOrEmpty(id))
                        continue;

                    int have = (stock.TryGetValue(id, out int q) ? q : 0) - entry.Quantity;
                    stock[id] = have;

                    if (have < 0 && flagged.Add(id))
                    {
                        result.Add(new Shortfall
                        {
                            ProductName = ResolveName(id),
                            Deficit = -have,
                            DealAbsMinute = dealAbs
                        });
                    }
                }
            }

            result.Sort((a, b) => a.DealAbsMinute.CompareTo(b.DealAbsMinute));
            return result;
        }

        private static int DueAbsMinute(Contract contract, int nowAbs, int nowMinOfDay)
        {
            QuestWindowConfig window = contract.DeliveryWindow;
            int windowMinOfDay = (window != null && window.IsEnabled)
                ? TimeManager.GetMinSumFrom24HourTime(window.WindowStartTime)
                : 0;

            int delta = windowMinOfDay - nowMinOfDay;
            if (delta < 0)
                delta += 1440;
            return nowAbs + delta;
        }

        public static string ResolveName(string productId)
        {
            try
            {
                ItemDefinition def = Registry.GetItem(productId);
                string name = def?.Name;
                return string.IsNullOrEmpty(name) ? productId : name;
            }
            catch
            {
                return productId;
            }
        }
    }
}
