using EFT.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static SubtitleSystem.TextStyle;
using SubtitleSystem;

namespace SubtitleSystem
{
    public partial class SubtitleManager : MonoBehaviour
    {
        // 单例模式实例，用于全局访问 SubtitleManager
        private static SubtitleManager _instance;

        // 字幕面板和相关队列/列表
        private GameObject _subtitlePanel; // 字幕面板对象
        private readonly Queue<SubtitleData> _subtitleQueue = new Queue<SubtitleData>(); // 字幕数据队列
        private readonly List<GameObject> _activeLines = new List<GameObject>(); // 当前显示的字幕行

        // 常量定义，用于控制字幕行为
        private const float FadeInTime = 0.5f; // 字幕淡入时间
        private const float FadeOutTime = 0.5f; // 字幕淡出时间
        private const float LineDuration = 3.0f; // 兜底默认：未传入时使用
        private const float CooldownTime = 0.5f; // 添加新字幕的冷却时间
        private const int MaxVisibleLines = 4; // 最多可见的字幕行数

        // 位置相关
        private float _stackBottomOffsetPercent = 0.12f; // 默认 12%

        private sealed class SubtitleRawText : MonoBehaviour
        {
            public string Value;
        }

        // 冷却状态标志
        private bool _cooldownActive;

        // 单例访问器
        public static SubtitleManager Instance => _instance;

        private void Awake()
        {
            // 如果实例未初始化，则初始化；否则销毁重复实例
            if (_instance == null)
            {
                _instance = this;
                InitializePanel(); // 初始化字幕面板
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void ApplyStackSettings()
        {
            try
            {
                float p = 0.12f;
                if (Subtitle.Config.Settings.SubtitleLayoutStackOffsetPercent != null)
                    p = Subtitle.Config.Settings.SubtitleLayoutStackOffsetPercent.Value;
                _stackBottomOffsetPercent = Mathf.Clamp01(p);
                if (_stackBottomOffsetPercent > 0.5f) _stackBottomOffsetPercent = 0.5f;
            }
            catch { _stackBottomOffsetPercent = 0.12f; }
        }

        private static TextAnchor ParseLayoutAnchor(string raw)
        {
            TextAnchor ta;
            if (!string.IsNullOrEmpty(raw) && SubtitleSystem.EnumUtil.TryParseTextAnchor(raw, out ta))
                return ta;
            return TextAnchor.LowerCenter;
        }

        private static float AnchorY(TextAnchor ta)
        {
            switch (ta)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.UpperCenter:
                case TextAnchor.UpperRight:
                    return 1f;
                case TextAnchor.MiddleLeft:
                case TextAnchor.MiddleCenter:
                case TextAnchor.MiddleRight:
                    return 0.5f;
                default:
                    return 0f;
            }
        }

        private static bool IsLeft(TextAnchor ta)
        {
            return ta == TextAnchor.UpperLeft || ta == TextAnchor.MiddleLeft || ta == TextAnchor.LowerLeft;
        }

        private static bool IsRight(TextAnchor ta)
        {
            return ta == TextAnchor.UpperRight || ta == TextAnchor.MiddleRight || ta == TextAnchor.LowerRight;
        }

        private static bool IsUpper(TextAnchor ta)
        {
            return ta == TextAnchor.UpperLeft || ta == TextAnchor.UpperCenter || ta == TextAnchor.UpperRight;
        }

        public void ApplySubtitleLayoutSettings()
        {
            if (_subtitlePanel == null) return;

            ApplyStackSettings();

            var layout = Subtitle.Config.Settings.BuildSubtitleLayoutSpec();
            var anchor = ParseLayoutAnchor(layout != null ? layout.anchor : null);

            float offsetX = 0f;
            float offsetY = 0f;
            if (layout != null && layout.offset != null)
            {
                if (layout.offset.Length > 0) offsetX = (float)layout.offset[0];
                if (layout.offset.Length > 1) offsetY = (float)layout.offset[1];
            }

            float safeX = 0f;
            float safeY = 0f;
            if (layout != null && layout.safeArea)
            {
                var safe = Screen.safeArea;
                float leftPad = safe.xMin;
                float rightPad = Mathf.Max(0f, Screen.width - safe.xMax);
                float bottomPad = safe.yMin;
                float topPad = Mathf.Max(0f, Screen.height - safe.yMax);

                if (IsLeft(anchor)) safeX += leftPad;
                else if (IsRight(anchor)) safeX -= rightPad;

                if (IsUpper(anchor)) safeY -= topPad;
                else if (AnchorY(anchor) <= 0.001f) safeY += bottomPad;
            }

            var selfRt = GetComponent<RectTransform>();
            float parentH = selfRt != null ? selfRt.rect.height : Screen.height;
            float baseY = AnchorY(anchor) <= 0.001f ? Mathf.Round(parentH * _stackBottomOffsetPercent) : 0f;

            var rt = _subtitlePanel.GetComponent<RectTransform>();
            if (rt != null)
            {
                float ay = AnchorY(anchor);
                rt.anchorMin = new Vector2(0f, ay);
                rt.anchorMax = new Vector2(1f, ay);
                rt.pivot = new Vector2(0.5f, ay);
                rt.anchoredPosition = new Vector2(offsetX + safeX, baseY + offsetY + safeY);
            }

            var vlg = _subtitlePanel.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.childAlignment = anchor;

                float styleLineSpacing = (Subtitle.Config.Settings.SubtitleLayoutLineSpacing != null)
                    ? Subtitle.Config.Settings.SubtitleLayoutLineSpacing.Value
                    : 0f;
                vlg.spacing = styleLineSpacing + GetSubtitleStyleMarginY() * 2f;
            }

            if (rt != null)
                LayoutRebuilder.MarkLayoutForRebuild(rt);
        }

