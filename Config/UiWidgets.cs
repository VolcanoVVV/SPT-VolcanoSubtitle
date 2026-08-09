using UnityEngine;
using UnityEngine.UI;

namespace Subtitle.Config
{
    // PhraseFilterPanel 与 DebugPhrasePanel 共用的 uGUI 构建辅助。
    // 两个面板原本各自复制了一份近似实现但外观参数略有差异，
    // 这里把差异全部参数化，保证两边视觉效果与原来完全一致。
    internal static class UiWidgets
    {
        // 内置 Arial 字体只取一次（原来每个文本元素都调用一次 GetBuiltinResource）
        private static Font s_defaultFont;
        internal static Font DefaultFont
        {
            get
            {
                if (s_defaultFont == null)
                    s_defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return s_defaultFont;
            }
        }

        internal static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        internal static Text CreateText(RectTransform parent, string name, string text, int size, TextAnchor align,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = DefaultFont;
            t.text = text;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = align;

            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            return t;
        }

        internal static Button CreateButton(RectTransform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax,
            Color bgColor, int labelSize, bool addOutline)
        {
            var rt = CreateRect(parent, name, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            var go = rt.gameObject;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            colors.pressedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            btn.colors = colors;

            var txt = CreateText(rt, "Label", label, labelSize, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            txt.color = Color.white;

            if (addOutline)
            {
                // 加个描边，防止暗背景“看不见”
                var outline = txt.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
                outline.effectDistance = new Vector2(1f, -1f);
            }

            return btn;
        }

        internal static Button CreateFlatButtonTemplate(Transform parent, string name, float height, Color bgColor,
            bool setNormalColor, Color highlightedColor, Color pressedColor, int labelSize, string initialLabel,
            Vector2 labelOffsetMin, Vector2 labelOffsetMax, bool addOutline)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, height);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            if (setNormalColor) colors.normalColor = bgColor;
            colors.highlightedColor = highlightedColor;
            colors.pressedColor = pressedColor;
            btn.colors = colors;

            var txt = CreateText(rt, "Label", initialLabel, labelSize, TextAnchor.MiddleLeft, labelOffsetMin, labelOffsetMax);
            txt.color = Color.white;

            if (addOutline)
            {
                var outline = txt.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
                outline.effectDistance = new Vector2(1f, -1f);
            }

            go.SetActive(false);
            return btn;
        }

        // 带边距的按钮实例化；layoutHeight > 0 时补齐 LayoutElement 固定高度
        internal static Button InstantiateButton(Button tpl, RectTransform parent, string text,
            Vector2 labelOffsetMin, Vector2 labelOffsetMax, bool normalizeLabel, float layoutHeight)
        {
            var btn = UnityEngine.Object.Instantiate(tpl, parent);
            btn.gameObject.SetActive(true);

            if (layoutHeight > 0f)
            {
                var le = btn.GetComponent<LayoutElement>();
                if (le == null) le = btn.gameObject.AddComponent<LayoutElement>();
                le.minHeight = layoutHeight;
                le.preferredHeight = layoutHeight;
            }

            // 注意：true = 包含 inactive 子物体
            var lbl = btn.GetComponentInChildren<Text>(true);
            if (lbl != null)
            {
                lbl.text = text;
                if (normalizeLabel)
                {
                    lbl.enabled = true;
                    if (lbl.font == null) lbl.font = DefaultFont;
                    lbl.color = Color.white;
                }

                var rt = lbl.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.offsetMin = labelOffsetMin;
                rt.offsetMax = labelOffsetMax;
            }

            return btn;
        }

        internal static void AddInfoRow(RectTransform parent, string text, bool withLayoutElement, Color color)
        {
            var row = new GameObject("Info");
            row.transform.SetParent(parent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 24f);

            if (withLayoutElement)
            {
                var le = row.AddComponent<LayoutElement>();
                le.preferredHeight = 24f;
                le.minHeight = 24f;
            }

            var t = row.AddComponent<Text>();
            t.font = DefaultFont;
            t.text = text;
            t.fontSize = 13;
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
        }

        internal static void ClearChildren(RectTransform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }

        // withScrollbar=true：PhraseFilterPanel 版（Viewport + RectMask2D + 滚动条）
        // withScrollbar=false：DebugPhrasePanel 版（Mask，无滚动条）
        internal static void MakeScrollWithContent(RectTransform parent, out ScrollRect scroll, out RectTransform content, bool withScrollbar)
        {
            if (withScrollbar)
            {
                MakeScrollWithScrollbar(parent, out scroll, out content);
                return;
            }

            var go = new GameObject("Scroll");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(6f, 6f);
            rt.offsetMax = new Vector2(-6f, -6f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

            var mask = go.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            scroll = go.AddComponent<ScrollRect>();
            scroll.horizontal = false;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(go.transform, false);
            content = contentGo.AddComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.spacing = 4f;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;
        }

        private static void MakeScrollWithScrollbar(RectTransform parent, out ScrollRect scroll, out RectTransform content)
        {
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(parent, false);

            var rt = scrollGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(6f, 6f);
            rt.offsetMax = new Vector2(-6f, -6f);

            var img = scrollGo.AddComponent<Image>();
            img.color = new Color(0.08f, 0.08f, 0.08f, 1f);

            scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;
            scroll.inertia = true;

            const float scrollbarWidth = 12f;
            const float scrollbarPadding = 2f;

            var scrollbarGo = new GameObject("Scrollbar");
            scrollbarGo.transform.SetParent(scrollGo.transform, false);
            var sbRt = scrollbarGo.AddComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 1f);
            sbRt.sizeDelta = new Vector2(scrollbarWidth, 0f);
            sbRt.anchoredPosition = new Vector2(-scrollbarPadding, 0f);

            var sbBg = scrollbarGo.AddComponent<Image>();
            sbBg.color = new Color(0.18f, 0.18f, 0.18f, 1f);

            var sb = scrollbarGo.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(scrollbarGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.anchorMin = new Vector2(0f, 0f);
            handleRt.anchorMax = new Vector2(1f, 1f);
            handleRt.offsetMin = new Vector2(2f, 2f);
            handleRt.offsetMax = new Vector2(-2f, -2f);

            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(0.55f, 0.55f, 0.55f, 1f);

            sb.handleRect = handleRt;
            sb.targetGraphic = handleImg;

            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing = scrollbarPadding;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGo.transform, false);

            var vRt = viewport.AddComponent<RectTransform>();
            vRt.anchorMin = new Vector2(0f, 0f);
            vRt.anchorMax = new Vector2(1f, 1f);
            vRt.offsetMin = Vector2.zero;
            vRt.offsetMax = new Vector2(-(scrollbarWidth + scrollbarPadding), 0f);

            var vImg = viewport.AddComponent<Image>();
            vImg.color = new Color(0f, 0f, 0f, 0.001f);

            viewport.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewport.transform, false);

            content = contentGo.AddComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(4, 4, 4, 4);

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.viewport = vRt;
            scroll.content = content;
        }
    }
}
