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

        // 字幕面板和当前显示行列表
        private GameObject _subtitlePanel; // 字幕面板对象
        private readonly Stack<GameObject> _linePool = new Stack<GameObject>(); // 字幕行对象池（减少 GC）
        private readonly List<GameObject> _activeLines = new List<GameObject>(); // 当前显示的字幕行

        // 常量定义，用于控制字幕行为
        private const float FadeInTime = 0.5f; // 字幕淡入时间
        private const float FadeOutTime = 0.5f; // 字幕淡出时间
        private const float LineDuration = 3.0f; // 兜底默认：未传入时使用
        private const float CooldownTime = 0.5f; // 添加新字幕的冷却时间
        private const int MaxVisibleLines = 4; // 最多可见的字幕行数

        // 超长滚动（marquee）相关常量
        private const float MarqueeEndHoldSec = 0.8f;          // 滚动到句尾后的停留时间（随后正常淡出）
        private const float MarqueeSpeedPerFontSize = 2.5f;    // 滚动速度 = 字号 × 此系数（px/秒）
        private const float MarqueeMinSpeedPx = 10f;           // 滚动速度下限（防除零/过慢）

        // 文本测量余量：加粗等渲染的实际宽度/高度可能比 TextGenerator 一次性估计略大，
        // 适当放宽保证“文本一定装得进盒子”（修复：不换行时台词超出背景框 / 换行时台词被截断）
        private const float MeasureSlackMinX = 2f;             // 宽度最小余量（px）
        private const float MeasureSlackRatioX = 0.05f;        // 宽度比例余量（按测量宽的 5%）
        private const float MeasureSlackY = 2f;                // 高度余量（吸收行高取整误差）

        // 位置相关
        private float _stackBottomOffsetPercent = 0.12f; // 默认 12%

        private sealed class SubtitleRawText : MonoBehaviour
        {
            public string Value;
        }

        // 超长滚动（marquee）状态：挂在字幕行上随对象池复用；
        // 每次重新布局 Version+1，旧滚动协程据此自行退出，避免池化复用后新旧协程串扰
        private sealed class SubtitleMarqueeState : MonoBehaviour
        {
            public int Version;     // 布局版本号
            public bool Active;     // 本行是否处于滚动模式
            public float Duration;  // 滚动时长（与超出长度成正比）
        }

        // 冷却状态标志
        private bool _cooldownActive;
        private static readonly WaitForSeconds _cooldownWait = new WaitForSeconds(CooldownTime); // 缓存冷却等待，避免每次分配

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
            panelImage.raycastTarget = false; // 全透明背景不拦截点击

            // ★关键：布局器（从下往上堆）
            var vlg = _subtitlePanel.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = _subtitlePanel.AddComponent<VerticalLayoutGroup>();

            vlg.childControlWidth = false;        // 不强行拉满宽度（我们要根据测量宽度左右延展）
            vlg.childControlHeight = true;        // 让 VLG 接管子项高度（关键）
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.LowerCenter; // 从下方居中堆叠
            vlg.padding = new RectOffset(6, 6, 6, 6);

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

            // 冷却期间直接丢弃（原队列实现入队后立即出队，冷却期不会累积，行为等价）
            var line = CreateSubtitleLine(text, color);
            _activeLines.Add(line); // 添加到活动行列表

            // 超长滚动：显示时长至少覆盖 滚动+句尾停留，保证滚完、看清句尾后再进入正常淡出
            var mq = line.GetComponent<SubtitleMarqueeState>();
            if (mq != null && mq.Active)
                dur = Mathf.Max(dur, mq.Duration + MarqueeEndHoldSec);

            StartCoroutine(FadeSubtitle(line, true, dur)); // 开始淡入动画
            StartCoroutine(CooldownCoroutine());
        }

