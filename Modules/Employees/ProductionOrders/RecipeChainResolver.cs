using System.Collections.Generic;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.StationFramework;

namespace Lithium.Modules.Employees.ProductionOrders
{
    // Resolves a target product into a linear sequence of mixing steps (base + mixer -> intermediate -> ...
    // -> target), using ONLY discovered mix recipes. There is no chain-walking utility in the game, so we
    // build one here via a breadth-first search over ProductManager.mixRecipes (the discovered-recipe set).
    //
    // Ingredient slot order in a StationRecipe is unstable (save/load and host->client replication swap the
    // two ingredients), so every recipe is classified by membership in ProductManager.ValidMixIngredients
    // (the mixer) vs. the other ingredient (the base product) — never by index.
    internal static class RecipeChainResolver
    {
        private readonly struct Edge
        {
            public readonly string BaseId;   // the product ingredient
            public readonly string MixerId;  // the mixer ingredient
            public readonly string OutputId; // the result product

            public Edge(string baseId, string mixerId, string outputId)
            {
                BaseId = baseId;
                MixerId = mixerId;
                OutputId = outputId;
            }
        }

        // Resolves the shortest makeable chain producing targetId. Prefers a chain whose base product and
        // every mixer are currently present on the shelves (availableIds); if none exists, falls back to the
        // shortest discovered chain down to a raw base product (one nothing else produces) so the order can be
        // accepted and the player told what to stock. Returns null if the target cannot be mixed at all.
        public static List<OrderStep> Resolve(string targetId, HashSet<string> availableIds)
        {
            ProductManager manager = NetworkSingleton<ProductManager>.Instance;
            if (manager == null || manager.mixRecipes == null || string.IsNullOrEmpty(targetId))
                return null;

            HashSet<string> mixerIds = new();
            if (manager.ValidMixIngredients != null)
            {
                for (int i = 0; i < manager.ValidMixIngredients.Count; i++)
                {
                    PropertyItemDefinition mixer = manager.ValidMixIngredients[i];
                    if (mixer != null && !string.IsNullOrEmpty(mixer.ID))
                        mixerIds.Add(mixer.ID);
                }
            }

            // output id -> the recipes (edges) that produce it
            Dictionary<string, List<Edge>> producedBy = new();
            HashSet<string> hasRecipe = new(); // every output id reachable by some discovered recipe

            var recipes = manager.mixRecipes;
            for (int r = 0; r < recipes.Count; r++)
            {
                StationRecipe recipe = recipes[r];
                if (recipe == null || recipe.Ingredients == null || recipe.Product == null || recipe.Product.Item == null)
                    continue;
                if (recipe.Ingredients.Count < 2)
                    continue;

                ItemDefinition mixerItem = null;
                ItemDefinition baseItem = null;
                for (int g = 0; g < recipe.Ingredients.Count; g++)
                {
                    ItemDefinition ingredient = recipe.Ingredients[g].Item;
                    if (ingredient == null)
                        continue;
                    if (mixerItem == null && mixerIds.Contains(ingredient.ID))
                        mixerItem = ingredient;
                    else
                        baseItem = ingredient;
                }
                if (mixerItem == null || baseItem == null)
                    continue;

                string outId = recipe.Product.Item.ID;
                if (string.IsNullOrEmpty(outId) || outId == baseItem.ID)
                    continue;

                if (!producedBy.TryGetValue(outId, out List<Edge> list))
                {
                    list = new List<Edge>();
                    producedBy[outId] = list;
                }
                list.Add(new Edge(baseItem.ID, mixerItem.ID, outId));
                hasRecipe.Add(outId);
            }

            if (!producedBy.ContainsKey(targetId))
                return null; // target is not the output of any discovered mix recipe

            // Pass 1: require every mixer and the base product to be present on the shelves right now.
            List<OrderStep> chain = Search(targetId, producedBy, hasRecipe, availableIds,
                requireAvailableMixers: true, requireAvailableBase: true);
            if (chain != null)
                return chain;

            // Pass 2: ignore current stock — terminate at a raw base product (one nothing discovered produces).
            return Search(targetId, producedBy, hasRecipe, availableIds,
                requireAvailableMixers: false, requireAvailableBase: false);
        }

        // Breadth-first search backward from the target. A node (product id) is a valid chain START when it is
        // available on a shelf (requireAvailableBase) or — in the fallback pass — a raw base (nothing produces
        // it). BFS guarantees the shortest chain. A visited set prevents the cycles that mix maps can contain.
        private static List<OrderStep> Search(string targetId, Dictionary<string, List<Edge>> producedBy,
            HashSet<string> hasRecipe, HashSet<string> availableIds,
            bool requireAvailableMixers, bool requireAvailableBase)
        {
            // Each queue entry is a product we still need to produce, plus the chain of steps (target-first)
            // that turns it into the target.
            Queue<(string need, List<OrderStep> tail)> queue = new();
            HashSet<string> visited = new();
            queue.Enqueue((targetId, new List<OrderStep>()));
            visited.Add(targetId);

            while (queue.Count > 0)
            {
                (string need, List<OrderStep> tail) = queue.Dequeue();

                if (!producedBy.TryGetValue(need, out List<Edge> edges))
                    continue;

                foreach (Edge edge in edges)
                {
                    if (requireAvailableMixers && (availableIds == null || !availableIds.Contains(edge.MixerId)))
                        continue;

                    // Prepend this step: producing `need` from `edge.BaseId` + `edge.MixerId`.
                    List<OrderStep> chain = new() { new OrderStep(edge.BaseId, edge.MixerId, need) };
                    chain.AddRange(tail);

                    bool baseIsRaw = !hasRecipe.Contains(edge.BaseId);
                    bool baseAvailable = availableIds != null && availableIds.Contains(edge.BaseId);
                    bool baseAccepted = requireAvailableBase ? baseAvailable : (baseAvailable || baseIsRaw);

                    if (baseAccepted)
                        return chain; // shortest chain found

                    if (visited.Add(edge.BaseId))
                        queue.Enqueue((edge.BaseId, chain));
                }
            }

            return null;
        }
    }
}
