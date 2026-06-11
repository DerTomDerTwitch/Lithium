using System.Collections.Generic;
using Newtonsoft.Json;

namespace Lithium.Modules.Employees.ProductionOrders
{
    // A remembered past order, offered for one-click reselection. Persisted per save (a small capped global
    // list). Only entries whose shelves still resolve and whose product still has a chain are shown for a given
    // chemist (validity is evaluated live, not stored).
    public sealed class OrderHistoryEntry
    {
        [JsonProperty(Order = 1)] public string TargetProductId = "";
        [JsonProperty(Order = 2)] public string TargetName = "";
        [JsonProperty(Order = 3)] public int Goal;
        [JsonProperty(Order = 4)] public List<string> ShelfGuids = new();

        public OrderHistoryEntry() { }

        public OrderHistoryEntry(ChemistOrderState order)
        {
            TargetProductId = order.TargetProductId;
            TargetName = order.TargetName;
            Goal = order.Goal;
            ShelfGuids = new List<string>(order.ShelfGuids);
        }

        // Stable identity for de-duplication: same product + same quantity + same set of shelves.
        public string Key()
        {
            List<string> sorted = new(ShelfGuids);
            sorted.Sort(System.StringComparer.Ordinal);
            return TargetProductId + "|" + Goal + "|" + string.Join(",", sorted);
        }
    }
}
