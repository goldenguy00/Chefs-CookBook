using BepInEx.Logging;
using RoR2;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static class CraftingExecutionHandler
    {
        private static ManualLogSource _log;
        private static Coroutine _craftingRoutine;
        private static MonoBehaviour _runner;

        public static bool IsAutoCrafting => _craftingRoutine != null;

        internal static void Init(ManualLogSource log, MonoBehaviour runner)
        {
            _log = log;
            _runner = runner;
        }

        public static void ExecuteChain(CraftPlanner.RecipeChain chain)
        {
            Abort();
            _craftingRoutine = _runner.StartCoroutine(CraftChainRoutine(chain));
        }

        public static void Abort()
        {
            if (_craftingRoutine != null) _runner.StopCoroutine(_craftingRoutine);
            _craftingRoutine = null;
            CraftingObjectiveTracker.CurrentObjectiveText = string.Empty;

            if (StateController.ActiveCraftingController) CraftUI.CloseCraftPanel(StateController.ActiveCraftingController);
            _log.LogInfo("[ExecutionHandler] Craft aborted.");
        }

        private static IEnumerator CraftChainRoutine(CraftPlanner.RecipeChain chain)
        {
            _log.LogInfo($"[Execution] Starting Chain. Scrap Requirements: {chain.DroneCostSparse?.Length ?? 0}. Recipe Steps: {chain.Steps.Count}");

            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (!body)
            {
                Abort();
                yield break;
            }

            if (chain.DroneCostSparse != null && chain.DroneCostSparse.Length > 0)
            {
                _log.LogInfo($"[Chain] Requirements: {chain.DroneCostSparse.Length} scrap types needed.");

                foreach (var req in chain.DroneCostSparse)
                {
                    PickupIndex pickupIndex;
                    if (req.IsItem) pickupIndex = PickupCatalog.FindPickupIndex((ItemIndex)req.UnifiedIndex);
                    else pickupIndex = PickupCatalog.FindPickupIndex((EquipmentIndex)(req.UnifiedIndex - ItemCatalog.itemCount));

                    var itemDef = PickupCatalog.GetPickupDef(pickupIndex);
                    if (itemDef == null) continue;

                    int currentCount = GetOwnedCount(itemDef, body);

                    if (currentCount < req.Count)
                    {
                        int needed = req.Count - currentCount;

                        string droneName = "Drone";
                        DroneIndex bestCandidate = InventoryTracker.GetScrapCandidate(req.UnifiedIndex);

                        if (bestCandidate != DroneIndex.None)
                        {
                            if (DroneCatalog.GetDroneDef(bestCandidate)?.bodyPrefab?.GetComponent<CharacterBody>() is CharacterBody droneBody) droneName = Language.GetString(droneBody.baseNameToken);
                        }

                        string scrapName = Language.GetString(itemDef.nameToken);
                        string msg = $"Scrap {droneName} for {scrapName}";
                        CraftingObjectiveTracker.SetObjective(msg, true);

                        _log.LogInfo($"[Execution] Waiting for {needed}x {scrapName}...");
                        yield return WaitForPendingPickup(pickupIndex, needed);
                    }
                }

                _log.LogInfo("[Execution] All scrap requirements met.");
            }

            Queue<ChefRecipe> craftQueue = new Queue<ChefRecipe>(chain.Steps.Reverse());
            PickupIndex lastPickup = PickupIndex.none;
            int lastQty = 0;

            while (craftQueue.Count > 0)
            {
                var localUser = LocalUserManager.GetFirstLocalUser();
                body = localUser?.cachedBody;
                var interactor = body?.GetComponent<Interactor>();

                if (lastPickup != PickupIndex.none)
                {
                    var def = PickupCatalog.GetPickupDef(lastPickup);
                    CraftingObjectiveTracker.SetObjective($"Collect {Language.GetString(def?.nameToken ?? "Result")}", true);
                    yield return WaitForPendingPickup(lastPickup, lastQty);
                }

                CraftingObjectiveTracker.SetObjective("Approach Wandering CHEF", true);

                while (StateController.ActiveCraftingController == null)
                {
                    if (!StateController.TargetCraftingObject || !body)
                    {
                        Abort();
                        yield break;
                    }

                    float maxDist = interactor.maxInteractionDistance + 6f;
                    float distSqr = (body.corePosition - StateController.TargetCraftingObject.transform.position).sqrMagnitude;

                    if (distSqr > (maxDist * maxDist))
                    {
                        yield return new WaitForSeconds(0.2f);
                        continue;
                    }

                    if (interactor) interactor.AttemptInteraction(StateController.TargetCraftingObject);
                    for (int i = 0; i < 5; i++)
                    {
                        if (StateController.ActiveCraftingController != null) break;
                        yield return new WaitForFixedUpdate();
                    }
                    if (StateController.ActiveCraftingController == null) yield return new WaitForSeconds(0.2f);
                }

                float initTimeout = 1.0f;
                while (StateController.ActiveCraftingController &&
                       (StateController.ActiveCraftingController.options == null ||
                        StateController.ActiveCraftingController.options.Length == 0))
                {
                    initTimeout -= Time.fixedDeltaTime;
                    if (initTimeout <= 0f) break;
                    yield return new WaitForFixedUpdate();
                }

                ChefRecipe step = craftQueue.Dequeue();
                string stepName = GetStepName(step);
                CraftingObjectiveTracker.SetObjective($"Processing {stepName}...", true);

                StateController.ActiveCraftingController.ClearAllSlots();
                if (!SubmitIngredients(StateController.ActiveCraftingController, step))
                {
                    _log.LogWarning($"Missing ingredients for {stepName}. Aborting.");
                    Abort();
                    yield break;
                }

                while (!StateController.ActiveCraftingController.AllSlotsFilled())
                {
                    yield return new WaitForFixedUpdate();
                }

                StateController.ActiveCraftingController.ConfirmSelection();
                CraftUI.CloseCraftPanel(StateController.ActiveCraftingController);

                lastQty = step.ResultCount;
                lastPickup = GetPickupIndex(step);

                if (craftQueue.Count > 0) yield return new WaitForSeconds(0.2f);
            }

            _log.LogInfo("[ExecutionHandler] Chain Complete.");
            Abort();
        }

        private static IEnumerator WaitForPendingPickup(PickupIndex pickupIndex, int expectedGain)
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (!body || !body.inventory) yield break;

            var def = PickupCatalog.GetPickupDef(pickupIndex);
            if (def == null) yield break;

            int targetCount = GetOwnedCount(def, body) + expectedGain;

            while (true)
            {
                if (GetOwnedCount(def, body) >= targetCount)
                {
                    _log.LogInfo($"[Chain] Confirmed pickup of {def.internalName}.");
                    yield break;
                }
                yield return new WaitForSeconds(0.1f);
            }
        }

        private static bool SubmitIngredients(CraftingController controller, ChefRecipe recipe)
        {
            var options = controller.options;
            HashSet<PickupIndex> available = new HashSet<PickupIndex>();

            foreach (var opt in options) if (opt.available) available.Add(opt.pickup.pickupIndex);

            foreach (var ing in recipe.Ingredients)
            {
                PickupIndex target = ing.IsItem
                    ? PickupCatalog.FindPickupIndex(ing.ItemIndex)
                    : PickupCatalog.FindPickupIndex(ing.EquipIndex);

                if (target == PickupIndex.none || !available.Contains(target))
                {
                    _log.LogWarning($"[CookBook] Missing ingredient: {target}");
                    return false;
                }
                controller.SendToSlot(target.value);
            }
            return true;
        }

        private static int GetOwnedCount(PickupDef def, CharacterBody body)
        {
            if (def.itemIndex != ItemIndex.None) return body.inventory.GetItemCountPermanent(def.itemIndex);
            if (def.equipmentIndex != EquipmentIndex.None) return body.inventory.currentEquipmentIndex == def.equipmentIndex ? 1 : 0;
            return 0;
        }

        private static string GetStepName(ChefRecipe step)
        {
            if (step.ResultIndex < ItemCatalog.itemCount)
                return Language.GetString(ItemCatalog.GetItemDef((ItemIndex)step.ResultIndex)?.nameToken);
            return Language.GetString(EquipmentCatalog.GetEquipmentDef((EquipmentIndex)(step.ResultIndex - ItemCatalog.itemCount))?.nameToken);
        }

        private static PickupIndex GetPickupIndex(ChefRecipe step)
        {
            if (step.ResultIndex < ItemCatalog.itemCount) return PickupCatalog.FindPickupIndex((ItemIndex)step.ResultIndex);
            return PickupCatalog.FindPickupIndex((EquipmentIndex)(step.ResultIndex - ItemCatalog.itemCount));
        }
    }
}