// 添加新字幕
public void AddSubtitle(string text, Color color)
        {
            AddSubtitle(text, color, LineDuration);
        }

        // 创建字幕行对象
        private GameObject CreateSubtitleLine(string text, Color color)
        {
            // 优先从对象池复用（减少 GC）
            GameObject row = null;
            while (_linePool.Count > 0)
            {
                row = _linePool.Pop();
                if (row != null) break; // 跳过已被外部销毁的池化对象
            }

            Text textComponent;
            if (row == null)
            {
                // Row 容器
                row = new GameObject("SubtitleRow", typeof(RectTransform));
                row.transform.SetParent(_subtitlePanel.transform, false);

                // 裁剪窗口节点（RectMask2D 默认关闭；仅超长滚动时启用，把文本限制在固定宽窗口内）
                var clipGo = new GameObject("Clip", typeof(RectTransform), typeof(RectMask2D));
                clipGo.transform.SetParent(row.transform, false);
                clipGo.GetComponent<RectMask2D>().enabled = false;

                // Text 子节点（挂在裁剪窗口下；窗口不启用裁剪时与直接挂 Row 下视觉一致）
                var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
                textGo.transform.SetParent(clipGo.transform, false);
                textComponent = textGo.GetComponent<Text>();
            }
            else
            {
                textComponent = row.GetComponentInChildren<Text>(true);
            }
            row.SetActive(true);

            // 配置 RectTransform
            var rectTransform = row.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0, 0.5f); // 锚点在面板的中间底部
            rectTransform.anchorMax = new Vector2(1, 0.5f); // 锚点在面板的中间顶部
            rectTransform.pivot = new Vector2(0.5f, 0.5f); // 轴心在字幕行中心
            rectTransform.sizeDelta = new Vector2(0, 50); // 宽度为父对象的宽度，高度50

            // 配置文本组件
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
            // 兜底：首帧真实渲染后（动态字体图集/度量彻底就绪）再重测重铺一次，
            // 防止任何“测量时机早于渲染就绪”的残余偏差（同步预热已覆盖绝大部分情况，这里双保险）
            StartCoroutine(DeferredRelayoutCoroutine(row, textComponent));
            return row;
        }

        // 首帧结束后的延迟重铺：行已被回收（池化复用）则直接退出
        private IEnumerator DeferredRelayoutCoroutine(GameObject row, Text txt)
        {
            yield return new WaitForEndOfFrame();
            if (row == null || !row.activeSelf || txt == null) yield break;
            ApplyRowLayoutAndBackground(row, txt);
        }

        // 回收字幕行到对象池
        private void RecycleSubtitleLine(GameObject line)
        {
            if (line == null) return;
            line.SetActive(false); // 超长滚动协程靠 activeSelf/版本号检测自行退出，无需手动停止
            _linePool.Push(line);
        }

        // 冷却协程，控制字幕添加的频率
        private IEnumerator CooldownCoroutine()
        {
            _cooldownActive = true;
            yield return _cooldownWait;
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

            // 2) 读取长度上限/滚动设置，并把垂直溢出改为 Overflow：
            //    即使测量仍有细微偏差也不再硬截断台词（超长滚动模式由 RectMask2D 负责裁剪）
            int capChars = GetSubtitleMaxLineChars();
            bool wrapOn = Subtitle.Config.Settings.SubtitleWrap != null && Subtitle.Config.Settings.SubtitleWrap.Value;
            bool marqueeOn = Subtitle.Config.Settings.SubtitleMarqueeEnabled == null
                || Subtitle.Config.Settings.SubtitleMarqueeEnabled.Value;
            // 长度上限的近似像素宽：中文等宽字每字约 1 个字号宽，拉丁字符更窄（估算偏保守，不会裁字）
            float capPx = capChars > 0 ? capChars * txt.fontSize : 0f;
            txt.verticalOverflow = VerticalWrapMode.Overflow;

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

            // 3.6) 先按估计尺寸铺设一次（保证当帧立即可见）
            //      注意：需要先把 txt.text 设置好，并已套用样式（字号/对齐）
            var rr = ResizeRowByMeasure(row, txt, rowRt, maxWidth, wrapOn, marqueeOn, capPx,
                padX, padY, extraX, extraY, shadowDx, shadowDy, bgSpec);

            // 3.7) ★关键：强制一次真实画布排版，让动态字体完成字形请求/度量更新。
            //      冷字形时一次性生成器测量会系统性偏小（表现为：文本超出背景盒一侧、
            //      换行后的第二行不被背景覆盖）；预热后立即重测重铺，使盒体尺寸与真实渲染一致。
            Canvas.ForceUpdateCanvases();
            rr = ResizeRowByMeasure(row, txt, rowRt, maxWidth, wrapOn, marqueeOn, capPx,
                padX, padY, extraX, extraY, shadowDx, shadowDy, bgSpec);

            // 尺寸可能已变化：标记堆叠面板重建，保证行位置/堆叠正确
            LayoutRebuilder.MarkLayoutForRebuild(panelRt);

            // 6) 可选强制文本对齐居中，使"左右对称"更自然
            if (layout.overrideTextAlignment != null)
            {
                TextAnchor ta;
                if (EnumUtil.TryParseTextAnchor(layout.overrideTextAlignment, out ta))
                    txt.alignment = ta;
            }

            // 7.5) 滚动状态：版本号 +1 使旧协程退出；滚动模式下启动新协程（池化复用安全）
            var mq = row.GetComponent<SubtitleMarqueeState>();
            if (mq == null) mq = row.AddComponent<SubtitleMarqueeState>();
            mq.Version++;
            mq.Active = rr.marquee;
            mq.Duration = 0f;
            if (rr.marquee)
            {
                // 滚动时长与超出长度成正比；速度随字号缩放
                mq.Duration = (rr.fullTextW - rr.clipW) / Mathf.Max(MarqueeMinSpeedPx, txt.fontSize * MarqueeSpeedPerFontSize);
                StartCoroutine(MarqueeScrollCoroutine(row, mq, mq.Version, txt.rectTransform, rr.posX0, rr.posX1, rr.posY));
            }

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
                bgRt.sizeDelta = new Vector2(rr.boxW, rr.boxH);
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

        // 行盒测量/铺设结果（ApplyRowLayoutAndBackground 与滚动协程共用）
        private struct RowMeasureResult
        {
            public bool marquee;            // 是否超长滚动模式
            public float boxW, boxH;        // 行盒（背景盒）尺寸
            public float windowW;           // 滚动窗口宽（仅 marquee 有效）
            public float clipW, innerH;     // 裁剪窗口尺寸
            public float fullTextW;         // 文本矩形宽
            public float posX0, posX1, posY; // 文本位置：滚动起点/终点/垂直（含投影半值补偿）
        }

        // 测量文本并铺设 行/LayoutElement/裁剪窗/文本 矩形。
        // 幂等：可重复调用（ApplyRowLayoutAndBackground 在预热字体度量后会再调一次）；
        // 背景盒只需最终尺寸，由调用方在末尾统一处理。
        private RowMeasureResult ResizeRowByMeasure(
            GameObject row, Text txt, RectTransform rowRt,
            float maxWidth, bool wrapOn, bool marqueeOn, float capPx,
            float padX, float padY, float extraX, float extraY,
            float shadowDx, float shadowDy,
            SubtitleSystem.TextStyle.BackgroundSpec bgSpec)
        {
            var r = new RowMeasureResult();

            // 第一遍测量：换行模式下按“最终换行宽度”（maxWidth 与长度上限的较小值）测
            float measureCap = maxWidth;
            if (wrapOn && capPx > 0f) measureCap = Mathf.Min(maxWidth, capPx);
            Vector2 pref1 = MeasurePreferredSize(txt, measureCap);
            // 抗低估余量：加粗等渲染比生成器估计略宽，按测量宽 5%（至少 2px）放宽，避免文本超出背景盒
            float textW = Mathf.Ceil(pref1.x) + Mathf.Max(MeasureSlackMinX, Mathf.Ceil(pref1.x * MeasureSlackRatioX));

            // 超长滚动判定：不换行 + 设了长度上限 + 开滚动 + 文本宽于窗口
            float windowW = 0f;
            if (!wrapOn && capPx > 0f && marqueeOn)
            {
                // 窗口宽 ≤ maxWidth - padding/描边余量，保证背景盒不会突破 maxWidth
                windowW = Mathf.Min(capPx, Mathf.Max(60f, maxWidth - padX * 2f - extraX * 2f));
                r.marquee = textW > windowW;
            }
            r.windowW = windowW;

            // 第二遍测量：按“最终文本宽”复测高度。
            // 渲染时会按最终宽度重新换行，行数可能多于第一遍（按 maxWidth 测）的结果；
            // 高度取两遍的大者，否则多出的行会超出盒高（换行台词的第二行不被背景覆盖）
            float finalTextW = r.marquee ? windowW : textW;
            Vector2 pref2 = MeasurePreferredSize(txt, Mathf.Max(1f, finalTextW));
            float textH = Mathf.Ceil(Mathf.Max(pref1.y, pref2.y)) + MeasureSlackY;

            // 行盒尺寸（决定“看起来从锚点左右延展”的宽度）
            if (string.Equals(bgSpec.fit, "fullRow", StringComparison.OrdinalIgnoreCase))
            {
                r.boxW = maxWidth;
                r.boxH = textH + padY * 2f + extraY * 2f;
            }
            else if (r.marquee)
            {
                // 滚动模式：背景盒按“可见窗口”尺寸，而不是完整文本宽
                r.boxW = windowW + padX * 2f + extraX * 2f;
                r.boxH = textH + padY * 2f + extraY * 2f;
            }
            else
            {
                // fit=text
                r.boxW = textW + padX * 2f + extraX * 2f;
                r.boxH = textH + padY * 2f + extraY * 2f;
            }

            // 行节点尺寸与 pivot（交给 VerticalLayoutGroup 做纵向堆叠，横向居中）
            rowRt.anchorMin = new Vector2(0.5f, 0.5f);
            rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.pivot = new Vector2(0.5f, 0.5f);
            rowRt.sizeDelta = new Vector2(r.boxW, r.boxH);

            // LayoutElement —— 让 VLG 正确垂直排版的关键
            var le = row.GetComponent<LayoutElement>();
            if (le == null) le = row.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0f;
            le.preferredWidth = r.boxW;
            le.flexibleWidth = 0f;
            le.minHeight = r.boxH;
            le.preferredHeight = r.boxH;
            le.flexibleHeight = 0f;

            // 裁剪窗口 + Text 节点（Clip 居中铺在 row 内，Text 居中铺在 Clip 内；
            // 不启用裁剪时与旧版“Text 直接挂 row 下”几何完全一致）
            var clipRt = row.transform.Find("Clip") as RectTransform;
            RectMask2D clipMask = null;
            if (clipRt == null)
            {
                // 兜底：旧池化对象可能缺 Clip 节点，懒建并把 Text 挂进去
                var clipGo = new GameObject("Clip", typeof(RectTransform), typeof(RectMask2D));
                clipGo.transform.SetParent(row.transform, false);
                clipRt = (RectTransform)clipGo.transform;
                clipMask = clipGo.GetComponent<RectMask2D>();
            }
            else
            {
                clipMask = clipRt.GetComponent<RectMask2D>();
            }
            if (txt.transform.parent != clipRt)
                txt.transform.SetParent(clipRt, false);

            r.clipW = (r.marquee ? windowW : textW) + extraX * 2f;
            r.innerH = textH + extraY * 2f;
            clipRt.anchorMin = clipRt.anchorMax = new Vector2(0.5f, 0.5f);
            clipRt.pivot = new Vector2(0.5f, 0.5f);
            clipRt.anchoredPosition = Vector2.zero;
            clipRt.sizeDelta = new Vector2(r.clipW, r.innerH);
            if (clipMask != null) clipMask.enabled = r.marquee;

            var txtRt = txt.rectTransform;
            txtRt.anchorMin = txtRt.anchorMax = new Vector2(0.5f, 0.5f);
            txtRt.pivot = new Vector2(0.5f, 0.5f);
            // 放大到文字首选尺寸 + 额外边距，避免描边/投影裁切
            r.fullTextW = textW + extraX * 2f;
            txtRt.sizeDelta = new Vector2(r.fullTextW, r.innerH);

            // 滚动起点显示句首（文本左边缘对齐窗口左边缘），终点显示句尾；非滚动时居中（0）
            float startX = r.marquee ? (r.fullTextW - r.clipW) * 0.5f : 0f;
            float endX = -startX;
            // 关键：把文本按“投影向量的一半”做反向平移，修正视觉中心（滚动时起终点同样叠加该补偿）
            r.posX0 = startX - shadowDx * 0.5f;
            r.posX1 = endX - shadowDx * 0.5f;
            r.posY = -shadowDy * 0.5f;
            txtRt.anchoredPosition = new Vector2(r.posX0, r.posY);

            return r;
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

            // 如果是淡出，回收字幕行（回池复用）
            if (!fadeIn)
            {
                _activeLines.Remove(subtitleLine);
                RecycleSubtitleLine(subtitleLine);
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

        // 共用的强制换行入口：Subtitle 与 World3D 仅设置来源不同
        private static string ApplyWrapBySetting(string src, bool wrapEnabled, int limit)
        {
            if (string.IsNullOrEmpty(src) || !wrapEnabled) return src;
            return (limit > 0) ? ForceWrapByLength(src, limit) : src;
        }

        // 超长滚动协程：等淡入结束后，从句首匀速滚到句尾，随后停在句尾等待正常淡出
        private IEnumerator MarqueeScrollCoroutine(GameObject row, SubtitleMarqueeState st, int version,
            RectTransform textRt, float startX, float endX, float posY)
        {
            if (row == null || st == null || textRt == null) yield break;

            // 等淡入结束再开始滚动
            float t = 0f;
            while (t < FadeInTime)
            {
                if (!MarqueeValid(row, st, version)) yield break;
                t += Time.deltaTime;
                yield return null;
            }

            float dur = Mathf.Max(0.01f, st.Duration);
            float el = 0f;
            while (el < dur)
            {
                if (!MarqueeValid(row, st, version)) yield break;
                el += Time.deltaTime;
                float k = Mathf.Clamp01(el / dur);
                textRt.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, k), posY);
                yield return null;
            }

            if (MarqueeValid(row, st, version))
                textRt.anchoredPosition = new Vector2(endX, posY); // 停在句尾
        }

        // 滚动协程有效性：行被回收（SetActive(false)）或重新布局（版本号变化/退出滚动模式）即失效
        private static bool MarqueeValid(GameObject row, SubtitleMarqueeState st, int version)
        {
            return row != null && row.activeSelf && st != null && st.Active && st.Version == version;
        }

        // 字幕长度上限（可见字符数，0 不限制）
        private static int GetSubtitleMaxLineChars()
        {
            try
            {
                if (Subtitle.Config.Settings.SubtitleMaxLineChars != null)
                    return Mathf.Clamp(Subtitle.Config.Settings.SubtitleMaxLineChars.Value, 0, 200);
            }
            catch { }
            return 0;
        }

        // 按可见字符数截断（跳过富文本标签）；超限时保留 cap-1 个可见字符并追加“…”
        private static string TruncateVisibleChars(string src, int cap)
        {
            if (string.IsNullOrEmpty(src) || cap <= 0) return src;

            // 先数一遍可见字符，未超限原样返回
            bool inTag = false;
            int count = 0;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '<') { inTag = true; continue; }
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (c == '\n' || c == '\r') continue;
                count++;
                if (count > cap) break;
            }
            if (count <= cap) return src;

            // 超限：保留 cap-1 个可见字符（留 1 个字符位给省略号），标签照常拷贝
            int keep = Mathf.Max(1, cap - 1);
            var sb = new System.Text.StringBuilder(src.Length + 1);
            inTag = false;
            count = 0;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '<') { inTag = true; sb.Append(c); continue; }
                if (inTag)
                {
                    sb.Append(c);
                    if (c == '>') inTag = false;
                    continue;
                }
                if (count >= keep) break; // 截断点之后的闭合标签一并丢弃（Unity 富文本容忍未闭合标签）
                sb.Append(c);
                if (c != '\n' && c != '\r') count++;
            }
            sb.Append('…');
            return sb.ToString();
        }

        // 字幕通道的文本处理入口：自动换行 / 长度上限 / 截断省略号（滚动模式保留全文，由滚动展示完整台词）
        private static string ApplySubtitleWrap(string src)
        {
            bool wrapEnabled = Subtitle.Config.Settings.SubtitleWrap != null && Subtitle.Config.Settings.SubtitleWrap.Value;
            int limit = (Subtitle.Config.Settings.SubtitleWrapLength != null)
                ? Subtitle.Config.Settings.SubtitleWrapLength.Value
                : 0;
            int cap = GetSubtitleMaxLineChars();

            if (wrapEnabled)
            {
                // 换行模式下长度上限充当换行宽度：与原换行阈值取较小值（0 表示该项不限制）
                int eff = limit > 0 ? (cap > 0 ? Mathf.Min(limit, cap) : limit) : cap;
                return ApplyWrapBySetting(src, true, eff);
            }

            // 不换行：超出上限且未开滚动 → 截断并补省略号；开滚动则保留全文
            bool marquee = Subtitle.Config.Settings.SubtitleMarqueeEnabled == null
                || Subtitle.Config.Settings.SubtitleMarqueeEnabled.Value;
            if (cap > 0 && !marquee)
                return TruncateVisibleChars(src, cap);
            return src;
        }

    }
}
