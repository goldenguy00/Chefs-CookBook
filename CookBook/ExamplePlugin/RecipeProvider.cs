using BepInEx.Logging;
using RoR2;
using RoR2.ContentManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CookBook
{
    /// <summary>
    /// Holds all chef crafting recipes.
    /// </summary>
    internal static class RecipeProvider
    {
        private static ManualLogSource _log;
        private static bool _initialized;
        private static bool _recipesBuilt;

        // Internal storage for recipes
        private static readonly List<ChefRecipe> _recipes = new List<ChefRecipe>();

        /// <summary>Public, read-only view of recipes.</summary>
        internal static IReadOnlyList<ChefRecipe> Recipes => _recipes;

        // fired when recipes are ready to prompt planner build 
        internal static event System.Action<IReadOnlyList<ChefRecipe>> OnRecipesBuilt;

        //--------------------------- LifeCycle -------------------------------
        /// <summary>
        /// Called once from CookBook.Awake().
        /// </summary>
        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;

            ContentManager.onContentPacksAssigned += OnContentPacksAssigned; // subscribe to pack events to ensure recipes are built after all other recipes are handled
        }

        internal static void Shutdown()
        {
            ContentManager.onContentPacksAssigned -= OnContentPacksAssigned;
        }

        //--------------------------- ContentPack Tracking -------------------------------
        internal static void OnContentPacksAssigned(HG.ReadOnlyArray<ReadOnlyContentPack> _)
        {
            if (_recipesBuilt)
                return;

            CraftableCatalog.availability.CallWhenAvailable(() =>
            {
                if (_recipesBuilt)
                    return;

                BuildRecipes();
            });
        }

        /// <summary>
        /// Build the list of chef recipes from CraftableDef
        /// </summary>
        /// 
        private static void BuildRecipes()
        {
            _recipes.Clear();

            var recipesArray = CraftableCatalog.GetAllRecipes();
            if (recipesArray == null || recipesArray.Length == 0)
            {
                _log.LogWarning("RecipeProvider: no recipes returned from CraftableCatalog.GetAllRecipes().");
                return;
            }

            foreach (var recipeEntry in recipesArray)
            {
                if (recipeEntry == null)
                {
                    continue;
                }

                // ---------------- Result pickup ----------------
                PickupIndex resultPickup = recipeEntry.result;
                if (!resultPickup.isValid || resultPickup == PickupIndex.none)
                {
                    continue;
                }

                PickupDef resultPickupDef = PickupCatalog.GetPickupDef(resultPickup);
                if (resultPickupDef == null)
                {
                    continue;
                }

                RecipeResultKind resultKind;
                ItemIndex resultItemidx = ItemIndex.None;
                EquipmentIndex resultEquipmentidx = EquipmentIndex.None;

                if (resultPickupDef.itemIndex != ItemIndex.None)
                {
                    resultKind = RecipeResultKind.Item;
                    resultItemidx = resultPickupDef.itemIndex;
                }
                else if (resultPickupDef.equipmentIndex != EquipmentIndex.None)
                {
                    resultKind = RecipeResultKind.Equipment;
                    resultEquipmentidx = resultPickupDef.equipmentIndex;
                }
                else
                {
                    continue;
                }

                int resultCount = recipeEntry.amountToDrop;

                // ---------------- Ingredient pickups ----------------
                List<PickupIndex> ingredientPickups = recipeEntry.GetAllPickups();
                if (ingredientPickups == null || ingredientPickups.Count == 0)
                {
                    continue;
                }

                var rawIngredients = new List<Ingredient>();

                foreach (var ingPi in ingredientPickups)
                {
                    if (!ingPi.isValid || ingPi == PickupIndex.none)
                    {
                        continue;
                    }

                    PickupDef ingDef = PickupCatalog.GetPickupDef(ingPi);
                    if (ingDef == null)
                    {
                        continue;
                    }

                    if (ingDef.itemIndex != ItemIndex.None)
                    {
                        rawIngredients.Add(new Ingredient(IngredientKind.Item, ingDef.itemIndex, EquipmentIndex.None, 1));
                    }
                    else if (ingDef.equipmentIndex != EquipmentIndex.None)
                    {
                        rawIngredients.Add(new Ingredient(IngredientKind.Equipment, ItemIndex.None, ingDef.equipmentIndex, 1));
                    }
                }

                if (rawIngredients.Count == 0)
                {
                    continue;
                }

                if (rawIngredients.Count <= 2)
                {
                    var recipe = new ChefRecipe(resultKind, resultItemidx, resultEquipmentidx, resultCount, rawIngredients.ToArray());
                    _recipes.Add(recipe);
                    PrintRecipeDebug(recipe, resultPickupDef.internalName);
                }
                else
                {
                    var baseIng = rawIngredients[0];
                    for (int i = 1; i < rawIngredients.Count; i++)
                    {
                        var variantIng = rawIngredients[i];

                        var pair = new Ingredient[] { baseIng, variantIng };

                        var recipe = new ChefRecipe(resultKind, resultItemidx, resultEquipmentidx, resultCount, pair);
                        _recipes.Add(recipe);
                        PrintRecipeDebug(recipe, resultPickupDef.internalName);
                    }
                }
            }
            _recipesBuilt = true;
            _log.LogInfo($"RecipeProvider: Built {_recipes.Count} explicit recipes from game data.");
            OnRecipesBuilt?.Invoke(_recipes); // Notify listeners that recipes are ready
        }
        private static void PrintRecipeDebug(ChefRecipe r, string resultName)
        {
            string logMsg = $"[RECIPE] Result: {resultName} (x{r.ResultCount}) | Ingredients: ";
            foreach (var ing in r.Ingredients)
            {
                string name = (ing.Kind == IngredientKind.Item)
                    ? ItemCatalog.GetItemDef(ing.Item)?.name ?? "null"
                    : EquipmentCatalog.GetEquipmentDef(ing.Equipment)?.name ?? "null";
                logMsg += $"{name} (x{ing.Count}), ";
            }
            _log.LogInfo(logMsg);
        }
    }

    /// <summary>recipe result type</summary>
    internal enum RecipeResultKind
    {
        Item,
        Equipment
    }

    /// <summary>recipe ingredient type</summary>
    internal enum IngredientKind
    {
        Item,
        Equipment
    }

    /// <summary>ingredient entry</summary>
    internal readonly struct Ingredient
    {
        public readonly IngredientKind Kind;
        public readonly ItemIndex Item;
        public readonly EquipmentIndex Equipment;
        public readonly int Count;

        public Ingredient(IngredientKind kind, ItemIndex item, EquipmentIndex equipment, int count)
        {
            Kind = kind;
            Item = item;
            Equipment = equipment;
            Count = count;
        }
    }

    /// <summary>result entry</summary>
    internal sealed class ChefRecipe
    {
        public RecipeResultKind ResultKind { get; }
        public ItemIndex ResultItem { get; }
        public EquipmentIndex ResultEquipment { get; }
        public int ResultCount { get; }
        public Ingredient[] Ingredients { get; }

        public ChefRecipe(
            RecipeResultKind resultKind,
            ItemIndex resultItem,
            EquipmentIndex resultEquipment,
            int resultCount,
            Ingredient[] ingredients)
        {
            ResultKind = resultKind;
            ResultItem = resultItem;
            ResultEquipment = resultEquipment;
            ResultCount = resultCount;
            Ingredients = ingredients;
        }
    }
}