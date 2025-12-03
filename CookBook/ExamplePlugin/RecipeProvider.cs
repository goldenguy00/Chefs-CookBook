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


        /// <summary>
        /// Called once from CookBook.Awake().
        /// </summary>
        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;
            _log.LogInfo("RecipeProvider.Init()");

            ContentManager.onContentPacksAssigned += OnContentPacksAssigned; // subscribe to pack events to ensure recipes are built after all other recipes are handled
        }

        //--------------------------- ContentPack Tracking -------------------------------
        private static void OnContentPacksAssigned(HG.ReadOnlyArray<ReadOnlyContentPack> _)
        {
            if (_recipesBuilt)
                return;

            ItemCatalog.availability.CallWhenAvailable(() =>
            {
                if (_recipesBuilt)
                    return;

                BuildRecipes();
                ContentManager.onContentPacksAssigned -= OnContentPacksAssigned;
            });
        }

        /// <summary>
        /// Build the list of chef recipes from CraftableDef
        /// </summary>
        /// 
        private static void BuildRecipes()
        {
            _recipes.Clear(); // initialize

            _log.LogInfo("RecipeProvider: Content packs assigned, building recipes from CraftableDef via reflection");

            // Find the CraftableDef type in ANY loaded assembly
            Type craftableDefType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("RoR2.CraftableDef", throwOnError: false))
                .FirstOrDefault(t => t != null);

            if (craftableDefType == null)
            {
                _log.LogError("RecipeProvider: could not find RoR2.CraftableDef type in loaded assemblies.");
                return;
            }

            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // get all CraftableDef instances via Resources.FindObjectsOfTypeAll(Type)
            UnityEngine.Object[] craftableObjects = Resources.FindObjectsOfTypeAll(craftableDefType);
            if (craftableObjects == null || craftableObjects.Length == 0)
            {
                _log.LogWarning("RecipeProvider: no CraftableDef instances found.");
                return;
            }
            else
            {
                _log.LogInfo($"RecipeProvider: found {craftableObjects.Length} CraftableDef instances via Resources.");
            }

            FieldInfo pickupField = craftableDefType.GetField("pickup", FLAGS);
            FieldInfo recipesField = craftableDefType.GetField("recipes", FLAGS);

            if (pickupField == null || recipesField == null)
            {
                _log.LogError("RecipeProvider: CraftableDef is missing 'pickup' or 'recipes' fields");
                return;
            }

            foreach (var craftableObj in craftableObjects)
            {
                if (craftableObj == null)
                {
                    continue;
                }

                object craftable = craftableObj;

                // Result pickup (item or equipment)
                var resultPickupObj = pickupField.GetValue(craftable) as UnityEngine.Object;
                if (resultPickupObj == null)
                {
                    _log.LogDebug("RecipeProvider: resultPickupObj is null");
                    continue;
                }

                // ---------------- Result parse (item/equipment) ----------------
                RecipeResultKind resultKind;
                ItemIndex resultItemidx = ItemIndex.None;
                EquipmentIndex resultEquipmentidx = EquipmentIndex.None;

                if (resultPickupObj is ItemDef resultItemDef)
                {
                    resultKind = RecipeResultKind.Item;
                    resultItemidx = ResolveItemIndex(resultItemDef);

                    if (resultItemidx == ItemIndex.None)
                    {
                        _log.LogDebug($"RecipeProvider: result ItemDef '{resultItemDef.name}' has no ItemIndex – skipping.");
                        continue;
                    }

                    _log.LogDebug($"RecipeProvider: craftable '{craftableObj.name}' ItemDef name={resultItemDef.name}, " + $"resolvedIndex={resultItemidx}");
                }
                else if (resultPickupObj is EquipmentDef resultEqDef)
                {
                    resultKind = RecipeResultKind.Equipment;
                    resultEquipmentidx = ResolveEquipmentIndex(resultEqDef);

                    if (resultEquipmentidx == EquipmentIndex.None)
                    {
                        _log.LogDebug($"RecipeProvider: result EquipmentDef '{resultEqDef.name}' has no EquipmentIndex – skipping.");
                        continue;
                    }

                    _log.LogDebug($"RecipeProvider: craftable '{craftableObj.name}' EquipmentDef name={resultEqDef.name}, " + $"resolvedIndex={resultEquipmentidx}");
                }
                else
                {
                    _log.LogDebug($"RecipeProvider: craftable '{craftableObj.name}' pickup type {resultPickupObj.GetType().FullName} unsupported – skipping.");
                    continue;
                }

                // build recipes array
                var recipesArray = recipesField.GetValue(craftable) as Array;
                if (recipesArray == null || recipesArray.Length == 0)
                {
                    _log.LogDebug("RecipeProvider: recipesArray is null or empty");
                    continue;
                }

                foreach (var recipeObj in recipesArray)
                {
                    if (recipeObj == null)
                    {
                        continue;
                    }

                    // attempt to fill in recipe data from binary
                    Type recipeType = recipeObj.GetType();
                    FieldInfo amountField = recipeType.GetField("amountToDrop", FLAGS);
                    FieldInfo ingredientsField = recipeType.GetField("ingredients", FLAGS);

                    if (amountField == null || ingredientsField == null)
                    {
                        _log.LogDebug($"RecipeProvider: recipe type '{recipeType.FullName}' missing amountToDrop/ingredients – skipping.");
                        continue;
                    }

                    int resultCount = (int)amountField.GetValue(recipeObj);
                    var ingredientsArray = ingredientsField.GetValue(recipeObj) as Array;
                    if (ingredientsArray == null || ingredientsArray.Length == 0)
                    {
                        _log.LogDebug("RecipeProvider: ingredientsArray is null or empty");
                        continue;
                    }

                    var ingList = new List<Ingredient>();

                    foreach (var ingObj in ingredientsArray)
                    {
                        if (ingObj == null)
                        {
                            continue;
                        }

                        Type ingType = ingObj.GetType();
                        FieldInfo ingPickupField = ingType.GetField("pickup", FLAGS);

                        if (ingPickupField == null)
                        {
                            _log.LogDebug($"RecipeProvider: ingredient type '{ingType.FullName}' missing pickup field – skipping.");
                            continue;
                        }

                        var ingPickupObj = ingPickupField.GetValue(ingObj) as UnityEngine.Object;
                        if (ingPickupObj == null)
                        {
                            continue;
                        }

                        // ---------------- ingredient parse (item/equipment) ----------------
                        if (ingPickupObj is ItemDef ingItemDef)
                        {
                            ItemIndex ingIndex = ResolveItemIndex(ingItemDef);
                            if (ingIndex == ItemIndex.None)
                            {
                                _log.LogDebug($"RecipeProvider: ingredient ItemDef '{ingItemDef.name}' has no ItemIndex – skipping.");
                                continue;
                            }

                            ingList.Add(new Ingredient(
                                kind: IngredientKind.Item,
                                item: ingIndex,
                                equipment: EquipmentIndex.None,
                                count: 1
                            ));
                        }
                        else if (ingPickupObj is EquipmentDef ingEqDef)
                        {
                            EquipmentIndex ingEqIndex = ResolveEquipmentIndex(ingEqDef);
                            if (ingEqIndex == EquipmentIndex.None)
                            {
                                _log.LogDebug($"RecipeProvider: ingredient EquipmentDef '{ingEqDef.name}' has no EquipmentIndex – skipping.");
                                continue;
                            }

                            ingList.Add(new Ingredient(
                                kind: IngredientKind.Equipment,
                                item: ItemIndex.None,
                                equipment: ingEqIndex,
                                count: 1
                            ));
                        }
                        else
                        {
                            _log.LogDebug($"RecipeProvider: ingredient pickup type '{ingPickupObj.GetType().FullName}' unsupported – skipping.");
                            continue;
                        }
                    }

                    if (ingList.Count > 0)
                    {
                        _recipes.Add(new ChefRecipe(
                            resultKind: resultKind,
                            resultItem: resultItemidx,
                            resultEquipment: resultEquipmentidx,
                            resultCount: resultCount,
                            ingredients: ingList.ToArray()
                        ));
                    }
                }
            }
            _recipesBuilt = true;
            _log.LogInfo($"RecipeProvider: built {_recipes.Count} recipes.");

            if (_log != null && _recipes.Count > 0)
            {
                _log.LogDebug("RecipeProvider: Listing all loaded Chef recipes");

                int index = 0;
                foreach (var recipe in _recipes)
                {
                    string resultName;
                    string resultType;

                    if (recipe.ResultKind == RecipeResultKind.Item)
                    {
                        var def = ItemCatalog.GetItemDef(recipe.ResultItem);
                        resultName = def ? def.nameToken : recipe.ResultItem.ToString();
                        resultType = "Item";
                    }
                    else
                    {
                        var def = EquipmentCatalog.GetEquipmentDef(recipe.ResultEquipment);
                        resultName = def ? def.nameToken : recipe.ResultEquipment.ToString();
                        resultType = "Equipment";
                    }

                    _log.LogDebug($"[{index}] Result: {resultName} ({resultType}), Count: {recipe.ResultCount}");

                    if (recipe.Ingredients != null && recipe.Ingredients.Length > 0)
                    {
                        foreach (var ing in recipe.Ingredients)
                        {
                            string ingName;

                            if (ing.Kind == IngredientKind.Item)
                            {
                                var idef = ItemCatalog.GetItemDef(ing.Item);
                                ingName = idef ? idef.nameToken : ing.Item.ToString();
                            }
                            else
                            {
                                var edef = EquipmentCatalog.GetEquipmentDef(ing.Equipment);
                                ingName = edef ? edef.nameToken : ing.Equipment.ToString();
                            }

                            _log.LogDebug($"      - Ingredient: {ingName} x{ing.Count}");
                        }
                    }
                    else
                    {
                        _log.LogDebug("      - No ingredients?");
                    }

                    index++;
                }

                _log.LogDebug("---- End of recipe list ----");
            }


            OnRecipesBuilt?.Invoke(_recipes); // Notify listeners that recipes are ready
        }

        //--------------------------------------- Index Resolvers ----------------------------------------------
        private static ItemIndex ResolveItemIndex(ItemDef def)
        {
            if (!def) return ItemIndex.None;

            int len = ItemCatalog.itemCount;
            for (int i = 0; i < len; i++)
            {
                ItemIndex idx = (ItemIndex)i;
                ItemDef catalogDef = ItemCatalog.GetItemDef(idx);
                if (!catalogDef)
                    continue;

                if (ReferenceEquals(catalogDef, def))
                    return idx;
            }
            return ItemIndex.None;
        }

        private static EquipmentIndex ResolveEquipmentIndex(EquipmentDef def)
        {
            if (!def) return EquipmentIndex.None;
            var defs = EquipmentCatalog.equipmentDefs;

            for (int i = 0; i < defs.Length; i++)
            {
                if (ReferenceEquals(defs[i], def))
                {
                    return (EquipmentIndex)i;
                }
            }
            return EquipmentIndex.None;
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