using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CookBook
{
    /// <summary>
    /// Computes all items craftable from a starting inventory using Wandering CHEF recipes, up to some max crafting depth.
    /// </summary>
    internal sealed class CraftPlanner
    {
        public int SourceItemCount { get; }
        private readonly IReadOnlyList<ChefRecipe> _recipes;
        private readonly int _itemCount;
        private readonly int _totalDefCount;
        private int _maxDepth;
        private readonly ManualLogSource _log;

        // Lookup tables
        private readonly Dictionary<int, List<ChefRecipe>> _recipesByIngredient = new();

        private readonly Dictionary<int, int> _physScratch = new();
        private readonly Dictionary<int, int> _droneScratch = new();

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
            _totalDefCount = _itemCount + EquipmentCatalog.equipmentCount;
            _log = log;
            SourceItemCount = ItemCatalog.itemCount;
            BuildRecipeIndex();
        }

        // ------------------------ LUT Build Logic ---------------------------
        private void BuildRecipeIndex()
        {
            _recipesByIngredient.Clear();
            foreach (var r in _recipes)
            {
                foreach (var ing in r.Ingredients)
                {
                    if (!_recipesByIngredient.TryGetValue(ing.UnifiedIndex, out var list))
                    {
                        list = new List<ChefRecipe>();
                        _recipesByIngredient[ing.UnifiedIndex] = list;
                    }
                    list.Add(r);
                }
            }
        }

        internal void SetMaxDepth(int newDepth) => _maxDepth = Math.Max(0, newDepth);

        /// <summary>
        /// Given a snapshot of item stacks (indexed by ItemCatalog.itemCount),
        /// compute all craftable results, up to the preconfigured _maxDepth. Now performs breadth traversal at calltime rather than precomputing chains, should reduce mem usage by ~98% with the above dict changes.
        /// </summary>
        public void ComputeCraftable(int[] unifiedStacks)
        {
            if (!StateController.IsChefStage() || unifiedStacks == null || unifiedStacks.Length != _totalDefCount) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var discovered = new Dictionary<int, List<RecipeChain>>();
            var seenSignatures = new HashSet<long>();
            var queue = new Queue<RecipeChain>();

            foreach (var recipe in _recipes)
            {
                if (CanAffordRecipe(unifiedStacks, recipe, null))
                {
                    var (phys, drone) = CalculateSplitCosts(null, recipe);
                    var chain = new RecipeChain(new[] { recipe }, phys, drone);
                    long sig = chain.CanonicalSignature;

                    if (seenSignatures.Add(sig))
                    {
                        AddChainToResults(discovered, queue, chain);
                    }
                }
            }

            for (int d = 2; d <= _maxDepth; d++)
            {
                int layerSize = queue.Count;
                if (layerSize == 0) break;

                for (int i = 0; i < layerSize; i++)
                {
                    var existingChain = queue.Dequeue();

                    if (_recipesByIngredient.TryGetValue(existingChain.ResultIndex, out var nextOptions))
                    {
                        foreach (var nextRecipe in nextOptions)
                        {
                            if (existingChain.HasProduced(nextRecipe.ResultIndex)) continue;

                            if (CanAffordRecipe(unifiedStacks, nextRecipe, existingChain))
                            {
                                long newSig = RecipeChain.CalculateSignatureWithPotentialStep(existingChain.Steps, nextRecipe);

                                if (seenSignatures.Add(newSig))
                                {
                                    var extendedSteps = existingChain.Steps.Concat(new[] { nextRecipe }).ToList();
                                    var (phys, drone) = CalculateSplitCosts(existingChain, nextRecipe);
                                    var newChain = new RecipeChain(extendedSteps, phys, drone);

                                    AddChainToResults(discovered, queue, newChain);
                                }
                            }
                        }
                    }
                }
            }

            var finalResult = discovered.Select(kvp => new CraftableEntry
            {
                ResultIndex = kvp.Key,
                ResultCount = kvp.Value[0].ResultCount,
                MinDepth = kvp.Value.Min(c => c.Depth),
                Chains = kvp.Value.OrderBy(c => c.Depth).ToList()
            }).ToList();

            finalResult.Sort(TierManager.CompareCraftableEntries);

            sw.Stop();
            _log.LogInfo($"CraftUI: ComputeCraftable Calculated all chains in {sw.ElapsedMilliseconds}ms");

            OnCraftablesUpdated?.Invoke(finalResult);
        }

        /// <summary>
        /// Checks whether the given inventory satisfies the required external item costs for the specified chain.
        /// </summary>
        private bool CanAffordRecipe(int[] totalStacks, ChefRecipe recipe, RecipeChain existingChain)
        {
            foreach (var ing in recipe.Ingredients)
            {
                int needed = ing.Count;
                if (existingChain != null && ing.UnifiedIndex == existingChain.ResultIndex) needed--;

                if (needed <= 0) continue;

                int alreadyUsed = existingChain?.GetTotalCostOf(ing.UnifiedIndex) ?? 0;
                if (totalStacks[ing.UnifiedIndex] < alreadyUsed + needed) return false;
            }
            return true;
        }

        private (Ingredient[] phys, Ingredient[] drone) CalculateSplitCosts(RecipeChain old, ChefRecipe next)
        {
            _physScratch.Clear();
            _droneScratch.Clear();

            if (old != null)
            {
                foreach (var ing in old.PhysicalCostSparse) _physScratch[ing.UnifiedIndex] = ing.Count;
                foreach (var ing in old.DroneCostSparse) _droneScratch[ing.UnifiedIndex] = ing.Count;
            }

            foreach (var ing in next.Ingredients)
            {
                if (old != null && ing.UnifiedIndex == old.ResultIndex) continue;

                int needed = ing.Count;
                int physOwned = InventoryTracker.GetPhysicalCount(ing.UnifiedIndex);
                int physAlreadyUsed = _physScratch.TryGetValue(ing.UnifiedIndex, out var val) ? val : 0;

                int physRemaining = Math.Max(0, physOwned - physAlreadyUsed);

                if (physRemaining >= needed)
                {
                    _physScratch[ing.UnifiedIndex] = physAlreadyUsed + needed;
                }
                else
                {
                    // Take what's left of physical, then dip into drone potential
                    _physScratch[ing.UnifiedIndex] = physAlreadyUsed + physRemaining;
                    int droneNeeded = needed - physRemaining;

                    if (!_droneScratch.ContainsKey(ing.UnifiedIndex)) _droneScratch[ing.UnifiedIndex] = 0;
                    _droneScratch[ing.UnifiedIndex] += droneNeeded;
                }
            }

            return (
                _physScratch.Select(kvp => new Ingredient(kvp.Key, kvp.Value)).ToArray(),
                _droneScratch.Select(kvp => new Ingredient(kvp.Key, kvp.Value)).ToArray()
            );
        }

        private void AddChainToResults(Dictionary<int, List<RecipeChain>> results, Queue<RecipeChain> queue, RecipeChain chain)
        {
            if (!results.TryGetValue(chain.ResultIndex, out var list))
            {
                list = new List<RecipeChain>();
                results[chain.ResultIndex] = list;
            }

            list.Add(chain);
            queue.Enqueue(chain);
        }

        // ------------------------ Types ---------------------------
        /// <summary>
        /// Describes one craftable result (item or equipment) and the chains that are currently affordable from a given inventory snapshot.
        /// Chains are sorted by increasing depth, where MinDepth is simply Chains[0].Depth
        /// </summary>
        internal sealed class CraftableEntry
        {
            public int ResultIndex;
            public int ResultCount;
            public int MinDepth;
            public List<RecipeChain> Chains = new();
            public bool IsItem => ResultIndex < ItemCatalog.itemCount;
            public ItemIndex ResultItem => IsItem ? (ItemIndex)ResultIndex : ItemIndex.None;
            public EquipmentIndex ResultEquipment => IsItem ? EquipmentIndex.None : (EquipmentIndex)(ResultIndex - ItemCatalog.itemCount);
        }

        /// <summary>
        /// A single recipe chain: ordered list of recipes plus cached external costs.
        /// </summary>
        internal sealed class RecipeChain
        {
            internal IReadOnlyList<ChefRecipe> Steps { get; }
            internal int Depth => Steps.Count;

            internal Ingredient[] PhysicalCostSparse { get; }
            internal Ingredient[] DroneCostSparse { get; }

            internal int ResultIndex => Steps[0].ResultIndex;
            internal int ResultCount => Steps[0].ResultCount;
            internal long CanonicalSignature { get; }

            internal RecipeChain(IEnumerable<ChefRecipe> steps, IEnumerable<Ingredient> phys, IEnumerable<Ingredient> drones)
            {
                Steps = steps.ToArray();
                PhysicalCostSparse = phys.Where(i => i.Count > 0).ToArray();
                DroneCostSparse = drones.Where(i => i.Count > 0).ToArray();
                CanonicalSignature = CalculateCanonicalSignature(Steps);
            }

            internal bool HasProduced(int resultIndex)
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (Steps[i].ResultIndex == resultIndex) return true;
                }
                return false;
            }

            internal static long CalculateCanonicalSignature(IReadOnlyList<ChefRecipe> chain)
            {
                if (chain == null || chain.Count == 0) return 0xDEADBEEF;
                var hashes = chain.Select(r => r.GetHashCode()).ToList();
                hashes.Sort();
                long sig = 17;
                foreach (int h in hashes) sig = sig * 31 + h;
                return sig;
            }

            internal static long CalculateSignatureWithPotentialStep(IReadOnlyList<ChefRecipe> currentSteps, ChefRecipe next)
            {
                var hashes = currentSteps.Select(r => r.GetHashCode()).ToList();
                hashes.Add(next.GetHashCode());
                hashes.Sort();

                long signature = 17;
                foreach (int h in hashes) signature = signature * 31 + h;
                return signature;
            }

            internal int GetTotalCostOf(int index)
            {
                int total = 0;
                foreach (var i in PhysicalCostSparse) if (i.UnifiedIndex == index) total += i.Count;
                foreach (var i in DroneCostSparse) if (i.UnifiedIndex == index) total += i.Count;
                return total;
            }
        }
    }
}
