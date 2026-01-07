using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    internal static class RecipeTrackerUI
    {
        private static RectTransform _trackerRoot;
        private static ManualLogSource _log;
        private static bool _alignQueued;
        private const int AlignDelayFrames = 1;
        private const float GapPx = 5f;
        private const float PanelHeightPx = 200f; // this shouldn't be fixed, our container must hug the contents and expand with them as the dropdown expands/retracts. 

        // All normalized values are relative to a single strip's height/width
        // All text normalization is relative to strip height

        // TrackerPanel
        private const float TrackerPanelPaddingTopNorm = 0.02f;
        private const float TrackerPanelPaddingBottomNorm = 0.02f;
        private const float TrackerPanelPaddingLeftNorm = 0f;
        private const float TrackerPanelPaddingRightNorm = 0f;
        private const float TrackerPanelElementSpacingNorm = 0.05f; // vertical gap between Rowtop and Dropdown

        // RowTop (stretches to parent)
        private const float RowTopHeightNorm = 1f;
        private const float RowTopTopPaddingNorm = 0.05f;
        private const float RowTopBottomPaddingNorm = 0.05f;
        private const float RowTopElementSpacingNorm = 0.1f; // horizontal spacing between the dropdown arrow, recipe tracker label container, metadata, etc.
        private const float MetaDataColumnWidthNorm = 0.3f;
        private const float MetaDataElementVerticalSpacingNorm = 0.1f;
        private const float DropDownArrowSizeNorm = 0.9f; // Normalized to strip height
        private const float RowTopTextHeightNorm = 0.5f; // Normalized to strip height
        private const float RowTopVerticalBorders = 1f;

        // DropdownContainer stretches to TrackerPanel width
        private const float DropdownContainerSpacingNorm = 0.05f; // Vertical spacing between drop down elements: Search Bar, RecipeContainer

        // SearchBar stretches to DropdownContainer width
        private const float SearchBarContainerHorizontalPaddingNorm = 0.1f;
        private const float SearchBarContainerHeightNorm = 0.8f;
        private const float SearchBarBottomBorderThickness = 1f;

        // RecipeContainer stretches to DropdownContainer width
        private const float RecipeContainerHorizontalPaddingNorm = 0.15f;
        private const int MaxVisibleRecipes = 4;

        // ----- RecipeRow sizing -----
        private const float RecipeRowHeightNorm = 0.75f;
        private const float RecipeRowLeftPaddingNorm = 0.1f;
        private const float RecipeRowRightPaddingNorm = 0.1f;
        private const float RecipeRowIngredientSpacingNorm = 0.1f;
        private const float RecipeRowTextHeightNorm = 0.5f;

        //----- Ingredients -------
        private const float IngredientHeightNorm = 0.9f;
        private const float StackSizeTextHeightPx = 15f;
        private const float StackMargin = 3f;

        public static void Init(ManualLogSource log)
        {
            _log = log;
            On.RoR2.UI.ScoreboardController.Rebuild += OnScoreboardRebuild;
        }

        private static void OnScoreboardRebuild(On.RoR2.UI.ScoreboardController.orig_Rebuild orig, ScoreboardController self)
        {
            // Let vanilla fully rebuild first.
            orig(self);
            if (!self) return;

            var containerRT = self.transform.Find("Container") as RectTransform;
            if (!containerRT) return;

            var strips = containerRT.Find("StripContainer") as RectTransform;
            if (!strips) return;

            if (!_trackerRoot)
                InitializeCookBookUI(containerRT);

            if (!_alignQueued)
            {
                _alignQueued = true;
                self.StartCoroutine(AlignCookbookNextFrames(self, containerRT, strips));
            }

            CleanupDiagnostics(containerRT);
            CraftUI.AddBorder(containerRT, Color.white, 2f, 2f, 2f, 2f);
            CraftUI.AddBorder(strips, Color.green, 2f, 2f, 2f, 2f);
            if (_trackerRoot) CraftUI.AddBorder(_trackerRoot, Color.magenta, 2f, 2f, 2f, 2f);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n=== COOKBOOK SIBLING HIERARCHY DUMP ===");
            RecursiveDump(containerRT, sb, 0);
            DebugLog.Trace(_log, sb.ToString());
        }

        private static IEnumerator AlignCookbookNextFrames(ScoreboardController self, RectTransform containerRT, RectTransform strips)
        {
            for (int i = 0; i < AlignDelayFrames; i++)
                yield return null;

            _alignQueued = false;

            if (!self || !containerRT || !strips || !_trackerRoot)
                yield break;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRT);
            Canvas.ForceUpdateCanvases();

            _trackerRoot.SetAsLastSibling();

            var cg = _trackerRoot.GetComponent<CanvasGroup>() ?? _trackerRoot.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            // ===== WIDTH from StripContainer =====
            if (!TryGetBottomEdgeInContainerLocal(containerRT, strips, out var stripsBL, out var stripsBR))
            {
                _log.LogWarning("CookBook: Could not compute StripContainer world corners.");
                yield break;
            }

            float xCenter = (stripsBL.x + stripsBR.x) * 0.5f;
            float stripContainerWidth = stripsBR.x - stripsBL.x;

            // ===== VERTICAL position from bottom of LAST strip =====
            var lastStrip = strips
                .GetComponentsInChildren<RectTransform>(true)
                .Where(rt => rt && rt.gameObject.activeInHierarchy && rt.name.Contains("ScoreboardStrip"))
                .LastOrDefault();

            if (!lastStrip)
            {
                _log.LogWarning("CookBook: Last strip not found; cannot place panel vertically.");
                yield break;
            }

            if (!TryGetBottomEdgeInContainerLocal(containerRT, lastStrip, out var lastBL, out var lastBR))
            {
                _log.LogWarning("CookBook: Could not compute last strip world corners.");
                yield break;
            }

            float yTop = lastBL.y - GapPx;

            _trackerRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _trackerRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _trackerRoot.pivot = new Vector2(0.5f, 1f);

            _trackerRoot.sizeDelta = new Vector2(stripContainerWidth, PanelHeightPx);
            _trackerRoot.anchoredPosition = new Vector2(xCenter, yTop);
        }

        /// <summary>
        /// Computes bottom-left and bottom-right points of a RectTransform in Container-local coordinates,
        /// using world corners (stable even when rect/anchoredPosition are layout-driven).
        /// </summary>
        private static bool TryGetBottomEdgeInContainerLocal(RectTransform containerRT, RectTransform targetRT, out Vector2 bl, out Vector2 br)
        {
            bl = default;
            br = default;

            if (!containerRT || !targetRT) return false;

            var wc = new Vector3[4];
            targetRT.GetWorldCorners(wc);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    containerRT,
                    RectTransformUtility.WorldToScreenPoint(null, wc[0]),
                    null,
                    out bl))
                return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    containerRT,
                    RectTransformUtility.WorldToScreenPoint(null, wc[3]),
                    null,
                    out br))
                return false;

            return true;
        }

        private static void InitializeCookBookUI(RectTransform container)
        {
            GameObject rootGO = new GameObject("CookBook_FloatingUI", typeof(RectTransform), typeof(LayoutElement));
            _trackerRoot = rootGO.GetComponent<RectTransform>();
            _trackerRoot.SetParent(container, false);

            var le = rootGO.GetComponent<LayoutElement>();
            le.ignoreLayout = true;

            _trackerRoot.sizeDelta = new Vector2(0f, PanelHeightPx);

            var img = rootGO.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.6f);

            DebugLog.Trace(_log, "CookBook UI Initialized as a non-invasive sibling.");
        }

        private static void CleanupDiagnostics(RectTransform root)
        {
            var existing = root.GetComponentsInChildren<RectTransform>(true)
                .Where(rt => rt && (rt.name.StartsWith("DB_") || rt.name.Contains("BorderGroup")));
            foreach (var d in existing)
                Object.Destroy(d.gameObject);
        }

        private static void RecursiveDump(Transform current, StringBuilder sb, int depth)
        {
            if (current == null) return;

            string indent = new string(' ', depth * 4);
            sb.Append($"{indent}[{current.name}]");

            if (current is RectTransform rt)
                sb.Append($" | Size: {rt.rect.size} | Delta: {rt.sizeDelta}");

            var components = current.GetComponents<Component>()
                .Where(c => c != null && (c is LayoutGroup || c is ContentSizeFitter || c is LayoutElement))
                .Select(c => c.GetType().Name);

            if (components.Any())
                sb.Append($" | Layout: {string.Join(", ", components)}");

            sb.AppendLine();

            foreach (Transform child in current)
            {
                if (child.name.StartsWith("DB_")) continue;
                RecursiveDump(child, sb, depth + 1);
            }
        }
    }
}
