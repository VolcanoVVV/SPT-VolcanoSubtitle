using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Subtitle.Config;
using Subtitle.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Subtitle.SettingsUI
{
    /// <summary>
    /// 图形化设置窗口：三栏布局 —— 左：分类列表 / 中：设置控件 / 右：实时预览（SettingsPreviewPanel）。
    /// 数据来源为 Settings.ConfigEntries（Bind 顺序即显示顺序）；控件只读写 entry.Value，
    /// 下游 SettingChanged → 运行期刷新沿用现有链路，F12 ConfigurationManager 不受影响。
    /// </summary>
    public class SettingsWindow : MonoBehaviour
    {
        private static SettingsWindow s_instance;

        private Canvas _canvas;
        private CanvasScaler _canvasScaler;
        private GameObject _windowRoot;
        private RectTransform _windowRt;
        // 整窗透明度（含全屏暗背景）：对应 Settings.SettingsWindowOpacity，仅视觉透明，不影响射线拦截
        private CanvasGroup _windowCanvasGroup;

        private RectTransform _catContent;
        private ScrollRect _catScroll;
        private Button _catBtnTpl;
        private readonly List<Button> _catButtons = new List<Button>();

        private RectTransform _centerContent;
        private ScrollRect _centerScroll;
        private Text _hintText;
        private Text _titleText;
        private Text _closeLabel;
        private Text _currentCategoryTitle;

        // 跟随鼠标的浮动说明框：作为 Canvas 直接子节点（不挂在 _windowRoot/CanvasGroup 下），
        // 因此不受“设置界面 不透明度”影响；且在 BuildUI 最后创建，同级顺序最高，渲染在窗口之上
        private RectTransform _floatingHintRt;
        private Text _floatingHintText;
        // 文本超过该宽度（参考分辨率单位）自动换行，面板按内容自适应
        private const float FloatingHintMaxWidth = 360f;
        // 默认显示在光标右下方
        private static readonly Vector2 FloatingHintOffset = new Vector2(14f, -14f);
        private const float FloatingHintScreenMargin = 4f;

        // 破坏性重置的二次确认：第一次点击只改文案并记录时间戳，ConfirmWindowSec 秒内再点才真正执行，
        // 超时未再点则由 Update 自动把文案还原（时间戳检查，不用协程）
        private const float ConfirmWindowSec = 3f;
        private Text _resetAllLabel;
        private Text _resetCatLabel;
        private float _resetAllArmedAt = -999f;
        private float _resetCatArmedAt = -999f;

        private static string ResetAllText { get { return I18n.Text("Reset.All", "全部重置"); } }
        private static string ResetAllConfirmText { get { return I18n.Text("Reset.AllConfirm", "确认全部重置？"); } }
        private static string ResetCategoryText { get { return I18n.Text("Reset.Category", "重置本板块"); } }
        private static string ResetCategoryConfirmText { get { return I18n.Text("Reset.CategoryConfirm", "确认重置？"); } }
        // 说明栏默认文本走 I18n（中文原文作回落值）
        private static string DefaultHint
        {
            get { return I18n.Text("HintBarDefault", "将鼠标悬停在设置项上查看说明。"); }
        }

        // 分类：section 原名 + 显示名 + 过滤后的可见条目；只构建一次，切换分类时仅重建中栏
        private class Category
        {
            public string Section;
            public string DisplayName;
            public List<ConfigEntryBase> Entries;
            public int SortOrder;
        }
        private readonly List<Category> _categories = new List<Category>();
        private int _currentCategory = -1;

        // 光标：打开时保存并释放，关闭时还原（局内同样生效）
        private bool _cursorSaved;
        private CursorLockMode _prevLockState;
        private bool _prevCursorVisible;

        private static readonly Color CatNormal = new Color(0.20f, 0.20f, 0.20f, 1f);
        private static readonly Color CatSelected = new Color(0.40f, 0.40f, 0.40f, 1f);

        public bool IsVisible
        {
            get { return _windowRoot != null && _windowRoot.activeSelf; }
        }

        /// <summary>常驻创建（默认隐藏）。在 Plugin.Awake 中调用；热键轮询在 Plugin.Update。</summary>
        public static void EnsureCreated()
        {
            if (s_instance != null) return;
            var go = new GameObject("SubtitleSettingsWindow");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<SettingsWindow>();
        }

        public static void ToggleVisible()
        {
            EnsureCreated();
            s_instance.Toggle();
        }

        private void Awake()
        {
            // 诊断：构建失败时窗口会永远打不开（Toggle 因 _windowRoot 为空而静默返回），必须落日志
            try
            {
                BuildCategories();
                BuildUI();
                if (_categories.Count > 0) SelectCategory(0);
                Hide();
                Subtitle.Plugin.Log?.LogInfo($"[SettingsUI] 设置界面已构建（{_categories.Count} 个分类），默认隐藏。");
            }
            catch (Exception e)
            {
                Subtitle.Plugin.Log?.LogError("[SettingsUI] 设置界面构建失败：" + e);
            }
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            // 防御：窗口在打开状态下被销毁时把光标还回去
            if (_cursorSaved) RestoreCursor();
        }

        private void Update()
        {
            if (!IsVisible) return;
            // 浮动说明框每帧跟随鼠标（窗口隐藏时它也必然隐藏，无需处理）
            if (_floatingHintRt != null && _floatingHintRt.gameObject.activeSelf)
                FollowFloatingHint();
            // 二次确认文案超时自动还原（两个破坏性按钮各自独立计时）
            if (_resetAllLabel != null && Time.unscaledTime - _resetAllArmedAt > ConfirmWindowSec
                && _resetAllLabel.text != ResetAllText)
            {
                _resetAllArmedAt = -999f;
                _resetAllLabel.text = ResetAllText;
            }
            if (_resetCatLabel != null && Time.unscaledTime - _resetCatArmedAt > ConfirmWindowSec
                && _resetCatLabel.text != ResetCategoryText)
            {
                _resetCatArmedAt = -999f;
                _resetCatLabel.text = ResetCategoryText;
            }
        }

        private void Toggle()
        {
            if (_windowRoot == null)
            {
                Subtitle.Plugin.Log?.LogWarning("[SettingsUI] Toggle 被调用但窗口未构建（_windowRoot 为空）。");
                return;
            }
            if (IsVisible) Hide();
            else Show();
            Subtitle.Plugin.Log?.LogInfo("[SettingsUI] 设置界面已" + (IsVisible ? "打开。" : "关闭。"));
        }

        private void Show()
        {
            _windowRoot.SetActive(true);
            SaveAndFreeCursor();
            // 打开时重新应用一次不透明度（窗口关闭期间若从 F12 改过值也能立即生效）
            ApplyOpacityInstance();
            // 每次打开都重建一次中栏，让控件读到的 entry.Value 是最新的
            if (_currentCategory < 0 && _categories.Count > 0) _currentCategory = 0;
            if (_currentCategory >= 0 && _currentCategory < _categories.Count)
                SelectCategory(_currentCategory);
        }

        private void Hide()
        {
            if (_windowRoot != null) _windowRoot.SetActive(false);
            HideFloatingHintInstance(); // 防御：窗口关闭时浮动说明框一并收起，避免残留
            RestoreCursor();
        }

        // ---------- 光标 ----------

        private void SaveAndFreeCursor()
        {
            if (_cursorSaved) return;
            _prevLockState = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            _cursorSaved = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RestoreCursor()
        {
            if (!_cursorSaved) return;
            _cursorSaved = false;
            Cursor.lockState = _prevLockState;
            Cursor.visible = _prevCursorVisible;
        }

        // ---------- 分类 ----------

        private void BuildCategories()
        {
            _categories.Clear();
            var entries = Settings.ConfigEntries;
            if (entries == null) return;

            var bySection = new Dictionary<string, Category>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (SettingControlFactory.ShouldSkip(entry)) continue;

                // 旧版“不透明度”配置键仍留在测试 section，GUI 中虚拟归入界面板块以保持 cfg 兼容。
                string section = ReferenceEquals(entry, Settings.SettingsWindowOpacity)
                    ? Settings.InterfaceSection
                    : entry.Definition.Section;
                Category cat;
                if (!bySection.TryGetValue(section, out cat))
                {
                    cat = new Category
                    {
                        Section = section,
                        // 显示名走 I18n；语言表缺失时回落到原 FormatSectionName（去数字前缀 + 调试区特判）
                        DisplayName = I18n.Category(section, FormatSectionName(section)),
                        Entries = new List<ConfigEntryBase>(),
                        SortOrder = GetCategorySortOrder(section)
                    };
                    bySection.Add(section, cat);
                    _categories.Add(cat); // 首见顺序即显示顺序
                }
                cat.Entries.Add(entry);
            }
            // 过滤后没有可见条目的分类不显示（例如某分类的条目全部被 ShouldSkip 跳过）
            _categories.RemoveAll(c => c.Entries == null || c.Entries.Count == 0);
            _categories.Sort(delegate(Category a, Category b)
            {
                int byOrder = a.SortOrder.CompareTo(b.SortOrder);
                return byOrder != 0 ? byOrder : string.Compare(a.Section, b.Section, StringComparison.Ordinal);
            });
        }

        private static int GetCategorySortOrder(string section)
        {
            if (string.Equals(section, "1. 通用", StringComparison.Ordinal)) return 100;
            if (string.Equals(section, Settings.InterfaceSection, StringComparison.Ordinal)) return 110;

            if (string.Equals(section, "2 字幕 - 通用", StringComparison.Ordinal)) return 200;
            if (string.Equals(section, "2.1 字幕 - 进阶", StringComparison.Ordinal)) return 210;
            if (string.Equals(section, "2.2 字幕 - 角色颜色", StringComparison.Ordinal)) return 220;
            if (string.Equals(section, "2.3 字幕 - 角色文本颜色", StringComparison.Ordinal)) return 230;

            if (string.Equals(section, "3 弹幕 - 通用", StringComparison.Ordinal)) return 300;
            if (string.Equals(section, "3.1 弹幕 - 进阶", StringComparison.Ordinal)) return 310;
            if (string.Equals(section, "3.2 弹幕 - 角色颜色", StringComparison.Ordinal)) return 320;
            if (string.Equals(section, "3.3 弹幕 - 角色文本颜色", StringComparison.Ordinal)) return 330;

            if (string.Equals(section, "4 3D气泡 - 通用", StringComparison.Ordinal)) return 400;
            if (string.Equals(section, "4.1 3D气泡 - 进阶", StringComparison.Ordinal)) return 410;
            if (string.Equals(section, "4.2 3D气泡 - 角色颜色", StringComparison.Ordinal)) return 420;
            if (string.Equals(section, "4.3 3D气泡 - 角色文本颜色", StringComparison.Ordinal)) return 430;

            if (string.Equals(section, "99. 测试", StringComparison.Ordinal)) return 9900;
            return 9000;
        }

        private static string FormatSectionName(string section)
        {
            // 调试区现在进 GUI，给一个更明确的显示名
            if (string.Equals(section, "99. 测试", StringComparison.Ordinal)) return "测试/调试";
            // 去掉排序用的数字前缀："2.1 字幕 - 进阶" → "字幕 - 进阶"
            if (string.IsNullOrEmpty(section)) return "其他";
            int i = 0;
            while (i < section.Length && (char.IsDigit(section[i]) || section[i] == '.')) i++;
            while (i < section.Length && section[i] == ' ') i++;
            return i > 0 && i < section.Length ? section.Substring(i) : section;
        }

        private void BuildCategoryList()
        {
            UiWidgets.ClearChildren(_catContent);
            _catButtons.Clear();

            for (int i = 0; i < _categories.Count; i++)
            {
                var btn = UiWidgets.InstantiateButton(_catBtnTpl, _catContent, _categories[i].DisplayName,
                    new Vector2(8f, 0f), new Vector2(-6f, 0f), true, 0f);
                _catButtons.Add(btn);
                int captured = i;
                btn.onClick.AddListener(delegate { SelectCategory(captured); });
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_catContent);
        }

        private void SelectCategory(int index)
        {
            if (index < 0 || index >= _categories.Count) return;
            _currentCategory = index;
            for (int i = 0; i < _catButtons.Count; i++)
                SetButtonBg(_catButtons[i], i == index ? CatSelected : CatNormal);
            if (_currentCategoryTitle != null)
                _currentCategoryTitle.text = _categories[index].DisplayName;
            RebuildCenter(_categories[index]);
            SetHint(null);
        }

        private static void SetButtonBg(Button btn, Color c)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = c;
        }

        // ---------- 中栏 ----------

        private void RebuildCenter(Category cat)
        {
            HideFloatingHintInstance(); // 防御：行被销毁后 PointerExit 不会再触发，先收起浮动说明框
            UiWidgets.ClearChildren(_centerContent);
            if (cat == null || cat.Entries == null) return;

            // 分类顶部的特殊行（预设/字体包选择器、台词面板与测试按钮 —— 原 F12 自绘按钮的 GUI 等价物）
            SettingControlFactory.BuildSpecialRows(cat.Section, _centerContent, SetHint, RefreshCurrentCategory);

            // 普通条目：按 Indent 元数据归成“开关父项 + 子选项组”（父关 → 子项置灰；父行带折叠按钮），
            // 父开关切换时保留滚动位置整栏重建以刷新子项置灰状态
            SettingControlFactory.BuildGrouped(cat.Entries, _centerContent, SetHint, RefreshCurrentCategoryKeepScroll);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_centerContent);
            if (_centerScroll != null) _centerScroll.verticalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
        }

        // 应用预设后大量 entry 被批量改写：重建当前分类中栏，让已建控件重新读取最新值
        internal void RefreshCurrentCategory()
        {
            if (_currentCategory >= 0 && _currentCategory < _categories.Count)
                SelectCategory(_currentCategory);
        }

        // 组父开关切换后的整栏重建：与 RefreshCurrentCategory 相同，但保留滚动位置（不回顶）
        private void RefreshCurrentCategoryKeepScroll()
        {
            float pos = _centerScroll != null ? _centerScroll.verticalNormalizedPosition : 1f;
            RefreshCurrentCategory();
            if (_centerScroll != null) _centerScroll.verticalNormalizedPosition = pos;
        }

        // 重置本板块：当前 section 下所有可重置条目恢复默认值（含特殊行背后的值条目，如字体资源包名），
        // 纯动作条目（按钮/折叠开关）由 Settings.IsResettable 排除；完成后重建中栏让控件显示默认值。
        private void ResetCurrentCategoryEntries()
        {
            if (_currentCategory < 0 || _currentCategory >= _categories.Count) return;
            var entries = _categories[_currentCategory].Entries;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e == null || e.Definition == null) continue;
                    if (!Settings.IsResettable(e)) continue;
                    Settings.ResetEntryToDefault(e);
                }
            }
            RefreshCurrentCategory();
        }

        // 全部重置：所有 section 的可重置条目恢复默认值。
        // 语言条目被重置时会触发其 SettingChanged 钩子（I18n 重载 + RebuildAll 整体重建），
        // 循环结束后再统一 ApplyChromeTexts + RefreshCurrentCategory 收尾，
        // 保证中栏/标题栏显示的是全部重置完成后的最终状态（不会产生额外的重复重建）。
        private void ResetAllEntries()
        {
            var entries = Settings.ConfigEntries;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (!Settings.IsResettable(e)) continue;
                    Settings.ResetEntryToDefault(e);
                }
            }
            ApplyChromeTexts();
            RefreshCurrentCategory();
        }

        /// <summary>
        /// 整体重建（语言切换时调用）：标题栏文本 + 分类列表 + 当前分类中栏全部按最新语言表重建。
        /// </summary>
        public static void RebuildAll()
        {
            if (s_instance == null) return;
            s_instance.RebuildAllInstance();
        }

        /// <summary>
        /// 把“设置界面 不透明度”应用到整窗 CanvasGroup（由该 entry 的 SettingChanged 钩子调用）。
        /// 窗口尚未构建时静默忽略（构建时会自行应用一次）。
        /// </summary>
        public static void ApplyOpacity()
        {
            if (s_instance == null) return;
            s_instance.ApplyOpacityInstance();
        }

        public static void ApplyScale()
        {
            if (s_instance == null) return;
            s_instance.ApplyScaleInstance();
        }

        private void ApplyOpacityInstance()
        {
            if (_windowCanvasGroup == null || Settings.SettingsWindowOpacity == null) return;
            // 再钳一次（防御 cfg 手改越界值）；blocksRaycasts 不动 —— 只是视觉透明
            _windowCanvasGroup.alpha = Mathf.Clamp(Settings.SettingsWindowOpacity.Value, 0.2f, 1.0f);
        }

        private void ApplyScaleInstance()
        {
            if (_canvasScaler == null) return;
            float scale = Settings.InterfaceScale == null ? 1f : Mathf.Clamp(Settings.InterfaceScale.Value, 0.75f, 1.30f);
            _canvasScaler.referenceResolution = new Vector2(1920f / scale, 1080f / scale);
            Canvas.ForceUpdateCanvases();
            ClampWindowToScreen();
        }

        private void ClampWindowToScreen()
        {
            if (_windowRt == null || _canvas == null) return;
            float scale = _canvas.scaleFactor <= 0f ? 1f : _canvas.scaleFactor;
            float screenW = Screen.width / scale;
            float screenH = Screen.height / scale;
            Vector2 size = _windowRt.sizeDelta;
            size.x = Mathf.Min(size.x, screenW);
            size.y = Mathf.Min(size.y, screenH);
            _windowRt.sizeDelta = size;

            float maxX = Mathf.Max(0f, (screenW - size.x) * 0.5f);
            float maxY = Mathf.Max(0f, (screenH - size.y) * 0.5f);
            Vector2 pos = _windowRt.anchoredPosition;
            pos.x = Mathf.Clamp(pos.x, -maxX, maxX);
            pos.y = Mathf.Clamp(pos.y, -maxY, maxY);
            _windowRt.anchoredPosition = pos;
        }

        private void RebuildAllInstance()
        {
            if (_windowRoot == null) return;
            try
            {
                ApplyChromeTexts();
                BuildCategories();
                BuildCategoryList();
                if (_currentCategory < 0 || _currentCategory >= _categories.Count) _currentCategory = 0;
                if (_categories.Count > 0) SelectCategory(_currentCategory);
            }
            catch (Exception e)
            {
                Subtitle.Plugin.Log?.LogError("[SettingsUI] 界面整体重建失败：" + e);
            }
        }

        private void SetHint(string text)
        {
            if (_hintText == null) return;
            _hintText.text = string.IsNullOrEmpty(text) ? DefaultHint : text;
        }

        // ---------- 浮动说明框（跟随鼠标） ----------

        /// <summary>行悬停进入时由 SettingControlFactory.AttachHint 调用；文本为空时不显示（说明栏默认文本逻辑不受影响）。</summary>
        public static void ShowFloatingHint(string text)
        {
            if (s_instance != null) s_instance.ShowFloatingHintInstance(text);
        }

        /// <summary>行悬停离开时由 SettingControlFactory.AttachHint 调用。</summary>
        public static void HideFloatingHint()
        {
            if (s_instance != null) s_instance.HideFloatingHintInstance();
        }

        private void ShowFloatingHintInstance(string text)
        {
            if (_floatingHintRt == null || _floatingHintText == null) return;
            if (string.IsNullOrEmpty(text))
            {
                HideFloatingHintInstance();
                return;
            }

            _floatingHintText.text = text;
            // 与 SettingsPreviewPanel 同一套测量：先按最大宽度测首选宽，再按最终宽度测一次高（宽度收窄会增加换行行数）
            var settingsW = _floatingHintText.GetGenerationSettings(new Vector2(FloatingHintMaxWidth, 0f));
            float prefW = _floatingHintText.cachedTextGeneratorForLayout.GetPreferredWidth(text, settingsW) / _floatingHintText.pixelsPerUnit;
            float textW = Mathf.Min(Mathf.Ceil(prefW), FloatingHintMaxWidth);
            var settingsH = _floatingHintText.GetGenerationSettings(new Vector2(textW, 0f));
            float prefH = _floatingHintText.cachedTextGeneratorForLayout.GetPreferredHeight(text, settingsH) / _floatingHintText.pixelsPerUnit;
            // 面板 = 文本 + 内边距（横 8×2 / 纵 6×2，与 BuildFloatingHint 中文本偏移一致）
            _floatingHintRt.sizeDelta = new Vector2(textW + 16f, Mathf.Ceil(prefH) + 12f);

            _floatingHintRt.gameObject.SetActive(true);
            FollowFloatingHint(); // 立即定位一次，避免首帧闪在旧位置
        }

        private void HideFloatingHintInstance()
        {
            if (_floatingHintRt != null) _floatingHintRt.gameObject.SetActive(false);
        }

        // 每帧跟随：屏幕坐标 ÷ scaleFactor 换算成画布单位（与标题栏拖拽同一套换算）；
        // 默认在光标右下方，靠近屏幕右/下边缘时翻转到左/上方，最终钳制在屏幕内
        private void FollowFloatingHint()
        {
            if (_floatingHintRt == null || _canvas == null) return;
            float scale = _canvas.scaleFactor;
            if (scale <= 0f) scale = 1f;

            Vector2 mouse = Input.mousePosition / scale;
            Vector2 size = _floatingHintRt.sizeDelta;
            float screenW = Screen.width / scale;
            float screenH = Screen.height / scale;

            // pivot(0,1)：anchoredPosition 即面板左上角
            float x = mouse.x + FloatingHintOffset.x;
            float y = mouse.y + FloatingHintOffset.y;
            if (x + size.x > screenW - FloatingHintScreenMargin)
                x = mouse.x - FloatingHintOffset.x - size.x; // 右侧放不下 → 翻到光标左边
            if (y - size.y < FloatingHintScreenMargin)
                y = mouse.y - FloatingHintOffset.y + size.y; // 下方放不下 → 翻到光标上方

            x = Mathf.Clamp(x, FloatingHintScreenMargin,
                Mathf.Max(FloatingHintScreenMargin, screenW - FloatingHintScreenMargin - size.x));
            y = Mathf.Clamp(y, FloatingHintScreenMargin + size.y,
                Mathf.Max(FloatingHintScreenMargin + size.y, screenH - FloatingHintScreenMargin));
            _floatingHintRt.anchoredPosition = new Vector2(x, y);
        }

        private void BuildFloatingHint(Transform canvasTransform)
        {
            var rt = UiWidgets.CreateRect(canvasTransform, "FloatingHint",
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rt.pivot = new Vector2(0f, 1f); // 左上角对齐锚点，定位时直接用面板左上角
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.92f);
            img.raycastTarget = false; // 不拦截射线，避免挡住行的悬停/点击

            // 文本相对面板四边内缩（横 8 / 纵 6）：offsetMin 是相对左下锚点的“左/下”内边距，
            // offsetMax 是相对右上锚点的“右/上”内边距（取负值），纵向写反会让文本框比面板高 12px 并整体上移
            _floatingHintText = UiWidgets.CreateText(rt, "Text", "", 13, TextAnchor.UpperLeft,
                new Vector2(8f, 6f), new Vector2(-8f, -6f));
            _floatingHintText.raycastTarget = false;
            _floatingHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _floatingHintText.verticalOverflow = VerticalWrapMode.Overflow;

            _floatingHintRt = rt;
            rt.gameObject.SetActive(false); // 默认隐藏
        }

        // 标题栏/说明栏等界面文本按当前语言表重刷（语言切换时由 RebuildAll 调用）
        private void ApplyChromeTexts()
        {
            if (_titleText != null) _titleText.text = I18n.Text("WindowTitle", "火山家的实时字幕 · 设置");
            if (_closeLabel != null) _closeLabel.text = I18n.Text("Close", "关闭");
            // 两个重置按钮的文案也随语言重刷，并顺带解除可能挂着的二次确认状态
            if (_resetAllLabel != null) { _resetAllArmedAt = -999f; _resetAllLabel.text = ResetAllText; }
            if (_resetCatLabel != null) { _resetCatArmedAt = -999f; _resetCatLabel.text = ResetCategoryText; }
            if (_currentCategoryTitle != null && _currentCategory >= 0 && _currentCategory < _categories.Count)
                _currentCategoryTitle.text = _categories[_currentCategory].DisplayName;
            SetHint(null);
        }

        // ---------- UI 构建 ----------

        private void BuildUI()
        {
            // 独立 Canvas：ScreenSpaceOverlay；台词控制面板使用 5003，可从本窗口上层打开。
            var goCanvas = new GameObject("SettingsWindowCanvas");
            goCanvas.transform.SetParent(transform, false);
            _canvas = goCanvas.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5002;
            _canvas.pixelPerfect = true;
            _canvasScaler = goCanvas.AddComponent<CanvasScaler>();
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
            _canvasScaler.matchWidthOrHeight = 0.5f;
            ApplyScaleInstance();
            goCanvas.AddComponent<GraphicRaycaster>();

            // 全屏暗背景
            _windowRoot = new GameObject("WindowRoot");
            _windowRoot.transform.SetParent(goCanvas.transform, false);
            var rootRt = _windowRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            var rootImg = _windowRoot.AddComponent<Image>();
            rootImg.color = new Color(0f, 0f, 0f, 0.4f);
            // 整窗透明度（设置界面 不透明度）：blocksRaycasts 保持默认 true，只调 alpha
            _windowCanvasGroup = _windowRoot.AddComponent<CanvasGroup>();
            ApplyOpacity();

            // 窗口本体：1400×800（参考分辨率），居中，可拖拽
            var winGo = new GameObject("Window");
            winGo.transform.SetParent(_windowRoot.transform, false);
            _windowRt = winGo.AddComponent<RectTransform>();
            _windowRt.anchorMin = new Vector2(0.5f, 0.5f);
            _windowRt.anchorMax = new Vector2(0.5f, 0.5f);
            _windowRt.pivot = new Vector2(0.5f, 0.5f);
            _windowRt.sizeDelta = new Vector2(1400f, 800f);
            _windowRt.anchoredPosition = Vector2.zero;
            var winImg = winGo.AddComponent<Image>();
            winImg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            BuildTitleBar(winGo.transform);
            BuildBody(_windowRt);
            BuildResizeHandle(winGo.transform);
            BuildCategoryList();
            // 浮动说明框最后创建：Canvas 直接子节点 + 同级顺序最高 → 始终渲染在窗口之上且不受整窗透明度影响
            BuildFloatingHint(goCanvas.transform);
            ApplyScaleInstance();
        }

        private void BuildTitleBar(Transform parent)
        {
            var top = UiWidgets.CreateRect(parent, "TitleBar",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -40f), Vector2.zero);
            var topImg = top.gameObject.AddComponent<Image>();
            topImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);

            _titleText = UiWidgets.CreateText(top, "Title", I18n.Text("WindowTitle", "火山字幕 · 设置"), 18, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            var titleRt = _titleText.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0f);
            titleRt.anchorMax = new Vector2(0.85f, 1f);
            titleRt.offsetMin = new Vector2(12f, 0f);
            titleRt.offsetMax = Vector2.zero;

            // “全部重置”（关闭按钮左侧，右锚定固定宽）：二次确认后把所有条目恢复默认值
            var resetAll = UiWidgets.CreateButton(top, "ResetAll", ResetAllText,
                new Vector2(1f, 0.15f), new Vector2(1f, 0.85f),
                new Color(0.25f, 0.25f, 0.25f, 1f), 13, true);
            var resetAllRt = (RectTransform)resetAll.transform;
            resetAllRt.offsetMin = new Vector2(-180f, 0f);
            resetAllRt.offsetMax = new Vector2(-84f, 0f);
            _resetAllLabel = resetAll.GetComponentInChildren<Text>(true);
            resetAll.onClick.AddListener(delegate
            {
                if (Time.unscaledTime - _resetAllArmedAt <= ConfirmWindowSec)
                {
                    _resetAllArmedAt = -999f;
                    if (_resetAllLabel != null) _resetAllLabel.text = ResetAllText;
                    ResetAllEntries();
                }
                else
                {
                    _resetAllArmedAt = Time.unscaledTime;
                    if (_resetAllLabel != null) _resetAllLabel.text = ResetAllConfirmText;
                }
            });

            var close = UiWidgets.CreateButton(top, "Close", I18n.Text("Close", "关闭"),
                new Vector2(0.945f, 0.15f), new Vector2(0.995f, 0.85f),
                new Color(0.25f, 0.25f, 0.25f, 1f), 13, true);
            _closeLabel = close.GetComponentInChildren<Text>(true);
            close.onClick.AddListener(Hide);

            // 标题栏拖拽移动窗口（Drag 事件沿层级向上传递，子控件不处理时会到这里）
            var trigger = top.gameObject.AddComponent<EventTrigger>();
            var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            drag.callback.AddListener(delegate (BaseEventData data)
            {
                var ped = data as PointerEventData;
                if (ped == null || _windowRt == null || _canvas == null) return;
                float scale = _canvas.scaleFactor;
                if (scale <= 0f) scale = 1f;
                _windowRt.anchoredPosition += ped.delta / scale;
            });
            trigger.triggers.Add(drag);
        }

        // 窗口尺寸钳制：下限 900×550（参考分辨率单位），上限为当前屏幕（拖拽时按 scaleFactor 换算）
        private static readonly Vector2 MinWindowSize = new Vector2(900f, 550f);

        // 右下角 24×24 拖拽手柄：拖动改变窗口 sizeDelta（与标题栏拖拽同一套 EventTrigger 方案）。
        // 窗口 pivot 居中：往右拖增宽、往下拖增高；三栏均为锚点比例布局，会随 sizeDelta 自适应。
        private void BuildResizeHandle(Transform parent)
        {
            var grip = UiWidgets.CreateRect(parent, "ResizeGrip",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 2f), new Vector2(-2f, 24f));
            var gripImg = grip.gameObject.AddComponent<Image>();
            gripImg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            var hint = UiWidgets.CreateText(grip, "Hint", "◢", 14, TextAnchor.LowerRight, Vector2.zero, new Vector2(-3f, 1f));
            hint.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            hint.raycastTarget = false;
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.verticalOverflow = VerticalWrapMode.Overflow;

            var trigger = grip.gameObject.AddComponent<EventTrigger>();
            var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            drag.callback.AddListener(delegate (BaseEventData data)
            {
                var ped = data as PointerEventData;
                if (ped == null || _windowRt == null || _canvas == null) return;
                float scale = _canvas.scaleFactor;
                if (scale <= 0f) scale = 1f;
                Vector2 delta = ped.delta / scale;
                Vector2 size = _windowRt.sizeDelta + new Vector2(delta.x, -delta.y);
                Vector2 maxSize = new Vector2(Screen.width / scale, Screen.height / scale);
                size.x = Mathf.Clamp(size.x, MinWindowSize.x, maxSize.x);
                size.y = Mathf.Clamp(size.y, MinWindowSize.y, maxSize.y);
                _windowRt.sizeDelta = size;
            });
            trigger.triggers.Add(drag);
        }

        private void BuildBody(RectTransform windowRt)
        {
            var body = UiWidgets.CreateRect(windowRt, "Body",
                Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -46f));

            // —— 左：分类列表（约 15%） ——
            var left = UiWidgets.CreateRect(body, "Left",
                new Vector2(0f, 0f), new Vector2(0.15f, 1f), Vector2.zero, new Vector2(-4f, 0f));
            var leftImg = left.gameObject.AddComponent<Image>();
            leftImg.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            UiWidgets.MakeScrollWithContent(left, out _catScroll, out _catContent, true);
            _catBtnTpl = UiWidgets.CreateFlatButtonTemplate(windowRt, "CatBtnTpl", 28f, CatNormal, true,
                new Color(0.38f, 0.38f, 0.38f, 1f), new Color(0.20f, 0.20f, 0.20f, 1f),
                14, "", new Vector2(8f, 0f), new Vector2(-6f, 0f), true);

            // —— 中：设置控件（约 50%）+ 底部说明栏 ——
            var center = UiWidgets.CreateRect(body, "Center",
                new Vector2(0.15f, 0f), new Vector2(0.65f, 1f), new Vector2(4f, 0f), new Vector2(-4f, 0f));
            var centerImg = center.gameObject.AddComponent<Image>();
            centerImg.color = new Color(0.12f, 0.12f, 0.12f, 1f);

            // 中栏顶部标题行：右侧“重置本板块”（二次确认，3 秒内再点才执行）
            var centerHead = UiWidgets.CreateRect(center, "CenterHeader",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(2f, -32f), new Vector2(-2f, -2f));
            var headImg = centerHead.gameObject.AddComponent<Image>();
            headImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);

            _currentCategoryTitle = UiWidgets.CreateText(centerHead, "CurrentCategory", "", 14,
                TextAnchor.MiddleCenter, new Vector2(10f, 0f), new Vector2(-114f, 0f));

            var catReset = UiWidgets.CreateButton(centerHead, "ResetCategory", ResetCategoryText,
                new Vector2(1f, 0.1f), new Vector2(1f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 12, false);
            var catResetRt = (RectTransform)catReset.transform;
            catResetRt.offsetMin = new Vector2(-104f, 0f);
            catResetRt.offsetMax = new Vector2(-6f, 0f);
            _resetCatLabel = catReset.GetComponentInChildren<Text>(true);
            catReset.onClick.AddListener(delegate
            {
                if (Time.unscaledTime - _resetCatArmedAt <= ConfirmWindowSec)
                {
                    _resetCatArmedAt = -999f;
                    if (_resetCatLabel != null) _resetCatLabel.text = ResetCategoryText;
                    ResetCurrentCategoryEntries();
                }
                else
                {
                    _resetCatArmedAt = Time.unscaledTime;
                    if (_resetCatLabel != null) _resetCatLabel.text = ResetCategoryConfirmText;
                }
            });

            var scrollWrap = UiWidgets.CreateRect(center, "ScrollWrap",
                Vector2.zero, Vector2.one, new Vector2(2f, 36f), new Vector2(-2f, -34f));
            UiWidgets.MakeScrollWithContent(scrollWrap, out _centerScroll, out _centerContent, true);

            var hintBar = UiWidgets.CreateRect(center, "HintBar",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 2f), new Vector2(-2f, 32f));
            var hintImg = hintBar.gameObject.AddComponent<Image>();
            hintImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            _hintText = UiWidgets.CreateText(hintBar, "Hint", DefaultHint, 12, TextAnchor.MiddleLeft,
                new Vector2(8f, 0f), new Vector2(-8f, 0f));
            _hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hintText.verticalOverflow = VerticalWrapMode.Truncate;

            // —— 右：实时预览（约 35%） ——
            var right = UiWidgets.CreateRect(body, "Right",
                new Vector2(0.65f, 0f), new Vector2(1f, 1f), new Vector2(4f, 0f), Vector2.zero);
            var rightImg = right.gameObject.AddComponent<Image>();
            rightImg.color = new Color(0.12f, 0.12f, 0.12f, 1f);

            var prevTitle = UiWidgets.CreateText(right, "PreviewTitle", I18n.Text("PreviewTitle", "预览"), 16, TextAnchor.MiddleLeft,
                new Vector2(10f, -32f), new Vector2(-10f, -2f));
            var prevTitleRt = prevTitle.rectTransform;
            prevTitleRt.anchorMin = new Vector2(0f, 1f);
            prevTitleRt.anchorMax = new Vector2(1f, 1f);

            // 实时预览：字幕/弹幕/3D气泡 三通道样例，随 Config.SettingChanged 实时刷新。
            // 根节点名必须固定为 SubtitlePreviewPane：FontReplace 模组按此祖先名跳过字体替换，勿改名。
            // RectMask2D 把样例裁剪在面板内，防止弹幕/长文本溢出面板与屏幕。
            var previewGo = new GameObject("SubtitlePreviewPane");
            previewGo.transform.SetParent(right, false);
            var previewRt = previewGo.AddComponent<RectTransform>();
            previewRt.anchorMin = Vector2.zero;
            previewRt.anchorMax = Vector2.one;
            previewRt.offsetMin = new Vector2(10f, 10f);
            previewRt.offsetMax = new Vector2(-10f, -40f);
            previewGo.AddComponent<RectMask2D>();
            previewGo.AddComponent<SettingsPreviewPanel>();
        }
    }
}
