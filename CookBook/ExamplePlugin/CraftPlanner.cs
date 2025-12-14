using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static CookBook.CraftPlanner;

namespace CookBook
{
    /// <summary>
    /// Computes all items craftable from a starting inventory using Wandering CHEF recipes, up to some max crafting depth.
    /// </summary>
    internal sealed class CraftPlanner
    {
        private readonly IReadOnlyList<ChefRecipe> _recipes;
        private readonly int _itemCount;
        private readonly int _equipmentCount;
        private int _maxDepth;
        private readonly ManualLogSource _log;

        // Lookup tables
        private readonly Dictionary<ResultKey, List<ChefRecipe>> _recipesByResult = new(); // Recipes grouped by result (item/equipment)
        private readonly Dictionary<ResultKey, PlanEntry> _plans = new();

        /// <summary>
        /// Offline computed plans; keyed by desired result.
        /// </summary>
        internal IReadOnlyDictionary<ResultKey, PlanEntry> Plans => _plans;

        /// <summary>
        /// Thrown when the list of craftable chains has been updated, caught by CraftUI.
        /// </summary>
        internal event Action<List<CraftableEntry>> OnCraftablesUpdated;

        /// <summary>
        /// Creates a new CraftPlanner given a recipe list and max traversal depth.
        /// </summary>
        public CraftPlanner(IReadOnlyList<ChefRecipe> recipes, int maxDepth, ManualLogSource log)
        {
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _maxDepth = maxDepth;
            _itemCount = ItemCatalog.itemCount;
            _equipmentCount = EquipmentCatalog.equipmentCount;
            _log = log;

            RebuildAllPlans();
        }

        internal void SetMaxDepth(int newDepth)
        {
            if (newDepth < 0)
            {
                newDepth = 0;
            }

            if (newDepth == _maxDepth)
            {
                return;
            }

            _maxDepth = newDepth;
            RebuildAllPlans();
        }

        /// <summary>
        /// useful for externally triggering a dictionary rebuild if maxdepth is changed.
        /// </summary>
        internal void RebuildAllPlans()
        {
            BuildRecipeIndex();
            BuildPlans();
            _log.LogInfo($"CraftPlanner: Built plans for {_plans.Count} results.");
        }

        // ------------------------ LUT Build Logic ---------------------------
        private void BuildRecipeIndex()
        {
            _recipesByResult.Clear();

            foreach (var r in _recipes)
            {
                var key = new ResultKey(r.ResultKind, r.ResultItem, r.ResultEquipment);

                if (!_recipesByResult.TryGetValue(key, out var list))
                {
                    list = new List<ChefRecipe>();
                    _recipesByResult[key] = list;
                }

                list.Add(r);
            }
        }

        // TODO: add checks to early exit on circular crafts, or crafts that create/consume the desired resulting item in any quantity.
        private void BuildPlans()
        {
            _plans.Clear();
            var stack = new HashSet<ResultKey>();

            foreach (var kvp in _recipesByResult)
            {
                var target = kvp.Key;
                var plan = new PlanEntry(target);

                EnumerateChainsForTarget(target, plan, stack);

                if (plan.Chains.Count > 0)
                {
                    _plans[target] = plan;

                    // --- DEBUG DUMP START ---
                    string targetName = (target.Kind == RecipeResultKind.Item)
                        ? ItemCatalog.GetItemDef(target.Item)?.name ?? "???"
                        : EquipmentCatalog.GetEquipmentDef(target.Equipment)?.name ?? "???";

                    if (targetName == "CritDamage")
                    {
                        foreach (var chain in plan.Chains)
                        {
                            var sb = new StringBuilder();
                            sb.AppendLine($"[CHAIN] {targetName} (Depth {chain.Depth}):");

                            int stepNum = 1;
                            foreach (var step in chain.Steps)
                            {
                                // 1. Identify Result
                                string stepResult = (step.ResultKind == RecipeResultKind.Item)
                                    ? ItemCatalog.GetItemDef(step.ResultItem)?.name ?? "null"
                                    : EquipmentCatalog.GetEquipmentDef(step.ResultEquipment)?.name ?? "null";

                                // 2. Identify Ingredients
                                var ingNames = new List<string>();
                                foreach (var ing in step.Ingredients)
                                {
                                    string iName = (ing.Kind == IngredientKind.Item)
                                        ? ItemCatalog.GetItemDef(ing.Item)?.name
                                        : EquipmentCatalog.GetEquipmentDef(ing.Equipment)?.name;
                                    ingNames.Add($"{iName} (x{ing.Count})");
                                }

                                sb.AppendLine($"   Step {stepNum}: {string.Join(" + ", ingNames)} -> {stepResult} (x{step.ResultCount})");
                                stepNum++;
                            }

                            // 3. Print Calculated Cost
                            sb.Append($"   >>> CALCULATED COST: ");
                            bool first = true;
                            for (int i = 0; i < chain.TotalItemCost.Length; i++)
                            {
                                if (chain.TotalItemCost[i] > 0)
                                {
                                    if (!first) sb.Append(", ");
                                    sb.Append($"{ItemCatalog.GetItemDef((ItemIndex)i)?.name} (x{chain.TotalItemCost[i]})");
                                    first = false;
                                }
                            }

                            _log.LogInfo(sb.ToString());
                        }
                    }
                    // --- DEBUG DUMP END ---
                }
            }
        }