        // 初始化字幕面板
        private void InitializePanel()
        {
            var selfRt = GetComponent<RectTransform>();
            if (selfRt != null)
            {
                selfRt.anchorMin = new Vector2(0f, 0f);
                selfRt.anchorMax = new Vector2(1f, 1f);
                selfRt.pivot = new Vector2(0.5f, 0.5f);
                selfRt.sizeDelta = Vector2.zero;
                selfRt.anchoredPosition = Vector2.zero;
            }
            _subtitlePanel = new GameObject("SubtitleStackPanel");
            _subtitlePanel.transform.SetParent(transform, false);

            // —— 底部堆叠字幕面板（拉满宽度，锚到底部）——
            var rectTransform = _subtitlePanel.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 0f);    // 底部左
            rectTransform.anchorMax = new Vector2(1f, 0f);    // 底部右（水平拉伸）
            rectTransform.pivot = new Vector2(0.5f, 0f);  // 以“底边”为基准
            rectTransform.sizeDelta = new Vector2(0f, 240f);  // 高度给个能容 3~4 行的值
                                                              // 用“距屏幕底部的百分比”来抬高（默认 12%）
            ApplyStackSettings();

            // 添加背景图像并设置颜色（可选）
            var panelImage = _subtitlePanel.AddComponent<Image>();

            //panelImage.color = new Color(0, 0, 0, 0.5f); // 半透明黑色背景
            panelImage.color = new Color(0, 0, 0, 0); // 透明背景

            // ★关键：布局器（从下往上堆）
            var vlg = _subtitlePanel.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = _subtitlePanel.AddComponent<VerticalLayoutGroup>();

            vlg.childControlWidth = false;        // 不强行拉满宽度（我们要根据测量宽度左右延展）
            vlg.childControlHeight = true;        // 让 VLG 接管子项高度（关键）
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.LowerCenter; // 从下方居中堆叠
            vlg.padding = new RectOffset(6, 6, 6, 6);

            selfRt = gameObject.GetComponent<RectTransform>();
            if (selfRt != null)
            {
                selfRt.anchorMin = new Vector2(0f, 0f);
                selfRt.anchorMax = new Vector2(1f, 1f);
                selfRt.pivot = new Vector2(0.5f, 0.5f);
                selfRt.sizeDelta = Vector2.zero;
                selfRt.anchoredPosition = Vector2.zero;
            }

            var fitter = _subtitlePanel.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ApplySubtitleLayoutSettings();

            _subtitlePanel.SetActive(true);
            // 并列初始化弹幕层（如果没初始化过）
            InitializeDanmakuLayer();
        }

        private int _lastW = -1, _lastH = -1;
        private void LateUpdate()
        {
            if (_subtitlePanel == null) return;
            if (_lastW != Screen.width || _lastH != Screen.height)
            {
                _lastW = Screen.width; _lastH = Screen.height;
                ApplySubtitleLayoutSettings();
            }
            UpdateWorld3DBubbles();
        }

