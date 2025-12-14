using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static class CraftUI
    {
        private static ManualLogSource _log;
        private static IReadOnlyList<CraftableEntry> _lastCraftables;
        private static readonly List<RecipeRowUI> _recipeRowUIs = new();
        private static bool _skeletonBuilt = false;
        private static CraftingController _currentController;

        private static GameObject _cookbookRoot;
        private static RectTransform _recipeListContent;
        private static TMP_InputField _searchInputField;

        private static GameObject _recipeRowTemplate;
        private static GameObject _pathRowTemplate;
        private static GameObject _ingredientSlotTemplate;
        private static GameObject _ResultSlotTemplate;

        private static RecipeRowRuntime _openRow;
        private static CraftUIRunner _runner;
        private static Coroutine _activeBuildRoutine;
        private static Coroutine _activeDropdownRoutine;
        private static RecipeDropdownRuntime _sharedDropdown;

        // ---------------- Layout constants (normalized) ----------------
        private static float _panelWidth;
        private static float _panelHeight;

        // CookBookPanel
        internal const float CookBookPanelPaddingTopNorm = 0.0159744409f;
        internal const float CookBookPanelPaddingBottomNorm = 0.0159744409f;
        internal const float CookBookPanelPaddingLeftNorm = 0f;
        internal const float CookBookPanelPaddingRightNorm = 0f;
        internal const float CookBookPanelElementSpacingNorm = 0.0159744409f; // vertical gap between SearchBar and RecipeList

        // SearchBar container
        internal const float SearchBarHeightNorm = 0.0798722045f;
        internal const float SearchBarBottomBorderThicknessNorm = 0.0001f;

        // RecipeList internal layout
        internal const float RecipeListVerticalPaddingNorm = 0f;
        internal const float RecipeListLeftPaddingNorm = 0.0181818182f;
        internal const float RecipeListRightPaddingNorm = 0.0181818182f;
        internal const float RecipeListElementSpacingNorm = 0f;
        internal const float RecipeListScrollbarWidthNorm = 0f;

        // RecipeRow/RowTop
        internal const float RowTopHeightNorm = 0.111821086f;
        internal const float RowTopTopPaddingNorm = 0.00798722045f;
        internal const float RowTopBottomPaddingNorm = 0.00798722045f;
        internal const float RowTopElementSpacingNorm = 0.0181818182f;
        internal const float MetaDataColumnWidthNorm = 0.254545455f;
        internal const float MetaDataElementSpacingNorm = 0.0159744409f;
        internal const float DropDownArrowSizeNorm = 0.0511182109f;
        internal const float textSizeNorm = 0.0383386581f;

        // ----- PathsContainer sizing -----
        private const float PathsContainerVerticalPaddingNorm = 0.0f;
        private const float PathsContainerLeftPaddingNorm = 0.0181818182f;
        private const float PathsContainerRightPaddingNorm = 0.0181818182f;
        private const int PathsContainerMaxVisibleRows = 4;
        private const float PathsContainerSpacingNorm = 0.00798722045f;

        // ----- PathRow sizing -----
        private const float PathRowHeightNorm = 0.0798722045f;
        private const float PathRowLeftPaddingNorm = 0.00909090909f;
        private const float PathRowRightPaddingNorm = 0.00909090909f;
        private const float PathRowIngredientSpacingNorm = 0.00909090909f;

        //----- Ingredients -------
        private const float IngredientHeightNorm = 0.0670926518f;
        private const float _IngredientStackSizeTextHeightPx = 10f;
        private const float _StackMargin = 2f;

        //==================== LifeCycle ====================
        internal static void Init(ManualLogSource log)
        {
            _log = log;
            StateController.OnCraftablesForUIChanged += CraftablesForUIChanged;
        }

        internal static void Attach(CraftingController controller)
        {

            _currentController = controller;
            if (_cookbookRoot != null)
            {
                _log.LogDebug("CraftUI.Attach: already attached, skipping.");
                return;
            }
            var craftingPanel = UnityEngine.Object.FindObjectOfType<CraftingPanel>();
            if (!craftingPanel)
            {
                _log.LogWarning("CraftUI.Attach: CraftingPanel not found; cannot attach UI.");
                return;
            }

            // hierarchy pieces
            Transform bgContainerTr = craftingPanel.transform.Find("MainPanel/Juice/BGContainer");
            RectTransform bgRect = bgContainerTr.GetComponent<RectTransform>(); // contains bgmain
            RectTransform bgMainRect = bgContainerTr ? bgContainerTr.Find("BGMain")?.GetComponent<RectTransform>() : null;
            RectTransform labelRect = craftingPanel.transform.Find("MainPanel/Juice/LabelContainer")?.GetComponent<RectTransform>();
            RectTransform craftBgRect = bgContainerTr.Find("CraftingContainer/Background")?.GetComponent<RectTransform>();
            RectTransform craftRect = bgContainerTr.Find("CraftingContainer")?.GetComponent<RectTransform>();
            RectTransform submenuRect = bgContainerTr.Find("SubmenuContainer")?.GetComponent<RectTransform>();
            RectTransform invRect = bgContainerTr.Find("InventoryContainer")?.GetComponent<RectTransform>();

            if (!labelRect)
            {
                _log.LogError("CraftUI.Attach: LabelContainer RectTransform not found; aborting border setup.");
                return; // or just skip the border part
            }

            // Preserve current on-screen position
            labelRect.SetParent(bgMainRect, worldPositionStays: true);

            // base width references
            float invBaseWidth = invRect ? invRect.rect.width : 0f;
            float invBaseHeight = invRect ? invRect.rect.height : 0f;
            float craftBaseWidth = craftBgRect ? craftBgRect.rect.width : 0;
            float baseWidth = bgMainRect.rect.width;
            float baseHeight = bgMainRect.rect.height;
            float baseLabelWidth = labelRect.rect.width;

            //==================== base UI scaling ====================
            var img = bgMainRect.GetComponent<Image>();
            var sprite = img ? img.sprite : null;
            float ppu = sprite ? sprite.pixelsPerUnit : 1f;
            float padLeft = sprite ? sprite.border.x / ppu : 0f;
            float padRight = sprite ? sprite.border.z / ppu : 0f;
            float padTop = sprite ? sprite.border.w / ppu : 0f;
            float padBottom = sprite ? sprite.border.y / ppu : 0f;
            float padHorizontal = padLeft + padRight;

            // Widen BG
            float widthscale = 1.8f;
            float newBgWidth = baseWidth * widthscale;

            // ensure even label margins
            float innerWidth = baseWidth - padHorizontal;
            float labelGap = (innerWidth - baseLabelWidth) * 0.5f;
            float newInnerWidth = newBgWidth - padHorizontal;
            float newLabelWidth = newInnerWidth - 2f * labelGap;

            // shrink vanilla ui margins
            float invWidthNew = invBaseWidth * 0.88f;
            float invHeightNew = invBaseHeight * 0.9f;
            float craftWidthNew = invWidthNew;

            // clamp cookbook width to 30% of total chef UI
            float cookbookWidth = Mathf.Clamp(newBgWidth * 0.3f, 260f, newInnerWidth - invBaseWidth);

            // clamp gap between cookbook and crafting panel to 5% of the total width
            float gap = Mathf.Clamp(newBgWidth * 0.05f, 20f, labelGap);

            // extend background and label
            bgRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newBgWidth);
            labelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newLabelWidth);

            // shrink vanilla ui margins
            invRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, invWidthNew);
            invRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, invHeightNew);
            craftBgRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, craftWidthNew);

            // recenter labelcontainer
            AlignLabelVerticallyBetween(labelRect, bgMainRect, craftBgRect);

            // position craft+inventory block using left sideMargin
            float craftWidth = craftBgRect.rect.width;
            float sideMargin = (newInnerWidth - (craftWidth + gap + cookbookWidth)) * 0.5f;
            float centerCraftPanel = -newInnerWidth * 0.5f + sideMargin + craftWidth * 0.5f;

            // shift vanilla UI left
            var pos = craftBgRect.anchoredPosition;
            pos.x = centerCraftPanel;
            craftBgRect.anchoredPosition = pos;

            pos = invRect.anchoredPosition;
            pos.x = centerCraftPanel;
            invRect.anchoredPosition = pos;

            pos = submenuRect.anchoredPosition;
            pos.x = centerCraftPanel;
            submenuRect.anchoredPosition = pos;

            // --- compute combined vertical bounds of craft + inventory in BGContainer space ---
            Bounds contentBounds = default;
            bool hasBounds = false;

            if (craftBgRect)
            {
                var b = RectTransformUtility.CalculateRelativeRectTransformBounds(bgContainerTr, craftBgRect);
                contentBounds = b;
                hasBounds = true;
            }

            if (invRect)
            {
                var b = RectTransformUtility.CalculateRelativeRectTransformBounds(bgContainerTr, invRect);
                if (hasBounds)
                {
                    contentBounds.Encapsulate(b);
                }
                else
                {
                    contentBounds = b;
                    hasBounds = true;
                }
            }

            // create CookBook panel in the new right-hand strip
            _cookbookRoot = CreateUIObject("CookBookPanel", typeof(RectTransform), typeof(CraftUIRunner), typeof(Canvas), typeof(GraphicRaycaster));

            _runner = _cookbookRoot.GetComponent<CraftUIRunner>();
            var canvas = _cookbookRoot.GetComponent<Canvas>();
            _cookbookRoot.GetComponent<GraphicRaycaster>();
            RectTransform cbRT = _cookbookRoot.GetComponent<RectTransform>();

            _cookbookRoot.transform.SetParent(bgContainerTr, false);
            cbRT.anchorMin = new Vector2(1f, 0.5f);
            cbRT.anchorMax = new Vector2(1f, 0.5f);
            cbRT.pivot = new Vector2(1f, 0.5f);

            // match vertical span of vanilla content
            cbRT.sizeDelta = new Vector2(cookbookWidth, contentBounds.size.y);

            /// ensure equal margins, same y position
            cbRT.anchoredPosition = new Vector2(-sideMargin, contentBounds.center.y);

            labelRect.GetComponent<Image>().enabled = false;
            AddBorder(labelRect, new Color32(209, 209, 210, 255), 2f, 2f, 4f, 4f);

            _log.LogInfo(
                $"CraftUI.Attach: CookBook panel attached. baseWidth={baseWidth:F1}, newWidth={newBgWidth:F1}, cookbookWidth={cookbookWidth:F1}, invBaseWidth={invBaseWidth:F1}"
            );
            _panelWidth = cbRT.rect.width;
            _panelHeight = cbRT.rect.height;

            // TODO: set up timer for perf analysis here
            CookBookSkeleton(cbRT);
            EnsureResultSlotArtTemplates(craftingPanel);
            EnsureIngredientSlotTemplate();
            BuildRecipeRowTemplate();
            BuildPathRowTemplate();
            BuildSharedDropdown();
            //ending here

            if (_lastCraftables != null && _lastCraftables.Count > 0)
            {
                PopulateRecipeList(_lastCraftables);
            }
        }

        internal static void Detach()
        {
            if (_cookbookRoot != null)
            {
                UnityEngine.Object.Destroy(_cookbookRoot);
                _cookbookRoot = null;
                _log.LogInfo("CraftUI.Detach: CookBook panel destroyed.");
            }

            _currentController = null;
            _skeletonBuilt = false;
            _recipeListContent = null;
        }

        internal static void Shutdown()
        {
            StateController.OnCraftablesForUIChanged -= CraftablesForUIChanged;
            _searchInputField.onValueChanged.RemoveAllListeners();
        }

        //==================== Runtimes ====================
        private class NestedScrollRect : ScrollRect
        {
            public ScrollRect ParentScroll;

            public override void OnScroll(UnityEngine.EventSystems.PointerEventData data)
            {
                if (!this.IsActive()) return;

                bool canScroll = content.rect.height > viewport.rect.height;

                if (!canScroll)
                {
                    if (ParentScroll) ParentScroll.OnScroll(data);
                }
                else if (data.scrollDelta.y > 0 && verticalNormalizedPosition >= 0.99f)
                {
                    if (ParentScroll) ParentScroll.OnScroll(data);
                }
                else if (data.scrollDelta.y < 0 && verticalNormalizedPosition <= 0.01f)
                {
                    if (ParentScroll) ParentScroll.OnScroll(data);
                }
                else
                {
                    // Otherwise, scroll normally
                    base.OnScroll(data);
                }
            }
        }

        private sealed class RecipeRowRuntime : MonoBehaviour
        {
            public CraftPlanner.CraftableEntry Entry;

            public RectTransform RowTransform;
            public LayoutElement RowLayoutElement;
            public RectTransform RowTop;
            public Button RowTopButton;
            public TextMeshProUGUI ArrowText;

            public RectTransform DropdownMountPoint;
            public LayoutElement DropdownLayoutElement;

            public Image ResultIcon;
            public TextMeshProUGUI ResultStackText;
            public TextMeshProUGUI ItemLabel;
            public TextMeshProUGUI DepthText;
            public TextMeshProUGUI PathsText;

            public bool IsExpanded;
            public float CollapsedHeight;
        }

        private sealed class CraftUIRunner : MonoBehaviour { }

        private class RecipeDropdownRuntime : MonoBehaviour
        {
            public ScrollRect ScrollRect;
            public RectTransform Content;
            public RectTransform Background;
            public RecipeRowRuntime CurrentOwner;

            public int SelectedPathIndex = -1;

            public void OpenFor(RecipeRowRuntime owner)
            {
                if (CurrentOwner != owner)
                {
                    Close();
                }

                CurrentOwner = owner;
                gameObject.SetActive(true);

                transform.SetParent(owner.DropdownMountPoint, false);

                var rt = (RectTransform)transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                if (ScrollRect != null)
                {
                    ScrollRect.enabled = owner.Entry.Chains.Count > PathsContainerMaxVisibleRows;
                    ScrollRect.verticalNormalizedPosition = 1f;
                }

                PopulateDropdown(Content, owner.Entry);
            }
            public void Close()
            {
                if (_activeDropdownRoutine != null && _runner != null)
                {
                    _runner.StopCoroutine(_activeDropdownRoutine);
                    _activeDropdownRoutine = null;
                }

                if (CurrentOwner == null)
                {
                    return;
                }

                SelectedPathIndex = -1;
                transform.SetParent(_cookbookRoot.transform, false);
                gameObject.SetActive(false);
                CurrentOwner = null;
            }
        }

        //==================== Cookbook Builders ====================
        internal static void CookBookSkeleton(RectTransform cookbookRoot)
        {
            if (!cookbookRoot) return;

            // Clear any leftovers if you re-enter the UI
            for (int i = cookbookRoot.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(cookbookRoot.GetChild(i).gameObject);
            }

            //------------------------ Border ------------------------
            GameObject frameClone = UnityEngine.Object.Instantiate(
                    UnityEngine.Object.FindObjectOfType<CraftingPanel>().transform.Find("MainPanel/Juice/BGContainer/CraftingContainer/Background").gameObject,
                    _cookbookRoot.transform
                );
            frameClone.name = "CookBookBorder";
            var borderRect = frameClone.GetComponent<RectTransform>();
            borderRect.anchorMin = new Vector2(0f, 0f);
            borderRect.anchorMax = new Vector2(1f, 1f);
            borderRect.pivot = new Vector2(0.5f, 0.5f);
            borderRect.anchoredPosition = Vector2.zero;
            borderRect.sizeDelta = Vector2.zero;

            // strip out the crafting contents
            foreach (Transform child in frameClone.transform)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
            // ensure stays top level
            borderRect.SetAsLastSibling();

            // ----------------------------- Dimensions ------------------------------
            float padTopPx = CookBookPanelPaddingTopNorm * _panelHeight;
            float padBottomPx = CookBookPanelPaddingBottomNorm * _panelHeight;
            float padLeftPx = CookBookPanelPaddingLeftNorm * _panelWidth;
            float padRightPx = CookBookPanelPaddingRightNorm * _panelWidth;
            float spacingPx = CookBookPanelElementSpacingNorm * _panelHeight;
            float searchBarHeightPx = SearchBarHeightNorm * _panelHeight;
            float innerHeight = _panelHeight - padTopPx - padBottomPx;
            float recipeListHeightPx = innerHeight - searchBarHeightPx - spacingPx;

            int recipeListVertPadPx = Mathf.RoundToInt(RecipeListVerticalPaddingNorm * _panelHeight);
            if (recipeListHeightPx < 0f) recipeListHeightPx = 0f;

            //------------------------ SearchBarContainer ------------------------
            GameObject searchGO = CreateUIObject("SearchBarContainer", typeof(RectTransform));

            var searchRect = searchGO.GetComponent<RectTransform>();
            searchRect.SetParent(cookbookRoot, false);

            searchRect.anchorMin = new Vector2(0f, 1f);
            searchRect.anchorMax = new Vector2(1f, 1f);
            searchRect.pivot = new Vector2(0.5f, 1f);

            searchRect.sizeDelta = new Vector2(0f, searchBarHeightPx);
            searchRect.anchoredPosition = new Vector2(0f, -padTopPx);

            var currentMin = searchRect.offsetMin;
            var currentMax = searchRect.offsetMax;
            currentMin.x = padLeftPx;
            currentMax.x = -padRightPx;
            searchRect.offsetMin = currentMin;
            searchRect.offsetMax = currentMax;

            //---------------------- SearchBar Internals ----------------------
            GameObject inputGO = CreateUIObject("SearchInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));

            var inputRect = inputGO.GetComponent<RectTransform>();
            var bgImage = inputGO.GetComponent<Image>();
            _searchInputField = inputGO.GetComponent<TMP_InputField>();

            inputRect.SetParent(searchRect, false);
            inputRect.anchorMin = Vector2.zero;
            inputRect.anchorMax = Vector2.one;
            inputRect.sizeDelta = Vector2.zero;
            inputRect.anchoredPosition = Vector2.zero;

            bgImage.color = new Color(0f, 0f, 0f, 0.4f);

            //---------------- Setup Text Fields ----------------
            GameObject textAreaGO = CreateUIObject("Text Area", typeof(RectTransform), typeof(RectMask2D));

            var textAreaRT = textAreaGO.GetComponent<RectTransform>();

            textAreaRT.SetParent(inputRect, false);
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = Vector2.zero;
            textAreaRT.offsetMax = Vector2.zero;

            GameObject textGO = CreateUIObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));

            var textTMP = textGO.GetComponent<TextMeshProUGUI>();
            var textRT = textGO.GetComponent<RectTransform>();

            textRT.SetParent(textAreaRT, false);
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;

            textTMP.fontSize = 20f;
            textTMP.alignment = TextAlignmentOptions.Center;
            textTMP.color = Color.white;

            GameObject phGO = CreateUIObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));

            var phRT = phGO.GetComponent<RectTransform>();
            var placeholderTMP = phGO.GetComponent<TextMeshProUGUI>();

            phRT.SetParent(textAreaRT, false);
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.sizeDelta = Vector2.zero;

            placeholderTMP.text = "search";
            placeholderTMP.fontSize = 20f;
            placeholderTMP.alignment = TextAlignmentOptions.Center;
            placeholderTMP.color = new Color(1f, 1f, 1f, 0.5f);
            placeholderTMP.raycastTarget = false;

            //---------------- Wire TMP_InputField ----------------
            _searchInputField.textViewport = textAreaRT;
            _searchInputField.textComponent = textTMP;
            _searchInputField.placeholder = placeholderTMP;
            _searchInputField.onValueChanged.AddListener(OnSearchTextChanged);

            AddBorder(inputRect, new Color32(209, 209, 210, 200), bottom: Mathf.Max(1f, SearchBarBottomBorderThicknessNorm * _panelHeight));
            //------------------------ RecipeListContainer ------------------------
            GameObject listGO = CreateUIObject("RecipeListContainer", typeof(RectTransform), typeof(ScrollRect));

            var listRect = listGO.GetComponent<RectTransform>();
            var scroll = listGO.GetComponent<ScrollRect>();

            listRect.SetParent(cookbookRoot, false);
            listRect.anchorMin = new Vector2(0f, 1f);
            listRect.anchorMax = Vector2.one;
            listRect.pivot = new Vector2(0.5f, 1f);
            listRect.sizeDelta = new Vector2(0f, recipeListHeightPx);

            float listTop = padTopPx + searchBarHeightPx + spacingPx;
            listRect.anchoredPosition = new Vector2(0f, -listTop);

            listRect.offsetMin = new Vector2(padLeftPx, listRect.offsetMin.y);
            listRect.offsetMax = new Vector2(-padRightPx, listRect.offsetMax.y);

            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = RowTopHeightNorm * _panelHeight * 0.5f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.16f;
            scroll.elasticity = 0.1f;

            GameObject viewportGO = CreateUIObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var viewportRT = viewportGO.GetComponent<RectTransform>();

            viewportRT.SetParent(listRect, false);
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.sizeDelta = Vector2.zero;
            scroll.viewport = viewportRT;

            viewportGO.GetComponent<Image>().color = Color.clear;

            GameObject contentGO = CreateUIObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));

            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.SetParent(viewportRT, false);
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = Vector2.one;
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = Vector2.zero;

            scroll.content = contentRT;
            _recipeListContent = contentRT;

            // rows stacked from top
            var vLayout = contentGO.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(
                Mathf.RoundToInt(RecipeListLeftPaddingNorm * _panelWidth),
                Mathf.RoundToInt(RecipeListRightPaddingNorm * _panelWidth),
                recipeListVertPadPx, recipeListVertPadPx
            );
            vLayout.spacing = RecipeListElementSpacingNorm * _panelHeight;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlHeight = true;
            vLayout.childControlWidth = true;
            vLayout.childForceExpandHeight = false;
            vLayout.childForceExpandWidth = true;

            var fitter = contentGO.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _skeletonBuilt = true;
        }

        //==================== Instantiation ====================
        internal static GameObject CreateRecipeRow(RectTransform parent, CraftableEntry entry)
        {
            if (_recipeRowTemplate == null)
            {
                _log?.LogWarning("RecipeRowTemplate missing.");
                return null;
            }

            // ---------------- Initialize root ----------------
            GameObject rowGO = UnityEngine.Object.Instantiate(_recipeRowTemplate, parent);
            rowGO.name = "RecipeRow";
            rowGO.SetActive(true);

            var runtime = rowGO.GetComponent<RecipeRowRuntime>();
            if (runtime == null)
            {
                _log.LogError("CreateRecipeRow: RecipeRowRuntime component missing on template.");
                return rowGO;
            }

            runtime.Entry = entry;
            runtime.IsExpanded = false;

            // ---------------- Label ----------------
            if (runtime.ItemLabel != null)
            {
                runtime.ItemLabel.text = GetEntryDisplayName(entry);
                runtime.ItemLabel.color = GetEntryColor(entry);
            }

            // ---------------- MetaData: Depth ----------------
            if (runtime.DepthText != null)
            {
                runtime.DepthText.text = $"Depth: {entry.MinDepth}";
            }

            // ---------------- MetaData: Paths ----------------
            if (runtime.PathsText != null)
            {
                int pathCount = entry.Chains?.Count ?? 0;
                runtime.PathsText.text = $"Paths: {pathCount}";
            }

            // ---------------- ItemIcon ----------------
            if (runtime.ResultIcon != null)
            {
                Sprite iconSprite = null;
                switch (entry.ResultKind)
                {
                    case RecipeResultKind.Item:
                        iconSprite = GetItemIcon(entry.ResultItem);
                        break;
                    case RecipeResultKind.Equipment:
                        iconSprite = GetEquipmentIcon(entry.ResultEquipment);
                        break;
                }

                if (iconSprite != null)
                {
                    runtime.ResultIcon.sprite = iconSprite;
                    runtime.ResultIcon.color = Color.white;
                }
                else
                {
                    runtime.ResultIcon.sprite = null;
                    runtime.ResultIcon.color = new Color(1f, 1f, 1f, 0.1f);
                }
            }

            // ---------------- Stack Text ----------------
            if (runtime.ResultStackText != null)
            {
                if (entry.ResultCount > 1)
                {
                    runtime.ResultStackText.text = entry.ResultCount.ToString();
                    runtime.ResultStackText.gameObject.SetActive(true);
                }
                else
                {
                    runtime.ResultStackText.gameObject.SetActive(false);
                }
            }

            if (runtime.RowTopButton != null)
            {
                runtime.RowTopButton.onClick.RemoveAllListeners();
                runtime.RowTopButton.onClick.AddListener(() => ToggleRecipeRow(runtime));
            }

            if (runtime.DropdownLayoutElement != null)
            {
                runtime.DropdownLayoutElement.preferredHeight = 0f;
            }

            _log.LogDebug("CreateRecipeRow: rendered row for " + GetEntryDisplayName(entry));
            return rowGO;
        }

        private static GameObject CreatePathRow(RectTransform parent, RecipeChain chain)
        {
            if (_pathRowTemplate == null) return null;

            var debugContents = new List<string>();

            GameObject pathRowGO = UnityEngine.Object.Instantiate(_pathRowTemplate, parent);
            pathRowGO.name = "PathRow";
            pathRowGO.layer = LayerMask.NameToLayer("UI");
            pathRowGO.SetActive(true);

            if (chain.TotalItemCost != null)
            {
                for (int i = 0; i < chain.TotalItemCost.Length; i++)
                {
                    int count = chain.TotalItemCost[i];
                    if (count <= 0) continue;

                    // TODO: remove debug
                    var def = ItemCatalog.GetItemDef((ItemIndex)i);
                    string name = def != null ? def.name : $"ItemIdx_{i}";
                    debugContents.Add($"{name} (x{count})");

                    Sprite icon = GetItemIcon((ItemIndex)i);

                    if (icon != null)
                    {
                        CreateIngredientSlot(pathRowGO.transform, icon, count);
                    }
                    else
                    {
                        _log.LogDebug($"Icon was null for {name}");
                    }
                }
            }

            if (chain.TotalEquipmentCost != null)
            {
                for (int i = 0; i < chain.TotalEquipmentCost.Length; i++)
                {
                    int count = chain.TotalEquipmentCost[i];
                    if (count <= 0) continue;

                    // TODO: remove debug
                    var def = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)i);
                    string name = def != null ? def.name : $"EquipIdx_{i}";
                    debugContents.Add($"{name} (x{count})");

                    Sprite icon = GetEquipmentIcon((EquipmentIndex)i);

                    if (icon != null)
                    {
                        CreateIngredientSlot(pathRowGO.transform, icon, count);
                    }
                    else
                    {
                        _log.LogDebug($"Icon was null for {name}");
                    }
                }
            }
            _log.LogInfo($"[UI DEBUG] Rendering PathRow: {string.Join(", ", debugContents)}");
            return pathRowGO;
        }

        private static void CreateIngredientSlot(Transform parentRow, Sprite icon, int count)
        {
            if (_ingredientSlotTemplate == null)
            {
                _log.LogDebug("IngredientSlotTemplate was null when attempting to create an ingredientslot.");
                return;
            }

            GameObject slotGO = UnityEngine.Object.Instantiate(_ingredientSlotTemplate, parentRow);
            slotGO.name = "IngredientSlot";
            slotGO.SetActive(true);

            var iconImg = slotGO.transform.Find("Background/Icon")?.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.sprite = icon;
                iconImg.color = Color.white;
            }

            var stackTmp = slotGO.transform.Find("Background/StackText")?.GetComponent<TextMeshProUGUI>();
            if (stackTmp != null)
            {
                if (count > 1)
                {
                    stackTmp.gameObject.SetActive(true);
                    stackTmp.text = count.ToString();
                }
                else
                {
                    stackTmp.gameObject.SetActive(false);
                }
            }
        }

        //==================== Prefabs ====================
        private static void BuildRecipeRowTemplate()
        {
            if (_recipeRowTemplate != null)
            {
                _log.LogDebug("BuildRecipeRowTemplate() Already built.");
                return;
            }

            float topPadPx = RowTopTopPaddingNorm * _panelHeight;
            float bottomPadPx = RowTopBottomPaddingNorm * _panelHeight;
            float elementSpacingPx = RowTopElementSpacingNorm * _panelWidth;
            float rowTopHeightPx = RowTopHeightNorm * _panelHeight;
            float innerHeight = rowTopHeightPx - (topPadPx + bottomPadPx);
            float metaWidthPx = MetaDataColumnWidthNorm * _panelWidth;
            float metaSpacingPx = MetaDataElementSpacingNorm * _panelHeight;
            float dropDownArrowSize = DropDownArrowSizeNorm * _panelHeight;
            float textSize = textSizeNorm * _panelHeight;

            // ---------------- RecipeRow root ----------------
            GameObject rowGO = CreateUIObject("RecipeRowTemplate", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(RecipeRowRuntime));

            var rowRT = rowGO.GetComponent<RectTransform>();
            var rowVLG = rowGO.GetComponent<VerticalLayoutGroup>();
            var rowLE = rowGO.GetComponent<LayoutElement>();

            rowRT.SetParent(_cookbookRoot.transform, false);
            rowGO.SetActive(false);

            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            rowRT.pivot = new Vector2(0.5f, 1f);
            rowRT.anchoredPosition = Vector2.zero;
            rowRT.sizeDelta = new Vector2(0f, rowTopHeightPx);

            rowVLG.spacing = 0f;
            rowVLG.childAlignment = TextAnchor.UpperCenter;
            rowVLG.childControlWidth = true;
            rowVLG.childControlHeight = true;
            rowVLG.childForceExpandWidth = true;
            rowVLG.childForceExpandHeight = false;

            rowLE.minHeight = rowTopHeightPx;
            rowLE.preferredHeight = rowTopHeightPx;
            rowLE.flexibleHeight = 0f;
            rowLE.flexibleWidth = 1f;

            // ---------------- RowTop ----------------
            GameObject rowTopGO = CreateUIObject("RowTop", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup), typeof(LayoutElement));

            var rowTopRT = rowTopGO.GetComponent<RectTransform>();
            var rowTopImg = rowTopGO.GetComponent<Image>();
            var rowTopH = rowTopGO.GetComponent<HorizontalLayoutGroup>();
            var rowTopLE = rowTopGO.GetComponent<LayoutElement>();

            rowTopRT.SetParent(rowRT, false);

            rowTopLE.minHeight = rowTopHeightPx;
            rowTopLE.preferredHeight = rowTopHeightPx;
            rowTopLE.flexibleHeight = 0f;
            rowTopLE.flexibleWidth = 1f;

            rowTopImg.color = new Color(0f, 0f, 0f, 0f);
            rowTopImg.raycastTarget = true;

            rowTopH.spacing = elementSpacingPx;
            rowTopH.childAlignment = TextAnchor.MiddleLeft;
            rowTopH.childControlWidth = true;
            rowTopH.childControlHeight = true;
            rowTopH.childForceExpandWidth = false;
            rowTopH.childForceExpandHeight = true;
            rowTopH.padding = new RectOffset(
                0,
                0,
                Mathf.RoundToInt(topPadPx),
                Mathf.RoundToInt(bottomPadPx)
            );

            // ---------------- DropDown ----------------
            GameObject dropGO = CreateUIObject("DropDown", typeof(RectTransform), typeof(LayoutElement));

            var dropRT = dropGO.GetComponent<RectTransform>();
            var dropLE = dropGO.GetComponent<LayoutElement>();

            dropRT.SetParent(rowTopRT, false);

            dropLE.minWidth = innerHeight;
            dropLE.preferredWidth = innerHeight;
            dropLE.flexibleWidth = 0f;

            GameObject arrowGO = CreateUIObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));

            var arrowRT = arrowGO.GetComponent<RectTransform>();
            var arrowTMP = arrowGO.GetComponent<TextMeshProUGUI>();

            arrowRT.SetParent(dropRT, false);
            arrowRT.anchorMin = Vector2.zero;
            arrowRT.anchorMax = Vector2.one;
            arrowRT.offsetMin = Vector2.zero;
            arrowRT.offsetMax = Vector2.zero;

            arrowTMP.text = ">";
            arrowTMP.alignment = TextAlignmentOptions.Center;
            arrowTMP.fontSize = dropDownArrowSize;
            arrowTMP.color = Color.white;
            arrowTMP.raycastTarget = false;
            arrowTMP.enableWordWrapping = false;
            arrowTMP.overflowMode = TextOverflowModes.Overflow;

            // ---------------- Item Slot ----------------
            GameObject slotGO = UnityEngine.Object.Instantiate(_ResultSlotTemplate, rowTopRT, false);
            slotGO.name = "ItemSlot";
            slotGO.SetActive(true);

            var slotRT = slotGO.GetComponent<RectTransform>();
            slotRT.SetParent(rowTopRT, false);

            // ---------------- Item Label ----------------
            GameObject labelGO = CreateUIObject("ItemLabel", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));

            var labelRT = labelGO.GetComponent<RectTransform>();
            var labelTMP = labelGO.GetComponent<TextMeshProUGUI>();
            var labelLE = labelGO.GetComponent<LayoutElement>();

            labelRT.SetParent(rowTopRT, false);

            labelLE.minWidth = 0f;
            labelLE.preferredWidth = 0f;
            labelLE.flexibleWidth = 1f;

            labelTMP.text = "NAME";
            labelTMP.fontSize = textSize;
            labelTMP.enableWordWrapping = false;
            labelTMP.overflowMode = TextOverflowModes.Ellipsis;
            labelTMP.alignment = TextAlignmentOptions.Center;
            labelTMP.color = Color.white;
            labelTMP.raycastTarget = false;

            // ---------------- MetaData ----------------
            GameObject metaGO = CreateUIObject("MetaData", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));

            var metaRT = metaGO.GetComponent<RectTransform>();
            var metaV = metaGO.GetComponent<VerticalLayoutGroup>();
            var metaLE = metaGO.GetComponent<LayoutElement>();

            metaRT.SetParent(rowTopRT, false);

            metaLE.preferredWidth = metaWidthPx;
            metaLE.minWidth = metaWidthPx;
            metaLE.flexibleWidth = 0f;

            metaV.spacing = metaSpacingPx;
            metaV.childAlignment = TextAnchor.MiddleRight;
            metaV.childForceExpandHeight = false;
            metaV.childControlWidth = true;
            metaV.childControlHeight = true;

            var depthGO = CreateUIObject("MinimumDepth", typeof(RectTransform), typeof(TextMeshProUGUI));
            SetupMetaDataText(depthGO, metaRT, "Depth: 0");

            var pathsGO = CreateUIObject("AvailablePaths", typeof(RectTransform), typeof(TextMeshProUGUI));
            SetupMetaDataText(pathsGO, metaRT, "Paths: 0");

            AddBorder(rowTopRT, new Color32(209, 209, 210, 255), 1f, 1f, 0f, 0f);

            // ---------------- PathsContainer ----------------
            GameObject mountGO = CreateUIObject("DropdownMountPoint", typeof(RectTransform), typeof(LayoutElement));
            var mountRT = mountGO.GetComponent<RectTransform>();
            var mountLE = mountGO.GetComponent<LayoutElement>();

            mountRT.SetParent(rowRT, false);

            mountRT.pivot = new Vector2(0.5f, 1f);
            mountRT.anchorMin = new Vector2(0f, 1f);
            mountRT.anchorMax = new Vector2(1f, 1f);
            mountRT.sizeDelta = Vector2.zero;

            mountLE.minHeight = 0f;
            mountLE.preferredHeight = 0f;
            mountLE.flexibleHeight = 0f;
            mountLE.flexibleWidth = 1f;

            // ---------------- Runtime wiring ----------------
            var runtime = rowGO.GetComponent<RecipeRowRuntime>();
            runtime.Entry = null;
            runtime.RowTransform = rowRT;
            runtime.RowTop = rowTopRT;
            runtime.RowLayoutElement = rowLE;
            runtime.RowTopButton = rowTopGO.GetComponent<Button>();
            runtime.ArrowText = arrowTMP;
            runtime.DropdownMountPoint = mountRT;
            runtime.DropdownLayoutElement = mountLE;
            runtime.ResultIcon = slotGO.transform.Find("Icon").GetComponent<Image>();
            runtime.ResultStackText = slotGO.transform.Find("StackText").GetComponent<TextMeshProUGUI>();
            runtime.ItemLabel = labelTMP;
            runtime.DepthText = depthGO.GetComponent<TextMeshProUGUI>();
            runtime.PathsText = pathsGO.GetComponent<TextMeshProUGUI>();

            runtime.CollapsedHeight = rowRT.sizeDelta.y;
            runtime.IsExpanded = false;

            _log.LogDebug("Built Recipe Row Template.");
            _log.LogInfo($"BuildRecipeRowTemplate: panelH={_panelHeight}, " +
             $"rowTopHeightPx={rowTopHeightPx}, innerHeight={innerHeight}");
            DumpHierarchy((Transform)rowGO.transform, 0);
            _recipeRowTemplate = rowGO;
        }

        private static void BuildPathRowTemplate()
        {
            if (_pathRowTemplate != null)
            {
                return;
            }

            float rowHeightPx = PathRowHeightNorm * _panelHeight;
            float slotSpacingPx = PathRowIngredientSpacingNorm * _panelWidth;
            int leftPadPx = Mathf.RoundToInt(PathRowLeftPaddingNorm * _panelWidth);
            int rightPadPx = Mathf.RoundToInt(PathRowRightPaddingNorm * _panelWidth);

            var rowGO = CreateUIObject("PathRowTemplate", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(HorizontalLayoutGroup));

            var rowRT = (RectTransform)rowGO.transform;
            var rowLE = rowGO.GetComponent<LayoutElement>();
            var hlg = rowGO.GetComponent<HorizontalLayoutGroup>();
            var rowImg = rowGO.GetComponent<Image>();

            rowRT.SetParent(_cookbookRoot.transform, false);
            rowGO.SetActive(false);

            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            rowRT.pivot = new Vector2(0.5f, 1f);
            rowRT.anchoredPosition = Vector2.zero;
            rowRT.offsetMin = Vector2.zero;
            rowRT.offsetMax = Vector2.zero;

            rowLE.preferredHeight = rowHeightPx;
            rowLE.flexibleHeight = 0f;
            rowLE.flexibleWidth = 1f;

            hlg.spacing = slotSpacingPx;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.padding = new RectOffset(leftPadPx, rightPadPx, 0, 0);

            rowImg.color = new Color32(5, 5, 5, 50);

            AddBorder(rowRT, new Color32(209, 209, 210, 255), top: 1f, bottom: 1f);
            _pathRowTemplate = rowGO;
            _log.LogDebug("Built Path Row Template.");
        }

        private static void BuildSharedDropdown()
        {
            if (_sharedDropdown != null)
            {
                return;
            }

            GameObject drawerGO = CreateUIObject("SharedDropdown", typeof(RectTransform), typeof(Image), typeof(NestedScrollRect), typeof(RecipeDropdownRuntime));

            var drawerRT = drawerGO.GetComponent<RectTransform>();
            var img = drawerGO.GetComponent<Image>();
            _sharedDropdown = drawerGO.GetComponent<RecipeDropdownRuntime>();

            drawerRT.anchorMin = new Vector2(0f, 0f);
            drawerRT.anchorMax = new Vector2(1f, 1f);
            drawerRT.pivot = new Vector2(0.5f, 0.5f);
            drawerRT.offsetMin = Vector2.zero;
            drawerRT.offsetMax = Vector2.zero;

            img.color = Color.clear;

            var scroll = drawerGO.GetComponent<NestedScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = RowTopHeightNorm * _panelHeight * 0.5f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.16f;
            scroll.elasticity = 0.1f;

            if (_cookbookRoot)
            {
                var mainScroll = _cookbookRoot.GetComponentInChildren<ScrollRect>();
                scroll.ParentScroll = mainScroll;
            }

            var viewportGO = CreateUIObject("Viewport", typeof(RectTransform), typeof(RectMask2D));

            var viewportRT = viewportGO.GetComponent<RectTransform>();
            viewportRT.SetParent(drawerRT, false);
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            viewportRT.sizeDelta = Vector2.zero;
            scroll.viewport = viewportRT;

            var contentGO = CreateUIObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.SetParent(viewportRT, false);

            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            contentRT.anchoredPosition = Vector2.zero;
            scroll.content = contentRT;

            var vlg = contentGO.GetComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = PathsContainerSpacingNorm * _panelHeight;

            contentGO.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _sharedDropdown.ScrollRect = scroll;
            _sharedDropdown.Content = contentRT;
            _sharedDropdown.Background = drawerRT;
            drawerGO.transform.SetParent(_cookbookRoot.transform, false);
            drawerGO.SetActive(false);
        }

        private static void EnsureResultSlotArtTemplates(CraftingPanel craftingPanel)
        {
            if (_ResultSlotTemplate != null)
            {
                return;
            }

            // configure base dimensions
            float topPadPx = RowTopTopPaddingNorm * _panelHeight;
            float bottomPadPx = RowTopBottomPaddingNorm * _panelHeight;
            float rowTopHeightPx = RowTopHeightNorm * _panelHeight;
            float SlotHeightPx = rowTopHeightPx - (topPadPx + bottomPadPx);

            var slotGO = CreateUIObject("ResultSlotTemplate", typeof(RectTransform), typeof(LayoutElement));
            var slotRT = (RectTransform)slotGO.transform;
            var slotLE = slotGO.GetComponent<LayoutElement>();

            slotRT.SetParent(_cookbookRoot.transform, false);
            slotGO.SetActive(false);

            slotRT.sizeDelta = new Vector2(SlotHeightPx, SlotHeightPx);

            slotRT.anchorMin = new Vector2(0f, 0.5f);
            slotRT.anchorMax = new Vector2(0f, 0.5f);
            slotRT.pivot = new Vector2(0f, 0.5f);
            slotRT.anchoredPosition = Vector2.zero;
            slotRT.sizeDelta = Vector2.zero;

            slotLE.minWidth = SlotHeightPx;
            slotLE.preferredWidth = SlotHeightPx;
            slotLE.minHeight = SlotHeightPx;
            slotLE.preferredHeight = SlotHeightPx;
            slotLE.flexibleWidth = 0f;

            // add standard dark background
            var bgGO = CreateUIObject("IconBackground", typeof(RectTransform), typeof(Image));
            var bgRT = (RectTransform)bgGO.transform;
            var bgImg = bgGO.GetComponent<Image>();

            bgRT.SetParent(slotRT, false);
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            bgRT.pivot = new Vector2(0.5f, 0.5f);

            bgImg.color = new Color32(5, 5, 5, 255);
            bgImg.raycastTarget = false;

            // Instantiate vanilla borders for cleanliness
            var outlineInnerloc = craftingPanel.transform.Find("MainPanel/Juice/BGContainer/CraftingContainer/Background/Result/Holder/Outline (1)");
            var outlineOuterloc = craftingPanel.transform.Find("MainPanel/Juice/BGContainer/CraftingContainer/Background/Result/Holder/Outline");

            if (outlineInnerloc && outlineOuterloc)
            {
                var inner = InstantiateLayer(outlineInnerloc.gameObject, slotRT);
                inner.name = "ResultOutlineInner";
                inner.SetActive(true);

                var outer = InstantiateLayer(outlineOuterloc.gameObject, slotRT);
                outer.name = "ResultOutlineOuter";
                outer.SetActive(true);
            }

            // initialize icon container
            var iconGO = CreateUIObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRT = (RectTransform)iconGO.transform;
            var iconImg = iconGO.GetComponent<Image>();

            float insetPx = SlotHeightPx * 0.10f;

            iconRT.SetParent(slotRT, false);
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = new Vector2(insetPx, insetPx);
            iconRT.offsetMax = new Vector2(-insetPx, -insetPx);

            iconImg.sprite = null;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            // initialize stack text
            var stackGO = CreateUIObject("StackText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            var stackRT = (RectTransform)stackGO.transform;
            var stackTMP = stackGO.GetComponent<TextMeshProUGUI>();
            var stackLE = stackGO.GetComponent<LayoutElement>();

            stackRT.SetParent(slotRT, false);
            stackRT.anchorMin = new Vector2(1f, 1f);
            stackRT.anchorMax = new Vector2(1f, 1f);
            stackRT.pivot = new Vector2(1f, 1f);
            stackRT.anchoredPosition = new Vector2(-_StackMargin, -_StackMargin);
            stackRT.sizeDelta = Vector2.zero;

            stackTMP.text = string.Empty;
            stackTMP.fontSize = _IngredientStackSizeTextHeightPx;
            stackTMP.alignment = TextAlignmentOptions.TopRight;
            stackTMP.color = Color.white;
            stackTMP.raycastTarget = false;

            stackGO.transform.SetAsLastSibling();
            stackLE.ignoreLayout = true;
            stackGO.SetActive(true);

            _ResultSlotTemplate = slotGO;
        }

        private static void EnsureIngredientSlotTemplate()
        {
            if (_ingredientSlotTemplate != null)
            {
                return;
            }

            float IngredientHeightPx = IngredientHeightNorm * _panelHeight;

            var slotGO = CreateUIObject("IngredientSlotTemplate", typeof(RectTransform), typeof(LayoutElement));
            var slotRT = (RectTransform)slotGO.transform;
            var slotLE = slotGO.GetComponent<LayoutElement>();

            slotRT.SetParent(_cookbookRoot.transform, false);
            slotGO.SetActive(false);

            slotRT.anchorMin = new Vector2(0f, 0.5f);
            slotRT.anchorMax = new Vector2(0f, 0.5f);
            slotRT.pivot = new Vector2(0f, 0.5f);
            slotRT.anchoredPosition = Vector2.zero;
            slotRT.sizeDelta = Vector2.zero;

            slotLE.minWidth = IngredientHeightPx;
            slotLE.minHeight = IngredientHeightPx;
            slotLE.preferredWidth = IngredientHeightPx;
            slotLE.preferredHeight = IngredientHeightPx;
            slotLE.flexibleWidth = 0f;
            slotLE.flexibleHeight = 0f;

            var bgGO = CreateUIObject("Background", typeof(RectTransform), typeof(Image), typeof(Outline));
            var bgRT = (RectTransform)bgGO.transform;
            var bgImg = bgGO.GetComponent<Image>();

            bgRT.SetParent(slotRT, false);
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.pivot = new Vector2(0.5f, 0.5f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            bgImg.color = new Color32(10, 10, 10, 255);
            bgImg.raycastTarget = false;

            var iconGO = CreateUIObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRT = (RectTransform)iconGO.transform;
            var iconImg = iconGO.GetComponent<Image>();

            iconRT.SetParent(bgRT, false);
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.pivot = new Vector2(0.5f, 0.5f);
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;

            iconImg.sprite = null;
            iconImg.color = new Color(1f, 1f, 1f, 0.1f);
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            var stackGO = CreateUIObject("StackText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));

            var stackRT = (RectTransform)stackGO.transform;
            var stackTMP = stackGO.GetComponent<TextMeshProUGUI>();
            var stackLE = stackGO.GetComponent<LayoutElement>();

            stackRT.SetParent(bgRT, false);

            stackRT.anchorMin = new Vector2(1f, 1f);
            stackRT.anchorMax = new Vector2(1f, 1f);
            stackRT.pivot = new Vector2(1f, 1f);
            stackRT.anchoredPosition = new Vector2(-_StackMargin, -_StackMargin);
            stackRT.sizeDelta = Vector2.zero;

            stackTMP.text = string.Empty;
            stackTMP.fontSize = _IngredientStackSizeTextHeightPx;
            stackTMP.alignment = TextAlignmentOptions.TopRight;
            stackTMP.color = Color.white;
            stackTMP.raycastTarget = false;

            stackLE.ignoreLayout = true;

            stackGO.transform.SetAsLastSibling();
            stackGO.SetActive(false);

            AddBorder(bgRT, new Color32(209, 209, 210, 255), 1f, 1f, 1f, 1f);
            _ingredientSlotTemplate = slotGO;
        }

        //==================== Helpers ====================
        private static GameObject CreateUIObject(string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            go.layer = LayerMask.NameToLayer("UI");
            return go;
        }

        private static void SetupMetaDataText(GameObject obj, RectTransform parent, string text)
        {
            var rt = obj.GetComponent<RectTransform>();
            var tmp = obj.GetComponent<TextMeshProUGUI>();

            rt.SetParent(parent, false);
            // VLG controls size/pos, no need for anchors

            tmp.text = text;
            tmp.fontSize = 16f;
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }

        private static GameObject InstantiateLayer(GameObject template, Transform parent)
        {
            var layer = UnityEngine.Object.Instantiate(template, parent, false);
            layer.SetActive(true);

            var rt = layer.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var le = layer.GetComponent<LayoutElement>();
            if (le) le.ignoreLayout = true;

            SetupStaticVisuals(layer);

            return layer;
        }

        private static void SetupStaticVisuals(GameObject root)
        {
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void AddBorder(RectTransform parent, Color32 color, float top = 0f, float bottom = 0f, float left = 0f, float right = 0f)
        {
            var containerGO = CreateUIObject("BorderGroup", typeof(RectTransform), typeof(LayoutElement));
            var containerRT = containerGO.GetComponent<RectTransform>();
            var le = containerGO.GetComponent<LayoutElement>();

            le.ignoreLayout = true;

            containerRT.SetParent(parent, false);
            containerRT.anchorMin = Vector2.zero;
            containerRT.anchorMax = Vector2.one;
            containerRT.pivot = new Vector2(0.5f, 0.5f);
            containerRT.offsetMin = Vector2.zero;
            containerRT.offsetMax = Vector2.zero;

            containerGO.transform.SetAsLastSibling();

            GameObject Make(string name)
            {
                var go = CreateUIObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(containerRT, false);
                var img = go.GetComponent<Image>();
                img.sprite = null;
                img.color = color;
                img.raycastTarget = false;
                return go;
            }

            if (top > 0f)
            {
                var rt = Make("Top").GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(0f, top);
            }

            if (bottom > 0f)
            {
                var rt = Make("Bottom").GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(0f, bottom);
            }

            if (left > 0f)
            {
                var rt = Make("Left").GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(left, 0f);

                rt.offsetMin = new Vector2(rt.offsetMin.x, bottom);
                rt.offsetMax = new Vector2(rt.offsetMax.x, -top);
            }

            if (right > 0f)
            {
                var rt = Make("Right").GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(right, 0f);

                rt.offsetMin = new Vector2(rt.offsetMin.x, bottom);
                rt.offsetMax = new Vector2(rt.offsetMax.x, -top);
            }
        }

        static void DumpHierarchy(Transform t, int depth = 0)
        {
            string indent = new string(' ', depth * 2);
            _log.LogInfo(indent + t.name);

            foreach (Transform child in t)
                DumpHierarchy(child, depth + 1);
        }

        private static void AlignLabelVerticallyBetween(RectTransform labelRect, RectTransform bgMainRect, RectTransform submenuRect)
        {
            if (!labelRect || !bgMainRect || !submenuRect)
                return;

            var corners = new Vector3[4];

            bgMainRect.GetWorldCorners(corners);
            Vector3 bgTopCenter = (corners[1] + corners[2]) * 0.5f;

            submenuRect.GetWorldCorners(corners);
            Vector3 submenuTopCenter = (corners[1] + corners[2]) * 0.5f;

            float midY = (bgTopCenter.y + submenuTopCenter.y) * 0.5f;

            var labelWorldPos = labelRect.position;
            labelWorldPos.y = midY;
            labelRect.position = labelWorldPos;
        }


        //=========================== Events =========================== 

        private static void CraftablesForUIChanged(IReadOnlyList<CraftableEntry> craftables)
        {
            _lastCraftables = craftables;

            if (!_skeletonBuilt)
                return;

            PopulateRecipeList(_lastCraftables);
        }

        private static void ToggleRecipeRow(RecipeRowRuntime runtime)
        {
            if (runtime == null)
            {
                return;
            }

            if (_openRow == runtime)
            {
                CollapseRow(runtime);
                _openRow = null;
                return;
            }

            if (_openRow != null)
            {
                CollapseRow(_openRow);
            }

            ExpandRow(runtime);
            _openRow = runtime;
        }

        private static void ExpandRow(RecipeRowRuntime runtime)
        {
            if (runtime.Entry == null || runtime.Entry.Chains == null)
            {
                return;
            }

            int chainCount = runtime.Entry.Chains.Count;

            if (chainCount != 0)
            {
                float rowHeightPx = PathRowHeightNorm * _panelHeight;
                float spacingPx = PathsContainerSpacingNorm * _panelHeight;
                int visibleRows = Mathf.Min(chainCount, PathsContainerMaxVisibleRows);

                float targetHeight = (visibleRows * rowHeightPx) + (Mathf.Max(0, visibleRows - 1) * spacingPx);

                if (runtime.DropdownLayoutElement != null)
                {
                    runtime.DropdownLayoutElement.preferredHeight = targetHeight;
                }
                else
                {
                    _log.LogDebug("DropDownLayoutElement was null, cant expand row.");
                }

                if (runtime.RowLayoutElement != null)
                {
                    runtime.RowLayoutElement.preferredHeight = runtime.CollapsedHeight + targetHeight;
                }
                else
                {
                    _log.LogDebug("RowLayoutElement was null, cant expand row.");
                }

                if (_sharedDropdown != null)
                {
                    _sharedDropdown.OpenFor(runtime);
                }
            }

            runtime.IsExpanded = true;
            if (runtime.ArrowText != null)
            {
                runtime.ArrowText.text = "v";
            }
        }

        private static void CollapseRow(RecipeRowRuntime runtime)
        {
            if (_sharedDropdown != null && _sharedDropdown.CurrentOwner == runtime)
            {
                _sharedDropdown.Close();
            }

            if (runtime.DropdownLayoutElement != null)
            {
                runtime.DropdownLayoutElement.preferredHeight = 0f;
            }
            else
            {
                _log.LogDebug("DropDownLayoutElement was null, cant retract row.");
            }

            if (runtime.RowLayoutElement != null)
            {
                runtime.RowLayoutElement.preferredHeight = runtime.CollapsedHeight;
            }
            else
            {
                _log.LogDebug("RowLayoutElement was null, cant retract row.");
            }

            runtime.IsExpanded = false;
            if (runtime.ArrowText != null)
            {
                runtime.ArrowText.text = ">";
            }
        }

        private static void OnSearchTextChanged(string text)
        {
            if (_recipeRowUIs == null)
                return;

            if (text == null)
            {
                text = string.Empty;
            }

            text = text.Trim().ToLowerInvariant();

            foreach (var row in _recipeRowUIs)
            {

                if (row.RowGO == null)
                {
                    _log.LogDebug($"row {row.RowGO.name} is null");
                    continue;
                }
                bool matches = EntryMatchesSearch(row.Entry, text);
                row.RowGO.SetActive(matches);
            }
        }

        //=========================== Coroutines =========================== 
        // TODO: set up timer for perf analysis here
        internal static void PopulateRecipeList(IReadOnlyList<CraftableEntry> craftables)
        {
            if (!_skeletonBuilt || _recipeListContent == null)
            {
                return;
            }
            if (_runner == null)
            {
                return;
            }

            if (_activeBuildRoutine != null)
            {
                _runner.StopCoroutine(_activeBuildRoutine);
            }

            if (_cookbookRoot.activeInHierarchy)
            {
                _activeBuildRoutine = _runner.StartCoroutine(PopulateRoutine(craftables));
            }
        }

        private static IEnumerator PopulateRoutine(IReadOnlyList<CraftableEntry> craftables)
        {
            var vlg = _recipeListContent.GetComponent<VerticalLayoutGroup>();
            var canvasGroup = _recipeListContent.GetComponent<CanvasGroup>();
            if (canvasGroup)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
            }
            if (vlg)
            {
                vlg.enabled = false;
            }

            foreach (Transform child in _recipeListContent)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
            _recipeRowUIs.Clear();

            yield return null;

            if (craftables == null || craftables.Count == 0)
            {
                if (vlg)
                {
                    vlg.enabled = true;
                }
                _activeBuildRoutine = null;
                yield break;
            }

            int builtCount = 0;

            foreach (var entry in craftables)
            {
                if (entry == null) continue;

                var rowGO = CreateRecipeRow(_recipeListContent, entry);

                _recipeRowUIs.Add(new RecipeRowUI { Entry = entry, RowGO = rowGO });

                builtCount++;

                if (builtCount % 5 == 0)
                {
                    yield return null;
                }
            }

            if (vlg)
            {
                vlg.enabled = true;
                LayoutRebuilder.ForceRebuildLayoutImmediate(_recipeListContent);
            }

            if (canvasGroup)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
            }

            _activeBuildRoutine = null;
        }


        internal static void PopulateDropdown(RectTransform contentRoot, CraftableEntry entry)
        {
            if (_runner == null || !_cookbookRoot.activeInHierarchy)
            {
                return;
            }

            if (_activeDropdownRoutine != null)
            {
                _runner.StopCoroutine(_activeDropdownRoutine);
            }

            _activeDropdownRoutine = _runner.StartCoroutine(PopulateDropdownRoutine(contentRoot, entry));
        }

        private static IEnumerator PopulateDropdownRoutine(RectTransform contentRoot, CraftableEntry entry)
        {
            foreach (Transform child in contentRoot)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            yield return null;

            if (entry == null || entry.Chains == null)
            {
                _activeDropdownRoutine = null;
                yield break;
            }

            int builtCount = 0;

            foreach (var chain in entry.Chains)
            {
                CreatePathRow(contentRoot, chain);

                builtCount++;

                if (builtCount % 4 == 0)
                {
                    yield return null;
                }
            }

            _activeDropdownRoutine = null;
        }

        // ========================= Helpers =========================
        private static bool EntryMatchesSearch(CraftableEntry entry, string term)
        {
            if (string.IsNullOrEmpty(term))
            {
                return true;
            }
            if (entry == null)
            {
                return false;
            }

            string name = GetEntryDisplayName(entry);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            return name.ToLowerInvariant().Contains(term);
        }

        private static string GetEntryDisplayName(CraftableEntry entry)
        {
            if (entry == null)
            {
                _log.LogDebug($"entry {entry} is null");
                return "Unknown Result";
            }
            switch (entry.ResultKind)
            {
                case RecipeResultKind.Item:
                    var idef = ItemCatalog.GetItemDef(entry.ResultItem);
                    if (idef == null || string.IsNullOrEmpty(idef.nameToken))
                    {
                        return $"bad Item {entry.ResultItem}";
                    }
                    return Language.GetString(idef.nameToken);
                case RecipeResultKind.Equipment:
                    var edef = EquipmentCatalog.GetEquipmentDef(entry.ResultEquipment);
                    if (edef == null || string.IsNullOrEmpty(edef.nameToken))
                    {
                        return $"bad Equipment {entry.ResultEquipment}";
                    }
                    return Language.GetString(edef.nameToken);
                default:
                    return "Unknown Result";
            }
        }

        private static Sprite GetItemIcon(ItemIndex index)
        {
            var def = ItemCatalog.GetItemDef(index);
            return def != null ? def.pickupIconSprite : null;
        }

        private static Sprite GetEquipmentIcon(EquipmentIndex index)
        {
            var def = EquipmentCatalog.GetEquipmentDef(index);
            return def != null ? def.pickupIconSprite : null;
        }

        private static Color GetEntryColor(CraftableEntry entry)
        {
            PickupIndex pickupIndex = PickupIndex.none;

            switch (entry.ResultKind)
            {
                case RecipeResultKind.Item:
                    pickupIndex = PickupCatalog.FindPickupIndex(entry.ResultItem);
                    break;

                case RecipeResultKind.Equipment:
                    pickupIndex = PickupCatalog.FindPickupIndex(entry.ResultEquipment);
                    break;

                default:
                    return Color.white;
            }

            if (!pickupIndex.isValid)
            {
                return Color.white;
            }

            var pickupDef = PickupCatalog.GetPickupDef(pickupIndex);
            if (pickupDef == null)
            {
                return Color.white;
            }

            return pickupDef.baseColor;
        }

        private struct RecipeRowUI
        {
            public CraftableEntry Entry;
            public GameObject RowGO;
        }

    }
}