        private void EnumerateChainsForTarget(ResultKey target, PlanEntry plan, HashSet<ResultKey> stack)
        {
            stack.Clear();
            var currentChain = new List<ChefRecipe>();
            var validChains = new List<RecipeChain>();

            DFS(target, target, 0, currentChain, stack, validChains);

            plan.Chains.Clear();
            plan.Chains.AddRange(validChains);
        }

        private void DFS(
           ResultKey current,
           ResultKey rootTarget,
           int depth,
           List<ChefRecipe> chain,
           HashSet<ResultKey> stack,
           List<RecipeChain> validChains)
        {
            if (depth >= _maxDepth)
            {
                return;
            }

            if (!_recipesByResult.TryGetValue(current, out var options))
            {
                return;
            }


            // Cycle Protection: allows "profit loops" but prevents infinite recursion
            bool isRecursiveLoop = stack.Contains(current);
            if (!isRecursiveLoop)
            {
                stack.Add(current);
            }

            foreach (var recipe in options)
            {
                if (IsCycle1Recipe(recipe))
                {
                    continue;
                }

                chain.Add(recipe);

                TryFinalizeChain(chain, validChains, rootTarget);

                if (!isRecursiveLoop)
                {
                    foreach (var ingredient in recipe.Ingredients)
                    {
                        ResultKey subKey = (ingredient.Kind == IngredientKind.Item)
                            ? new ResultKey(RecipeResultKind.Item, ingredient.Item, EquipmentIndex.None)
                            : new ResultKey(RecipeResultKind.Equipment, ItemIndex.None, ingredient.Equipment);

                        DFS(subKey, rootTarget, depth + 1, chain, stack, validChains);
                    }
                }
                chain.RemoveAt(chain.Count - 1);
            }
            if (!isRecursiveLoop) stack.Remove(current);
        }

