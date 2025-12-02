using BepInEx.Logging;
using UnityEngine;
using RoR2;

namespace CookBook
{
    internal static class ChefStateController
    {
        private static ManualLogSource _log;
        private static bool _initialized;
        private static CraftPlanner _planner;
        private static bool _inChefStage;

        internal static void Init(ManualLogSource log, CraftPlanner planner)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;
            _planner = planner;

            InventoryTracker.Init(log);

            Stage.onStageStartGlobal += OnStageStart;
            Run.onRunDestroyGlobal += OnRunDestroy;

            log.LogInfo("ChefStateController.Init()");
        }

        //-------------------------------- Stage Tracking ----------------------------------
        /// <summary>
        /// called whenever a new Stage instance starts, use for scene-based system control
        /// </summary>
        private static void OnStageStart(Stage stage)
        {
            var sceneDef = stage ? stage.sceneDef : null;
            var sceneName = sceneDef ? sceneDef.baseSceneName : "<null>";

            _log.LogInfo($"ChefStateController.OnStageStart: {sceneName}");

            if (IsChefStage(sceneDef))
            {
                _log.LogInfo("Entering Chef stage, enabling InventoryTracker.");
                InventoryTracker.Enable();
                // TODO: hook Chef NPC interaction here.
                // TODO: enable UI handling (invisible)
            }
            else
            {
                _log.LogInfo("Not Chef stage, disabling InventoryTracker.");
                InventoryTracker.Disable();
                // TODO: disable custom UI
            }
        }

        //--------------------------------------- Helpers ----------------------------------------
        /// <summary>
        /// Check if the current SceneDef corresponds to the Chef stage
        /// </summary>
        private static bool IsChefStage(SceneDef sceneDef)
        {
            return sceneDef && sceneDef.baseSceneName == "computationalexchange";
        }

        /// <summary>
        /// Cleanup Chef State Tracker when the run ends
        /// </summary>
        private static void OnRunDestroy(Run run)
        {
            _log.LogInfo("Run ended -> disabling InventoryTracker and resetting Chef state.");
            _inChefStage = false;
            InventoryTracker.Disable();
        }

        // TODO: Chef dialogue detection
        // - Hook the Chef NPC’s interaction component
        // - Detect when the player opens/closes that dialogue
        // - Show/hide cookbook UI only while that dialogue is open

        /// <summary>
        /// retrieve the current SceneDef (if any)
        /// </summary>
        internal static SceneDef GetCurrentScene()
        {
            return Stage.instance ? Stage.instance.sceneDef : null;
        }
    }
}
