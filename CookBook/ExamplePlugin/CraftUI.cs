using System;
using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    internal static class CraftUI
    {
        private static ManualLogSource _log;
        private static GameObject _cookbookRoot;   // our custom panel root
        private static CraftingController _currentController;

        internal static void Init(ManualLogSource log)
        {
            _log = log;
        }

        internal static void Attach(CraftingController controller)
        {
            _currentController = controller;

            // Avoid double-attach
            if (_cookbookRoot != null)
            {
                _log.LogDebug("CraftUI.Attach: already attached, skipping.");
                return;
            }

            // Try to find the CraftingPanel instance in the scene
            var craftingPanel = UnityEngine.Object.FindObjectOfType<CraftingPanel>();
            if (!craftingPanel)
            {
                _log.LogWarning("CraftUI.Attach: CraftingPanel not found; cannot attach UI.");
                return;
            }

            var parentRect = craftingPanel.GetComponent<RectTransform>();
            if (!parentRect)
            {
                _log.LogWarning("CraftUI.Attach: CraftingPanel has no RectTransform; cannot attach UI.");
                return;
            }

            // Create root GO under the existing panel
            _cookbookRoot = new GameObject("CookBookPanel", typeof(RectTransform));
            _cookbookRoot.transform.SetParent(parentRect, worldPositionStays: false);

            var rt = _cookbookRoot.GetComponent<RectTransform>();

            // Anchor to middle-right of the existing panel
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);   // left edge, vertically centered
            rt.anchoredPosition = new Vector2(20f, 0f); // 20px to the right of parent center
            rt.sizeDelta = new Vector2(300f, parentRect.rect.height * 0.9f);

            // Background
            var image = _cookbookRoot.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.6f); // semi-transparent black

            // OPTIONAL: add a layout group later; for now, just drop a label
            CreateTitleLabel(rt);

            _log.LogInfo("CraftUI.Attach: CookBook panel created and attached.");
        }

        private static void CreateTitleLabel(RectTransform parent)
        {
            var go = new GameObject("CookBookTitle", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -10f);
            rt.sizeDelta = new Vector2(parent.rect.width - 20f, 30f);

            // If you have TextMeshPro in refs, use TMP_Text; otherwise use plain Text.
            var text = go.AddComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 18;
            text.text = "CookBook (WIP)";
            text.color = Color.white;
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
        }
    }
}
