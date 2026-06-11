using System.Collections.Generic;
using Newtonsoft.Json;

namespace Lithium.Modules.Employees.ProductionOrders
{
    // One stage of a multi-step mixing chain: InputId + MixerId mix into OutputId. For stage 1 InputId is a
    // base product fetched from a shelf; for later stages InputId is the previous stage's OutputId (the
    // intermediate the chemist puts back as the next source). The final stage's OutputId == TargetProductId.
    public sealed class OrderStep
    {
        [JsonProperty(Order = 1)] public string InputId = "";
        [JsonProperty(Order = 2)] public string MixerId = "";
        [JsonProperty(Order = 3)] public string OutputId = "";

        public OrderStep() { }

        public OrderStep(string input, string mixer, string output)
        {
            InputId = input;
            MixerId = mixer;
            OutputId = output;
        }
    }

    // Persisted per-chemist production order (keyed by the chemist's GUID in the SaveSlotStore). Only the
    // host ever writes this; the orchestrator re-derives all transient execution state from live world state
    // each tick, so the only progress we persist is Started (target units already committed to a final mix).
    public sealed class ChemistOrderState
    {
        [JsonProperty(Order = 1)] public string TargetProductId = "";

        // The display name captured at order time, purely for status texts/logs.
        [JsonProperty(Order = 2)] public string TargetName = "";

        [JsonProperty(Order = 3)] public int Goal;

        // Target units for which a final-stage mix has already been committed (counted once per final batch).
        // The order is complete once Started >= Goal.
        [JsonProperty(Order = 4)] public int Started;

        // GUID strings of the PlaceableStorageEntity shelves the chemist may pull ingredients from.
        [JsonProperty(Order = 5)] public List<string> ShelfGuids = new();

        // The resolved linear chain (stage 1..N). Empty if unresolved.
        [JsonProperty(Order = 6)] public List<OrderStep> Chain = new();

        [JsonProperty(Order = 7)] public bool Active = true;

        [JsonIgnore] public int Remaining => Goal - Started;

        [JsonIgnore] public bool IsComplete => !Active || Started >= Goal;
    }
}