        public void SetVisible(bool visible)
        {
            if (_subtitlePanel != null)
                _subtitlePanel.SetActive(visible);
        }

        public void RefreshSubtitleStyles()
        {
            if (_subtitlePanel == null) return;
            for (int i = 0; i < _activeLines.Count; i++)
            {
                var line = _activeLines[i];
                if (line == null) continue;
                var text = line.GetComponentInChildren<Text>();
                if (text == null) continue;

                Subtitle.Config.Settings.ApplySubtitleTextOverrides(text);

                if (Subtitle.Config.Settings.SubtitleBgEnabled != null &&
                    Subtitle.Config.Settings.SubtitleBgEnabled.Value)
                    Subtitle.Config.Settings.NormalizeTextRectForBackground(text);

                string raw = null;
                var rawHolder = line.GetComponent<SubtitleRawText>();
                if (rawHolder != null && !string.IsNullOrEmpty(rawHolder.Value))
                    raw = rawHolder.Value;
                if (string.IsNullOrEmpty(raw)) raw = text.text;

                try
                {
                    text.text = ApplySubtitleWrap(raw);
                }
                catch { }

                ApplyRowLayoutAndBackground(line, text);
            }

            ApplySubtitleLayoutSettings();
        }
        // 将字幕系统附加到战斗 UI 屏幕
        public static GameObject TryAttachToBattleUIScreen(EftBattleUIScreen screen)
        {
            var root = new GameObject("SubtitleRoot", typeof(RectTransform));
            root.transform.SetParent(screen.transform, false);

            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            root.AddComponent<SubtitleManager>(); // Awake→InitializePanel 会创建子面板
            DLog("[Danmaku] Root attach to BattleUI");
            return root;
        }

        // 新：带“本行显示时长”的重载
        public void AddSubtitle(string text, Color color, float durationSec)
        {
            if (_subtitlePanel == null || _cooldownActive || _activeLines.Count >= MaxVisibleLines)
                return;
            float extraSec = 0f;
            try
            {
                if (Subtitle.Config.Settings.SubtitleDisplayDelaySec != null)
                    extraSec = Mathf.Clamp(Subtitle.Config.Settings.SubtitleDisplayDelaySec.Value, 0f, 3f);
            }
            catch { }

            float dur = durationSec > 0f ? durationSec : LineDuration;
            dur += extraSec;
            EnqueueSubtitle(text, color, dur);
        }

// 添加新字幕
public void AddSubtitle(string text, Color color)
        {
            AddSubtitle(text, color, LineDuration);
        }

        private void EnqueueSubtitle(string text, Color color, float durationSec)
        {
            if (_subtitlePanel == null || _cooldownActive || _activeLines.Count >= MaxVisibleLines)
                return;
            if (durationSec <= 0f) durationSec = LineDuration;
            _subtitleQueue.Enqueue(new SubtitleData { Text = text, TextColor = color, Duration = durationSec });
            DisplayNextSubtitle();
            StartCoroutine(CooldownCoroutine());
        }

        private void RepositionStackPanel()
        {
            ApplySubtitleLayoutSettings();
        }

        // 显示下一条字幕
        private void DisplayNextSubtitle()
        {
            if (_subtitleQueue.Count == 0) return; // 如果队列为空，则退出

            // 从队列中取出字幕数据并创建对应的字幕行
            var subtitle = _subtitleQueue.Dequeue();
            var subtitleLine = CreateSubtitleLine(subtitle.Text, subtitle.TextColor);
            _activeLines.Add(subtitleLine); // 添加到活动行列表

            // 开始淡入动画
            StartCoroutine(FadeSubtitle(subtitleLine, true, subtitle.Duration));
        }

        // 创建字幕行对象
        private GameObject CreateSubtitleLine(string text, Color color)
        {
            // Row 容器
            var row = new GameObject("SubtitleRow", typeof(RectTransform));
            row.transform.SetParent(_subtitlePanel.transform, false);

            // Text 子节点
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(row.transform, false);

            // 配置 RectTransform
            var rectTransform = row.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0, 0.5f); // 锚点在面板的中间底部
            rectTransform.anchorMax = new Vector2(1, 0.5f); // 锚点在面板的中间顶部
            rectTransform.pivot = new Vector2(0.5f, 0.5f); // 轴心在字幕行中心
            rectTransform.sizeDelta = new Vector2(0, 50); // 宽度为父对象的宽度，高度50