        /// <summary>
        /// Given the current chain, compute its external input signature and,
        /// if it's new for this result, store it as a RecipeChain.
        /// </summary>
        private void TryFinalizeChain(
            List<ChefRecipe> chain,
            List<RecipeChain> validChains,
            ResultKey targetKey)
        {
            if (chain.Count == 0)
            {
                return;
            }

            var externalItems = new int[_itemCount];
            var externalEquip = new int[_equipmentCount];
            bool hasCost = false;

            var currentItems = new int[_itemCount];
            var currentEquip = new int[_equipmentCount];

            for (int s = chain.Count - 1; s >= 0; s--)
            {
                var step = chain[s];

                foreach (var ing in step.Ingredients)
                {
                    if (ing.Kind == IngredientKind.Item)
                    {
                        int idx = (int)ing.Item;
                        if (idx >= 0 && idx < _itemCount)
                        {
                            int needed = ing.Count;
                            if (currentItems[idx] < needed)
                            {
                                int missing = needed - currentItems[idx];
                                externalItems[idx] += missing;
                                currentItems[idx] += missing;
                                hasCost = true;
                            }
                            currentItems[idx] -= needed;
                        }
                    }
                    else
                    {
                        int idx = (int)ing.Equipment;
                        if (idx >= 0 && idx < _equipmentCount)
                        {
                            int needed = ing.Count;
                            if (currentEquip[idx] < needed)
                            {
                                int missing = needed - currentEquip[idx];
                                externalEquip[idx] += missing;
                                currentEquip[idx] += missing;
                                hasCost = true;
                            }
                            currentEquip[idx] -= needed;
                        }
                    }
                }

                if (step.ResultKind == RecipeResultKind.Item)
                {
                    int idx = (int)step.ResultItem;
                    if (idx >= 0 && idx < _itemCount)
                        currentItems[idx] += step.ResultCount;
                }
                else
                {
                    int idx = (int)step.ResultEquipment;
                    if (idx >= 0 && idx < _equipmentCount)
                        currentEquip[idx] += step.ResultCount;
                }
            }

            // ---------- Yield Calculation -----------
            int finalBalance = (targetKey.Kind == RecipeResultKind.Item)
                ? currentItems[(int)targetKey.Item]
                : currentEquip[(int)targetKey.Equipment];

            int startupCost = (targetKey.Kind == RecipeResultKind.Item)
                ? externalItems[(int)targetKey.Item]
                : externalEquip[(int)targetKey.Equipment];

            if (targetKey.Item != ItemIndex.None && ItemCatalog.GetItemDef(targetKey.Item)?.name == "CritDamage")
            {
                if (startupCost > 0)
                {
                    _log.LogInfo($"[YIELD CHECK] Chain Depth {chain.Count}");
                    _log.LogInfo($"   Target: {ItemCatalog.GetItemDef(targetKey.Item).name}");
                    _log.LogInfo($"   Produced: {finalBalance} | Cost: {startupCost}");
                    _log.LogInfo($"   Result: {finalBalance - startupCost}");
                }
            }

            if ((finalBalance - startupCost) <= 0)
            {
                return;
            }

            if (!hasCost)
            {
                _log.LogInfo($"Skipping entry for target {targetKey.Kind}");
                return;
            }

            bool isDuplicate = false;

            var comparisonBuffer = new List<ChefRecipe>(chain.Count);
            foreach (var valid in validChains)
            {
                if (valid.Steps.Count != chain.Count)
                {
                    continue; // if length doesnt match, early exit iteration for optimization, as this must not be a permutation
                }

                comparisonBuffer.Clear();
                comparisonBuffer.AddRange(valid.Steps);

                bool match = true;
                foreach (var recipe in chain)
                {
                    if (!comparisonBuffer.Remove(recipe))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (!isDuplicate)
            {
                validChains.Add(new RecipeChain(chain, externalItems, externalEquip));
            }
        }

        // ------------------------ Runtime query API ---------------------------
        /// <summary>
        /// Given a snapshot of item stacks (indexed by ItemCatalog.itemCount),
        /// compute all craftable results, up to the preconfigured _maxDepth.
        /// </summary>
        public List<CraftableEntry> ComputeCraftable(int[] itemStacks, int[] equipmentStacks)
        {
            var result = new List<CraftableEntry>();
            var affordableChains = new List<RecipeChain>();

            foreach (var (rk, plan) in _plans)
            {
                affordableChains.Clear();

                foreach (var chain in plan.Chains)
                {
                    if (CanAffordChain(itemStacks, equipmentStacks, chain))
                    {
                        affordableChains.Add(chain);
                    }
                }

                if (affordableChains.Count == 0)
                {
                    continue;
                }

                // sort primary ResultCount, secondary Depth
                affordableChains.Sort((a, b) =>
                {
                    int c = a.ResultCount.CompareTo(b.ResultCount);
                    if (c != 0) return c;
                    return a.Depth.CompareTo(b.Depth);
                });

                int currentCount = -1;
                var currentGroup = new List<RecipeChain>();

                foreach (var chain in affordableChains)
                {
                    if (chain.ResultCount != currentCount)
                    {
                        if (currentGroup.Count > 0)
                        {
                            EmitCraftableGroup(result, rk, currentCount, currentGroup);
                        }
                        currentCount = chain.ResultCount;
                        currentGroup.Clear();
                    }
                    currentGroup.Add(chain);
                }
                if (currentGroup.Count > 0)
                {
                    EmitCraftableGroup(result, rk, currentCount, currentGroup);
                }
            }

            // sort primary item tier, secondary alphanumeric
            result.Sort(TierManager.CompareCraftableEntries);

            _log.LogDebug($"CraftPlanner.ComputeCraftable: {result.Count} entries from " + $"{itemStacks.Length} items / {equipmentStacks.Length} equipment.");

            OnCraftablesUpdated?.Invoke(result);
            return result;
        }



        /// <summary>
        /// Checks whether the given inventory satisfies the required external item costs for the specified chain.
        /// </summary>
        private bool CanAffordChain(int[] itemStacks, int[] equipmentStacks, RecipeChain chain)
        {
            var itemCost = chain.TotalItemCost;
            for (int i = 0; i < itemCost.Length; i++)
            {
                if (itemCost[i] > 0 && itemStacks[i] < itemCost[i])
                {
                    return false;
                }
            }
            var equipCost = chain.TotalEquipmentCost;

            for (int i = 0; i < equipCost.Length; i++)
            {
                if (i < equipmentStacks.Length)
                {
                    if (equipCost[i] > 0 && equipmentStacks[i] < equipCost[i]) return false;
                }
            }

            return true;
        }

        private static void EmitCraftableGroup(List<CraftableEntry> result, ResultKey rk, int count, List<RecipeChain> group)
        {
            result.Add(new CraftableEntry
            {
                ResultKind = rk.Kind,
                ResultItem = rk.Item,
                ResultEquipment = rk.Equipment,
                ResultCount = count,
                MinDepth = group[0].Depth,
                Chains = new List<RecipeChain>(group)
            });
        }

        private bool IsCycle1Recipe(ChefRecipe recipe)
        {
            foreach (var ing in recipe.Ingredients)
            {
                // Check for Item Self-Consumption
                if (ing.Kind == IngredientKind.Item && recipe.ResultKind == RecipeResultKind.Item)
                {
                    if (ing.Item == recipe.ResultItem) return true;
                }
                // Check for Equipment Self-Consumption
                else if (ing.Kind == IngredientKind.Equipment && recipe.ResultKind == RecipeResultKind.Equipment)
                {
                    if (ing.Equipment == recipe.ResultEquipment) return true;
                }
            }
            return false;
        }

        // ------------------------ Types ---------------------------
        /// <summary>
        /// Describes one craftable result (item or equipment) and the chains that are currently affordable from a given inventory snapshot.
        /// Chains are sorted by increasing depth, where MinDepth is simply Chains[0].Depth
        /// </summary>
        internal sealed class CraftableEntry
        {
            public RecipeResultKind ResultKind;
            public ItemIndex ResultItem;
            public EquipmentIndex ResultEquipment;
            public int ResultCount;
            public int MinDepth;
            public List<RecipeChain> Chains = new();
        }

        /// <summary>
        /// A single recipe chain: ordered list of recipes plus cached external costs.
        /// </summary>
        internal sealed class RecipeChain
        {
            public IReadOnlyList<ChefRecipe> Steps { get; }
            public int Depth => Steps.Count;
            public int[] TotalItemCost { get; }
            public int[] TotalEquipmentCost { get; }
            public int ResultCount { get; }

            public RecipeChain(List<ChefRecipe> steps, int[] items, int[] equip)
            {
                Steps = steps.ToArray();
                TotalItemCost = items;
                TotalEquipmentCost = equip;
                ResultCount = (steps.Count > 0) ? Math.Max(1, steps[0].ResultCount) : 1;
            }
        }

        /// <summary>
        /// All chains for a given result.
        /// </summary>
        internal sealed class PlanEntry
        {
            public ResultKey Result { get; }
            public List<RecipeChain> Chains { get; } = new();
            public PlanEntry(ResultKey result) { Result = result; }
        }

        /// <summary>
        /// Value type key representing "what result does this recipe produce"
        /// </summary>
        internal readonly struct ResultKey : IEquatable<ResultKey>
        {
            public readonly RecipeResultKind Kind;
            public readonly ItemIndex Item;
            public readonly EquipmentIndex Equipment;

            public ResultKey(RecipeResultKind kind, ItemIndex item, EquipmentIndex equipment)
            {
                Kind = kind;
                Item = item;
                Equipment = equipment;
            }
            public bool Equals(ResultKey other) => Kind == other.Kind && Item == other.Item && Equipment == other.Equipment;
            public override bool Equals(object obj) => obj is ResultKey other && Equals(other);
            public override int GetHashCode() => (int)Kind ^ (int)Item ^ (int)Equipment;
        }
    }
}
