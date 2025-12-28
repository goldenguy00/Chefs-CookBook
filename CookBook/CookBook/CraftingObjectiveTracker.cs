using RoR2;
using RoR2.UI;
using System.Collections.Generic;
using UnityEngine;

namespace CookBook
{
    internal static class CraftingObjectiveTracker
    {
        private static string _currentObjectiveText = string.Empty;

        /// <summary>
        /// Updates the text shown on the local player's objective HUD.
        /// </summary>
        public static string CurrentObjectiveText
        {
            get => _currentObjectiveText;
            set => _currentObjectiveText = value;
        }

        internal static void Init()
        {
            // Subscribe to the global objective collector
            ObjectivePanelController.collectObjectiveSources += OnCollectObjectiveSources;
        }

        internal static void Cleanup()
        {
            ObjectivePanelController.collectObjectiveSources -= OnCollectObjectiveSources;
            _currentObjectiveText = string.Empty;
        }

        private static void OnCollectObjectiveSources(CharacterMaster viewerMaster, List<ObjectivePanelController.ObjectiveSourceDescriptor> output)
        {
            if (viewerMaster != LocalUserManager.GetFirstLocalUser()?.cachedMaster) return;

            if (!string.IsNullOrEmpty(_currentObjectiveText))
            {
                output.Add(new ObjectivePanelController.ObjectiveSourceDescriptor
                {
                    source = viewerMaster,
                    master = viewerMaster,
                    objectiveType = typeof(ChefObjectiveTracker)
                });
            }
        }

        internal static void SetObjective(string message, bool canAbort = false)
        {
            if (string.IsNullOrEmpty(message))
            {
                CurrentObjectiveText = string.Empty;
                return;
            }

            if (canAbort)
            {
                CurrentObjectiveText = $"{message} <style=cSub>(Hold left alt to Cancel)</style>";
            }
            else
            {
                CurrentObjectiveText = message;
            }
        }

        private class ChefObjectiveTracker : ObjectivePanelController.ObjectiveTracker
        {
            protected override bool IsDirty() => cachedString != CurrentObjectiveText;
            protected override string GenerateString() => CurrentObjectiveText;
        }
    }
}