            // 配置文本组件
            var textComponent = textGo.GetComponent<Text>();
            textComponent.supportRichText = true;
            textComponent.fontSize = 24; // 字号
            Subtitle.Config.Settings.ApplySubtitleTextOverrides(textComponent);

            if (Subtitle.Config.Settings.SubtitleBgEnabled != null && Subtitle.Config.Settings.SubtitleBgEnabled.Value)
                Subtitle.Config.Settings.NormalizeTextRectForBackground(textComponent);

            // 最终颜色：按调用方/Setting 决定
            textComponent.color = color;

            var rawHolder = row.GetComponent<SubtitleRawText>();
            if (rawHolder == null) rawHolder = row.AddComponent<SubtitleRawText>();
            rawHolder.Value = text;

            // 根据 Setting 的 WrapLength 做可见字符强制换行
            try
            {
                text = ApplySubtitleWrap(text);
            }
            catch { }
            textComponent.text = text;

            // 套用布局/背景（会根据测量结果调整行盒宽度，实现“围绕锚点左右延展”或背景条）
            ApplyRowLayoutAndBackground(row, textComponent);
            return row;
        }

        // 冷却协程，控制字幕添加的频率
        private IEnumerator CooldownCoroutine()
        {
            _cooldownActive = true;
            yield return new WaitForSeconds(CooldownTime);
            _cooldownActive = false;
        }

        private float GetSubtitleStyleMarginY()
        {
            try
            {
                return (Subtitle.Config.Settings.SubtitleBgMarginY != null)
                    ? Subtitle.Config.Settings.SubtitleBgMarginY.Value
                    : 0f;
            }
            catch { return 0f; }
        }

        // 计算并应用布局/背景（subtitle 样式）
        private void ApplyRowLayoutAndBackground(GameObject row, Text txt)
        {
            if (row == null || txt == null) return;

            var layout = Subtitle.Config.Settings.BuildSubtitleLayoutSpec() ?? new SubtitleSystem.TextStyle.LayoutSpec();
            var bgSpec = Subtitle.Config.Settings.BuildSubtitleBackgroundSpec() ?? new SubtitleSystem.TextStyle.BackgroundSpec();

            var rowRt = row.GetComponent<RectTransform>();
            var panelRt = _subtitlePanel != null ? _subtitlePanel.GetComponent<RectTransform>() : null;
            if (rowRt == null || panelRt == null) return;

            // 1) 计算最大宽度
            float parentW = panelRt.rect.width;
            float maxPct = (float)Mathf.Clamp01((float)layout.maxWidthPercent);
            if (maxPct <= 0f) maxPct = 0.9f;
            float maxWidth = parentW * maxPct;

            // 2) 先测量文本的首选尺寸（受 Wrap 影响）
            //    注意：需要先把 txt.text 设置好，并已套用样式（字号/对齐）
            Vector2 pref = MeasurePreferredSize(txt, maxWidth);
            float textW = Mathf.Ceil(pref.x);
            float textH = Mathf.Ceil(pref.y);

            // 3) 背景 padding / margin
            float padX = 0f, padY = 0f, marY = 0f;
            if (bgSpec.padding != null && bgSpec.padding.Length >= 2)
            {
                padX = (float)bgSpec.padding[0];
                padY = (float)bgSpec.padding[1];
            }
            if (bgSpec.margin != null && bgSpec.margin.Length >= 2)
            {
                marY = (float)bgSpec.margin[1];
            }

            // 3.5) 额外视觉边距：把描边/阴影的位移计入盒子（对称加入，保持视觉居中）
            float extraX = 0f, extraY = 0f;
            float shadowDx = 0f, shadowDy = 0f;
            try
            {
                if (Subtitle.Config.Settings.SubtitleOutlineEnabled != null && Subtitle.Config.Settings.SubtitleOutlineEnabled.Value)
                {
                    var dx = (Subtitle.Config.Settings.SubtitleOutlineDistX != null) ? Subtitle.Config.Settings.SubtitleOutlineDistX.Value : 0f;
                    var dy = (Subtitle.Config.Settings.SubtitleOutlineDistY != null) ? Subtitle.Config.Settings.SubtitleOutlineDistY.Value : 0f;
                    // Outline 是四向对称膨胀，只影响盒子，不需要位移矫正
                    extraX = Mathf.Max(extraX, Mathf.Abs(dx));
                    extraY = Mathf.Max(extraY, Mathf.Abs(dy));
                }
                if (Subtitle.Config.Settings.SubtitleShadowEnabled != null && Subtitle.Config.Settings.SubtitleShadowEnabled.Value)
                {
                    shadowDx = (Subtitle.Config.Settings.SubtitleShadowDistX != null) ? Subtitle.Config.Settings.SubtitleShadowDistX.Value : 0f;
                    shadowDy = (Subtitle.Config.Settings.SubtitleShadowDistY != null) ? Subtitle.Config.Settings.SubtitleShadowDistY.Value : 0f;

                    // 投影是单向位移：既要把极值计入盒子，又要用 1/2 反向位移让视觉居中
                    extraX = Mathf.Max(extraX, Mathf.Abs(shadowDx));
                    extraY = Mathf.Max(extraY, Mathf.Abs(shadowDy));
                }
            }
            catch { /* ignore */ }

            // 4) 行盒尺寸（决定“看起来从锚点左右延展”的宽度）
            float boxW, boxH;
            if (string.Equals(bgSpec.fit, "fullRow", StringComparison.OrdinalIgnoreCase))
            {
                boxW = maxWidth;
                boxH = textH + padY * 2f + extraY * 2f;
            }
            else
            {
                // fit=text
                boxW = textW + padX * 2f + extraX * 2f;
                boxH = textH + padY * 2f + extraY * 2f;
            }

            // 5) 行节点尺寸与 pivot/对齐（交给 VerticalLayoutGroup 做纵向堆叠，横向居中）
            rowRt.anchorMin = new Vector2(0.5f, 0.5f);
            rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.pivot = new Vector2(0.5f, 0.5f);
            rowRt.sizeDelta = new Vector2(boxW, boxH);

            // ✨ 新增/更新 LayoutElement —— 这是让 VLG 正确垂直排版的关键
            var le = row.GetComponent<LayoutElement>();
            if (le == null) le = row.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0f;
            le.preferredWidth = boxW;
            le.flexibleWidth = 0f;

            le.minHeight = boxH;
            le.preferredHeight = boxH;
            le.flexibleHeight = 0f;

            // 6) 如果 grow=both，可选强制文本对齐居中，使“左右对称”更自然
            if (layout.overrideTextAlignment != null)
            {
                TextAnchor ta;
                if (EnumUtil.TryParseTextAnchor(layout.overrideTextAlignment, out ta))
                    txt.alignment = ta;
            }

            // 7) Text 节点铺在 row 内（去掉 padding 后大小等于文本）
            var txtRt = txt.rectTransform;
            txtRt.anchorMin = txtRt.anchorMax = new Vector2(0.5f, 0.5f);
            txtRt.pivot = new Vector2(0.5f, 0.5f);
            // 放大到文字首选尺寸 + 额外边距，避免描边/投影裁切
            txtRt.sizeDelta = new Vector2(textW + extraX * 2f, textH + extraY * 2f);

            // 关键：把文本按“投影向量的一半”做反向平移，修正视觉中心
            txtRt.anchoredPosition = new Vector2(-shadowDx * 0.5f, -shadowDy * 0.5f);

            // 8) 背景（可选）
            var bgTr = row.transform.Find("BG");
            if (bgSpec.enabled)
            {
                if (bgTr == null)
                {
                    var bgGo = new GameObject("BG", typeof(RectTransform), typeof(Image));
                    bgGo.transform.SetParent(row.transform, false);
                    bgTr = bgGo.transform;
                    // 放在 Text 之下
                    bgGo.transform.SetAsFirstSibling();
                }

                var bgRt = (RectTransform)bgTr;
                bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 0.5f);
                bgRt.pivot = new Vector2(0.5f, 0.5f);
                bgRt.sizeDelta = new Vector2(boxW, boxH);
                bgRt.anchoredPosition = Vector2.zero;

                var img = bgTr.GetComponent<Image>();
                Color bcol;
                if (!ColorUtil.TryParseColor(bgSpec.color, out bcol))
                    bcol = new Color(0f, 0f, 0f, 0.35f);
                img.color = bcol;

                // 可选：九宫格 sprite（若资源存在）
                if (!string.IsNullOrEmpty(bgSpec.sprite))
                {
                    var sp = Resources.Load<Sprite>(bgSpec.sprite);
                    if (sp != null)
                    {
                        img.sprite = sp;
                        img.type = Image.Type.Sliced;
                    }
                }
                else
                {
                    img.sprite = null;
                    img.type = Image.Type.Simple;
                }

                // 背景阴影（可选）
                if (bgSpec.shadow != null && bgSpec.shadow.enabled)
                {
                    Shadow s = bgTr.GetComponent<Shadow>();
                    if (s == null) s = bgTr.gameObject.AddComponent<Shadow>();
                    s.useGraphicAlpha = bgSpec.shadow.useGraphicAlpha;
                    Color sc;
                    if (ColorUtil.TryParseColor(bgSpec.shadow.color, out sc))
                        s.effectColor = sc;
                    if (bgSpec.shadow.distance != null && bgSpec.shadow.distance.Length >= 2)
                        s.effectDistance = new Vector2((float)bgSpec.shadow.distance[0], (float)bgSpec.shadow.distance[1]);
                }
                else
                {
                    var s = bgTr.GetComponent<Shadow>();
                    if (s != null) Destroy(s);
                }
            }
            else
            {
                if (bgTr != null) Destroy(bgTr.gameObject);
            }
        }

        // 计算 UGUI Text 的首选尺寸（考虑最大宽度，用于自动换行）
        private static Vector2 MeasurePreferredSize(Text txt, float maxWidth)
        {
            if (txt == null) return Vector2.zero;


            // 生成参数：extents.x = 最大宽度；extents.y 设 0 即可
            var settings = txt.GetGenerationSettings(new Vector2(maxWidth, 0f));

            // 用内置的布局生成器测出宽高；注意要除以像素比
            float w = txt.cachedTextGeneratorForLayout.GetPreferredWidth(txt.text, settings) / txt.pixelsPerUnit;
            float h = txt.cachedTextGeneratorForLayout.GetPreferredHeight(txt.text, settings) / txt.pixelsPerUnit;

            // 取整以避免抖动
            return new Vector2(Mathf.Ceil(w), Mathf.Ceil(h));
        }

        // 淡入或淡出字幕
        private IEnumerator FadeSubtitle(GameObject subtitleLine, bool fadeIn, float durationSec)
        {
            var canvasGroup = subtitleLine.GetComponent<CanvasGroup>() ?? subtitleLine.AddComponent<CanvasGroup>();
            float elapsedTime = 0f;

            // 淡入或淡出动画
            while (elapsedTime < (fadeIn ? FadeInTime : FadeOutTime))
            {
                elapsedTime += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(fadeIn ? 0 : 1, fadeIn ? 1 : 0, elapsedTime / (fadeIn ? FadeInTime : FadeOutTime));
                yield return null;
            }

            // 如果是淡出，移除字幕行
            if (!fadeIn)
            {
                _activeLines.Remove(subtitleLine);
                Destroy(subtitleLine);
            }
            else
            {
                yield return new WaitForSeconds(durationSec);
                StartCoroutine(FadeSubtitle(subtitleLine, false, 0f));
            }
        }

 

        private static string ForceWrapByLength(string src, int limit)
        {
            if (string.IsNullOrEmpty(src) || limit <= 0) return src;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(src.Length + 16);
            bool inTag = false;
            int count = 0;

            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];

                if (c == '<')
                {
                    inTag = true;
                    sb.Append(c);
                    continue;
                }
                if (inTag)
                {
                    sb.Append(c);
                    if (c == '>') inTag = false;
                    continue;
                }

                // 可见字符
                sb.Append(c);
                if (c != '\n' && c != '\r')
                {
                    count++;
                    if (count >= limit)
                    {
                        sb.Append('\n');
                        count = 0;
                    }
                }
            }
            return sb.ToString();
        }

        private static string ApplySubtitleWrap(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;

            bool wrapEnabled = Subtitle.Config.Settings.SubtitleWrap != null && Subtitle.Config.Settings.SubtitleWrap.Value;
            int limit = (Subtitle.Config.Settings.SubtitleWrapLength != null)
                ? Subtitle.Config.Settings.SubtitleWrapLength.Value
                : 0;

            if (!wrapEnabled) return src;
            return (limit > 0) ? ForceWrapByLength(src, limit) : src;
        }

    }



    // 字幕数据结构
    public class SubtitleData
    {
        public string Text; // 字幕文本
        public Color TextColor; // 字幕颜色
        public float Duration;  //字幕显示时长
    }
}
