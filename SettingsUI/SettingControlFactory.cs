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
    /// 按 ConfigEntry 的 SettingType 自动生成对应控件。
    /// 所有控件只读写 entry.Value（BoxedValue），SettingChanged → 运行期刷新走现有链路。
    /// </summary>
    // partial：*FontFamilyCsv 字体列表编辑器在 SettingControlFactory.FontListEditor.cs
    internal static partial class SettingControlFactory
    {
        private static readonly Color RowBg = new Color(1f, 1f, 1f, 0.02f);
        private static readonly Color BtnNormal = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color BtnOn = new Color(0.20f, 0.45f, 0.20f, 1f);
        private static readonly Color SliderBg = new Color(0.30f, 0.30f, 0.30f, 1f);
        private static readonly Color SliderHandle = new Color(0.60f, 0.60f, 0.60f, 1f);
        private static readonly Color InputBg = new Color(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color HintGray = new Color(0.65f, 0.65f, 0.65f, 1f);

        // 键名里冗余的频道前缀，显示时剥掉（仅外观，不改实际 Key）
        private static readonly string[] s_ChannelPrefixes = { "字幕 ", "弹幕 ", "3D气泡 ", "主播模式 " };

        // 各 section 名与 Setting.cs 中 private const 保持一致（字面量副本）
        private const string GeneralSectionName = "1. 通用";
        private const string SubtitleAdvancedSectionName = "2.1 字幕 - 进阶";
        private const string DanmakuAdvancedSectionName = "3.1 弹幕 - 进阶";
        private const string World3DAdvancedSectionName = "4.1 3D气泡 - 进阶";

        // ---------- 过滤 ----------

        internal static bool ShouldSkip(ConfigEntryBase entry)
        {
            if (entry == null || entry.Definition == null) return true;
            // 带自绘控件（CustomDrawer）的条目是 IMGUI 专用，其功能在 GUI 里已有等价特殊行，不再生成普通控件：
            // TextPresetName（预设选择行替代）/ SettingsWindowButton（仅入口）/ 台词面板与两个测试按钮 /
            // 三个 Show*Options 折叠开关（机制已停用）/ 三个 字体资源包选择器。
            // 注意："99. 测试" 调试区不再整体跳过，作为普通分类进 GUI（显示名见 SettingsWindow.FormatSectionName）。
            var attrs = GetCmAttributes(entry);
            if (attrs != null && attrs.CustomDrawer != null) return true;
            // 界面语言由 BuildSpecialRows 的专用语言选择行渲染（实时扫描 locales 目录、显示本地语言名），
            // 普通列表里不再生成泛型字符串控件
            if (ReferenceEquals(entry, Settings.UiLanguage)) return true;
            return false;
        }

        internal static ConfigurationManagerAttributes GetCmAttributes(ConfigEntryBase entry)
        {
            var tags = entry != null && entry.Description != null ? entry.Description.Tags : null;
            if (tags == null) return null;
            for (int i = 0; i < tags.Length; i++)
            {
                var attrs = tags[i] as ConfigurationManagerAttributes;
                if (attrs != null) return attrs;
            }
            return null;
        }

        internal static string FormatEntryLabel(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            for (int i = 0; i < s_ChannelPrefixes.Length; i++)
            {
                var p = s_ChannelPrefixes[i];
                if (key.StartsWith(p, StringComparison.Ordinal) && key.Length > p.Length)
                    return key.Substring(p.Length);
            }
            return key;
        }

        // 子选项缩进：Config 侧在 ConfigurationManagerAttributes.Indent 标记从属级别（0=普通项），
        // 这里按级别给整行/整块左侧加布局留白（不动标签文本 —— 标签在 I18n 等处也按原文查找）
        private const int IndentPxPerLevel = 24;

        private static int GetIndentLevel(ConfigEntryBase entry)
        {
            var attrs = GetCmAttributes(entry);
            return attrs != null && attrs.Indent > 0 ? attrs.Indent : 0;
        }

        // ---------- 入口 ----------

        // requestRebuild：仅“子选项组的父开关”行传入 —— 父 bool 被切换/重置后子项组要刷新置灰状态，
        // 由调用方（SettingsWindow）重建中栏；普通行保持 null，行为不变
        internal static void Build(ConfigEntryBase entry, RectTransform parent, Action<string> setHint, Action requestRebuild = null)
        {
            // 显示名/悬停说明走 I18n；语言表缺失时回落到 剥频道前缀的键 / ConfigDescription 原文
            string key = entry.Definition.Key;
            string label = I18n.SettingName(key, FormatEntryLabel(key));
            string tooltip = I18n.SettingDesc(key, entry.Description != null ? entry.Description.Description : null);
            var type = entry.SettingType;

            if (type == typeof(bool))
            {
                BuildBool(entry, parent, label, tooltip, setHint, requestRebuild);
            }
            else if (type == typeof(Color))
            {
                BuildColor(entry, parent, label, tooltip, setHint);
            }
            else if (type.IsEnum)
            {
                BuildEnumCycle(entry, parent, label, tooltip, setHint);
            }
            else if (type == typeof(KeyboardShortcut))
            {
                BuildShortcut(entry, parent, label, tooltip, setHint);
            }
            else if (type == typeof(string))
            {
                // 三个 *FontFamilyCsv 条目走专用字体列表编辑器，其余字符串仍用普通输入框
                if (IsFontFamilyCsvEntry(entry))
                {
                    BuildFontFamilyEditor(entry, parent, label, tooltip, setHint);
                }
                else
                {
                    var list = GetAcceptableList(entry);
                    if (list != null) BuildListCycle(entry, parent, label, tooltip, setHint, list);
                    else BuildStringInput(entry, parent, label, tooltip, setHint);
                }
            }
            else if (type == typeof(int) || type == typeof(float))
            {
                var list = GetAcceptableList(entry);
                if (list != null) BuildListCycle(entry, parent, label, tooltip, setHint, list);
                else BuildNumber(entry, parent, label, tooltip, setHint);
            }
            else
            {
                BuildUnsupported(entry, parent, label, tooltip, setHint);
            }
        }

        // ---------- 子选项组（Indent 折叠） ----------

        // 折叠状态跨重建保留：键 = 父条目 Definition.Key，值 = 是否已折叠（缺省展开）
        private static readonly Dictionary<string, bool> s_GroupCollapsed = new Dictionary<string, bool>(StringComparer.Ordinal);

        // 按显示顺序构建整个分类的普通条目，并把连续的 Indent>=1 条目归到最近一个 bool 开关父项下：
        // - 子项始终构建（默认展开可见），父行最左侧始终带 ▼/▶ 折叠按钮；
        // - 父开关为 false：子项整组置灰不可编辑（CanvasGroup 半透明 + 禁交互），开启后恢复正常；
        // - 孤儿（缩进条目上方最近的非缩进项不是 bool 开关，如 4.1 进阶的“朝向更新间隔”——
        //   它真正的父项“朝向玩家”在 4.0 分类）：不建组、不置灰，按原样缩进渲染。
        // requestRebuild：父开关被切换/重置后由 SettingsWindow 重建中栏（保留滚动位置），
        // 子项置灰状态在重建构建时按父开关当前值统一应用，无需额外的实时联动。
        internal static void BuildGrouped(IList<ConfigEntryBase> entries, RectTransform parent,
            Action<string> setHint, Action requestRebuild)
        {
            if (entries == null || parent == null) return;

            // 第一遍：识别分组。父项必须是 bool 开关 —— 当前配置里所有合法父项都是启用类开关；
            // 非 bool 的非缩进项（数值/枚举等）不构成父项，其后的缩进条目按孤儿处理
            var childrenOf = new Dictionary<int, List<int>>();
            int lastHead = -1;  // 最近一个 Indent==0 条目下标
            int openGroup = -1; // 当前已开组的父项下标（-1 = 不在组内）
            for (int i = 0; i < entries.Count; i++)
            {
                if (GetIndentLevel(entries[i]) <= 0)
                {
                    lastHead = i;
                    openGroup = -1;
                    continue;
                }
                if (openGroup >= 0)
                {
                    childrenOf[openGroup].Add(i); // 组已开：后续缩进条目延续本组
                    continue;
                }
                if (lastHead >= 0 && entries[lastHead].SettingType == typeof(bool))
                {
                    var list = new List<int> { i };
                    childrenOf.Add(lastHead, list);
                    openGroup = lastHead;
                }
                // 否则：孤儿，不建组
            }

            // 第二遍：按组构建
            for (int i = 0; i < entries.Count; i++)
            {
                List<int> kids;
                if (!childrenOf.TryGetValue(i, out kids))
                {
                    Build(entries[i], parent, setHint);
                    continue;
                }

                var head = entries[i];
                string key = head.Definition.Key;
                bool collapsed;
                if (!s_GroupCollapsed.TryGetValue(key, out collapsed)) collapsed = false;

                // 父行：bool 开关，切换/重置后需整栏重建以刷新子项组的置灰状态
                Build(head, parent, setHint, requestRebuild);
                var headRow = parent.GetChild(parent.childCount - 1);

                // 子行：始终构建；先按父开关当前值置灰/恢复，再按折叠状态 SetActive 显隐
                // （两条轴互不干扰：折叠只动 active，置灰挂在 CanvasGroup 上，重建后两者都按最新状态应用）
                var childRows = new List<GameObject>(kids.Count);
                for (int k = 0; k < kids.Count; k++)
                {
                    int before = parent.childCount;
                    Build(entries[kids[k]], parent, setHint);
                    for (int c = before; c < parent.childCount; c++)
                        childRows.Add(parent.GetChild(c).gameObject);
                }
                i = kids[kids.Count - 1];

                ApplyGroupGrayState(childRows, IsGroupOn(head));
                AddGroupCollapseButton(headRow, key, collapsed, childRows);
            }
        }

        // 父项“功能是否开启”：bool 开关取当前值；万一出现非 bool 父项，视为常开（不置灰）
        private static bool IsGroupOn(ConfigEntryBase head)
        {
            var v = head.BoxedValue;
            return !(v is bool) || (bool)v;
        }

        // 父开关关闭时子项整组置灰：半透明 + 不可交互 + 不接收射线（悬停提示也随之失效，属预期：
        // 功能已关闭，子项不可改也不需要说明）；父开关开启后完全恢复。
        // 折叠按钮在父行上，不在 childRows 里，永远不会被置灰。
        private const float GroupDisabledAlpha = 0.4f;

        private static void ApplyGroupGrayState(List<GameObject> childRows, bool groupOn)
        {
            for (int c = 0; c < childRows.Count; c++)
            {
                var go = childRows[c];
                if (go == null) continue;
                var cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = groupOn ? 1f : GroupDisabledAlpha;
                cg.interactable = groupOn;
                cg.blocksRaycasts = groupOn;
            }
        }

        // 父行最左侧的 ▼/▶ 折叠按钮：作为行的 HorizontalLayoutGroup 第一个子节点插入，
        // 布局组自动把行内容右移腾出位置；点击只做 SetActive 显隐（不触发整栏重建，滚动位置不动）
        private static void AddGroupCollapseButton(Transform headRow, string key, bool collapsed, List<GameObject> childRows)
        {
            var btn = CreateLayoutButton(headRow as RectTransform, "GroupFold", collapsed ? "▶" : "▼", 22f);
            btn.transform.SetSiblingIndex(0);
            // 弱化底色与字号，避免抢“开/关”主按钮的视觉
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = new Color(0.18f, 0.18f, 0.18f, 1f);
            var txt = btn.GetComponentInChildren<Text>(true);
            if (txt != null) txt.fontSize = 11;

            for (int c = 0; c < childRows.Count; c++)
                if (childRows[c] != null) childRows[c].SetActive(!collapsed);

            btn.onClick.AddListener(delegate
            {
                bool now;
                if (!s_GroupCollapsed.TryGetValue(key, out now)) now = false;
                now = !now;
                s_GroupCollapsed[key] = now;
                for (int c = 0; c < childRows.Count; c++)
                    if (childRows[c] != null) childRows[c].SetActive(!now);
                if (txt != null) txt.text = now ? "▶" : "▼";
            });
        }

        // ---------- 特殊行（原 F12 CustomDrawer 功能的 GUI 等价物） ----------

        // 构建指定分类顶部的特殊行（在自动生成的控件之前）。
        // requestRebuild：应用预设后大量 entry 被批量改写，由调用方（SettingsWindow）重建中栏刷新所有控件。
        internal static void BuildSpecialRows(string section, RectTransform parent, Action<string> setHint, Action requestRebuild)
        {
            if (string.IsNullOrEmpty(section) || parent == null) return;
            switch (section)
            {
                case GeneralSectionName:
                    BuildLanguageRow(parent, setHint);
                    BuildPresetPickerRow(parent, setHint, requestRebuild);
                    BuildSavePresetRow(parent, setHint, requestRebuild);
                    BuildActionRow(parent, I18n.Text("PhraseFilterPanel.Label", "台词显示控制面板"),
                        I18n.Text("PhraseFilterPanel.Button", "打开面板"),
                        I18n.Text("PhraseFilterPanel.Tooltip", "打开台词显示控制面板，用于选择声线/触发器/NetId 的显示规则。"), setHint,
                        delegate { try { PhraseFilterPanel.ToggleVisible(); } catch { } });
                    BuildActionRow(parent, I18n.Text("TestSubtitle.Label", "随机测试字幕"),
                        I18n.Text("TestSubtitle.Button", "▶ 发送"),
                        I18n.Text("TestSubtitle.Tooltip", "随机发送一条测试字幕（任意场景可用）。"), setHint, Settings.SendRandomTestSubtitle);
                    BuildActionRow(parent, I18n.Text("TestDanmaku.Label", "随机测试弹幕"),
                        I18n.Text("TestDanmaku.Button", "▶ 发送 3 条"),
                        I18n.Text("TestDanmaku.Tooltip", "随机发送 3 条测试弹幕（任意场景可用）。"), setHint, Settings.SendRandomTestDanmaku);
                    break;
                case SubtitleAdvancedSectionName:
                    BuildFontBundleRow(parent, Settings.SubtitleFontBundleName, "字幕 字体资源包", setHint);
                    break;
                case DanmakuAdvancedSectionName:
                    BuildFontBundleRow(parent, Settings.DanmakuFontBundleName, "弹幕 字体资源包", setHint);
                    break;
                case World3DAdvancedSectionName:
                    BuildFontBundleRow(parent, Settings.World3DFontBundleName, "3D气泡 字体资源包", setHint);
                    break;
            }
        }

        // 单按钮操作行（台词面板 / 测试按钮）
        private static void BuildActionRow(RectTransform parent, string label, string buttonText, string tooltip,
            Action<string> setHint, Action onClick)
        {
            RectTransform area;
            CreateRow(parent, label, tooltip, 30f, setHint, out area); // 特殊行不缩进
            var btn = CreateLayoutButton(area, "Action", buttonText, 120f);
            if (onClick != null) btn.onClick.AddListener(delegate { onClick(); });
        }

        // 语言的本地名称（native name，非翻译文本，故写死在代码里）：已知代码映射，未知目录显示原始目录名
        private static string LanguageDisplayName(string code)
        {
            switch (code)
            {
                case "ch": return "简体中文";
                case "en": return "English";
                case "ru": return "Русский";
                case "ja": return "日本語";
                case "ko": return "한국어";
                case "de": return "Deutsch";
                case "fr": return "Français";
                case "es": return "Español";
                default: return code;
            }
        }

        // 语言选择行：◀ 本地语言名 ▶，每次构建时实时扫描 locales 根目录（ScanLanguages 保证 ch 置顶），
        // 新增 locales/xx/ 目录后下次打开窗口即出现在可选项里。
        // 切换写 UiLanguage.Value → SettingChanged 钩子（I18n.Reload + SettingsWindow.RebuildAll）整体重建界面。
        private static void BuildLanguageRow(RectTransform parent, Action<string> setHint)
        {
            var entry = Settings.UiLanguage;
            if (entry == null) return;
            RectTransform area;
            CreateRow(parent, I18n.SettingName(entry.Definition.Key, "界面 语言"),
                I18n.SettingDesc(entry.Definition.Key, entry.Description != null ? entry.Description.Description : null),
                30f, setHint, out area);

            // 实时扫描（不缓存）：行重建时（打开窗口/切分类/切语言）拿到最新目录列表
            var langs = new List<string>(I18n.ScanLanguages());

            var prev = CreateLayoutButton(area, "Prev", "◀", 28f);
            var nameLabel = AddAreaText(area, 120f);
            var next = CreateLayoutButton(area, "Next", "▶", 28f);

            Action refreshLabel = delegate
            {
                string cur = entry.Value;
                if (string.IsNullOrEmpty(cur))
                {
                    nameLabel.text = LanguageDisplayName(I18n.DefaultLanguage);
                }
                else if (langs.Contains(cur))
                {
                    nameLabel.text = LanguageDisplayName(cur);
                }
                else
                {
                    // 保存的语言目录已被删除：显示原始保存值（I18n 加载时本身也会回落到内置中文），
                    // 点一下 ◀/▶ 即可切回列表内的语言
                    nameLabel.text = cur;
                }
            };
            refreshLabel();

            Action<int> step = delegate (int dir)
            {
                if (langs.Count == 0) return;
                int idx = langs.IndexOf(entry.Value);
                // 保存值不在扫描结果里（目录已删）：第一下先回到 ch
                idx = idx < 0 ? 0 : (idx + dir + langs.Count) % langs.Count;
                entry.Value = langs[idx]; // 触发 SettingChanged → 整体重建（本行随之以新语言重建）
                refreshLabel();
            };
            prev.onClick.AddListener(delegate { step(-1); });
            next.onClick.AddListener(delegate { step(1); });
        }

        // 预设选择行：◀ 名称 ▶ + 刷新 + 应用；显示始终回同步到 TextPresetName.Value
        private static void BuildPresetPickerRow(RectTransform parent, Action<string> setHint, Action requestRebuild)
        {
            RectTransform area;
            CreateRow(parent, I18n.Text("PresetPicker.Label", "文本样式预设"),
                I18n.Text("PresetPicker.Tooltip", "从 presets 文件夹读取所有 .jsonc 预设文件。点击“应用”后，会将预设中所有包含选项一次性导入本配置。"),
                30f, setHint, out area);

            Settings.SyncPresetSelectionToCurrent();

            var prev = CreateLayoutButton(area, "Prev", "◀", 28f);
            var nameLabel = AddAreaText(area, 0f);
            var next = CreateLayoutButton(area, "Next", "▶", 28f);
            var refresh = CreateLayoutButton(area, "Refresh", I18n.Text("BtnRefresh", "刷新"), 56f);
            var apply = CreateLayoutButton(area, "Apply", I18n.Text("BtnApply", "应用"), 56f);

            Action refreshLabel = delegate
            {
                var list = Settings.GetPresetNames();
                int idx = Settings.GetSelectedPresetIndex();
                nameLabel.text = (list != null && list.Count > 0 && idx >= 0 && idx < list.Count)
                    ? list[idx] : I18n.Text("PresetPicker.Empty", "(无预设)");
            };
            refreshLabel();

            prev.onClick.AddListener(delegate
            {
                Settings.SetSelectedPresetIndex(Settings.GetSelectedPresetIndex() - 1);
                refreshLabel();
            });
            next.onClick.AddListener(delegate
            {
                Settings.SetSelectedPresetIndex(Settings.GetSelectedPresetIndex() + 1);
                refreshLabel();
            });
            refresh.onClick.AddListener(delegate
            {
                Settings.RefreshPresetList();
                refreshLabel();
            });
            apply.onClick.AddListener(delegate
            {
                var pick = Settings.ApplySelectedPreset();
                if (pick == null) return;
                // 预设批量改写了大量 entry：重建当前分类，让中栏已建控件重新读取最新值
                if (requestRebuild != null) requestRebuild();
            });
        }

        // 保存预设行：预设名输入框 + “保存为预设”按钮（旧抽屉 保存预设/确定 流程的 GUI 等价物）
        private static void BuildSavePresetRow(RectTransform parent, Action<string> setHint, Action requestRebuild)
        {
            RectTransform area;
            CreateRow(parent, I18n.Text("SavePreset.Label", "保存当前为预设"),
                I18n.Text("SavePreset.Tooltip", "把当前所有设置保存为 presets 文件夹下的 .jsonc 预设文件（同名覆盖）。保存成功后上面的预设选择器即可选到新预设。"),
                30f, setHint, out area);

            // 输入框（样式与 BuildStringInput 一致）
            var go = new GameObject("Input");
            go.transform.SetParent(area, false);
            var rt = go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 24f;
            var img = go.AddComponent<Image>();
            img.color = InputBg;

            var input = go.AddComponent<InputField>();
            var text = CreateFullChildText(rt, "Text", 13, Color.white);
            text.supportRichText = false;
            var ph = CreateFullChildText(rt, "Placeholder", 13, HintGray);
            ph.text = I18n.Text("SavePreset.Placeholder", "新预设名…");
            input.textComponent = text;
            input.placeholder = ph;

            // 初始建议名与旧抽屉一致：预设选择器当前选中的名字
            var names = Settings.GetPresetNames();
            int sel = Settings.GetSelectedPresetIndex();
            if (names != null && names.Count > 0 && sel >= 0 && sel < names.Count)
                input.SetTextWithoutNotify(names[sel]);

            var save = CreateLayoutButton(area, "Save", I18n.Text("SavePreset.Button", "保存为预设"), 84f);
            save.onClick.AddListener(delegate
            {
                var name = input.text != null ? input.text.Trim() : "";
                if (string.IsNullOrEmpty(name))
                {
                    if (setHint != null) setHint(I18n.Text("SavePreset.HintEmpty", "请输入预设名。"));
                    return;
                }
                var saved = Settings.SavePresetAs(name);
                if (saved == null)
                {
                    if (setHint != null) setHint(I18n.Text("SavePreset.HintFail", "保存失败，请查看日志。"));
                    return;
                }
                // 成功后列表已重扫：重建中栏，预设选择行立即包含新预设
                // （选择回同步到当前 cfg 值，不自动选中新预设 —— 与旧抽屉行为一致）
                if (requestRebuild != null) requestRebuild();
            });
        }

        // 字体包选择行：< 名称 > + 刷新 + 应用（三个渠道各一行，选择索引按 entry 分别记忆）
        private static void BuildFontBundleRow(RectTransform parent, ConfigEntry<string> entry, string label, Action<string> setHint)
        {
            if (entry == null) return;
            RectTransform area;
            // 行标题即该隐藏条目的配置键显示名（走 I18n.SettingName，与 GUI 普通行一致）
            CreateRow(parent, I18n.SettingName(entry.Definition.Key, label),
                I18n.Text("FontBundle.Tooltip", "从 BepInEx\\plugins\\FontReplace\\Font 选择字体资源包（不覆盖则留空）。"), 30f, setHint, out area);

            Settings.GetFontBundleNames(entry); // 确保已扫描

            var prev = CreateLayoutButton(area, "Prev", "<", 28f);
            var nameLabel = AddAreaText(area, 0f);
            var next = CreateLayoutButton(area, "Next", ">", 28f);
            var refresh = CreateLayoutButton(area, "Refresh", I18n.Text("BtnRefresh", "刷新"), 56f);
            var apply = CreateLayoutButton(area, "Apply", I18n.Text("BtnApply", "应用"), 56f);

            Action refreshLabel = delegate
            {
                var list = Settings.GetFontBundleNames(entry);
                int idx = Settings.GetFontBundleSelection(entry, entry.Value);
                nameLabel.text = (list != null && list.Count > 0 && idx >= 0 && idx < list.Count)
                    ? Settings.FormatFontBundleLabel(list[idx]) : I18n.Text("FontBundle.Empty", "(无字体)");
            };
            refreshLabel();

            prev.onClick.AddListener(delegate
            {
                var list = Settings.GetFontBundleNames(entry);
                int count = list != null ? list.Count : 0;
                int idx = Settings.GetFontBundleSelection(entry, entry.Value);
                if (count > 0) idx = (idx - 1 + count) % count;
                Settings.SetFontBundleSelection(entry, idx, entry.Value);
                refreshLabel();
            });
            next.onClick.AddListener(delegate
            {
                var list = Settings.GetFontBundleNames(entry);
                int count = list != null ? list.Count : 0;
                int idx = Settings.GetFontBundleSelection(entry, entry.Value);
                if (count > 0) idx = (idx + 1) % count;
                Settings.SetFontBundleSelection(entry, idx, entry.Value);
                refreshLabel();
            });
            refresh.onClick.AddListener(delegate
            {
                Settings.RefreshFontBundles(entry);
                refreshLabel();
            });
            apply.onClick.AddListener(delegate
            {
                // 应用会把选择写入 entry.Value 并触发对应渠道的运行期样式刷新；只改隐藏条目，无需整栏重建
                Settings.ApplyFontBundleSelection(entry);
                refreshLabel();
            });
        }

        // ---------- 各类型控件 ----------

        private static void BuildBool(ConfigEntryBase entry, RectTransform parent, string label, string tooltip,
            Action<string> setHint, Action requestRebuild = null)
        {
            RectTransform area;
            CreateRow(parent, label, tooltip, 30f, setHint, out area, GetIndentLevel(entry));

            var btn = CreateLayoutButton(area, "Toggle", "", 72f);
            var txt = btn.GetComponentInChildren<Text>(true);
            var img = btn.GetComponent<Image>();

            Action refresh = delegate
            {
                bool v = entry.BoxedValue is bool && (bool)entry.BoxedValue;
                txt.text = v ? I18n.Text("ToggleOn", "开") : I18n.Text("ToggleOff", "关");
                img.color = v ? BtnOn : BtnNormal;
            };
            refresh();
            btn.onClick.AddListener(delegate
            {
                bool cur = entry.BoxedValue is bool && (bool)entry.BoxedValue;
                entry.BoxedValue = !cur;
                refresh();
                // 组父开关：子项组的置灰状态随开关变化 → 重建中栏（普通行此回调为空，无影响）
                if (requestRebuild != null) requestRebuild();
            });
            // 单项重置也可能翻转父开关的 bool 值（如默认开启被关后再重置），同样触发重建刷新置灰
            AddResetButton(area, entry, delegate
            {
                refresh();
                if (requestRebuild != null) requestRebuild();
            });
        }

        private static void BuildNumber(ConfigEntryBase entry, RectTransform parent, string label, string tooltip, Action<string> setHint)
        {
            bool isInt = entry.SettingType == typeof(int);
            float min, max;

            if (TryGetRange(entry, isInt, out min, out max))
            {
                // 有范围 → 滑条 + 数值标签（拖动时实时写入）
                RectTransform area;
                CreateRow(parent, label, tooltip, 30f, setHint, out area, GetIndentLevel(entry));

                bool percent = IsShowAsPercent(entry);
                Text valueLabel;
                var slider = CreateSlider(area, min, max, isInt, out valueLabel);

                float cur = GetNumber(entry, isInt);
                slider.SetValueWithoutNotify(Mathf.Clamp(cur, min, max));
                UpdateNumberLabel(valueLabel, cur, isInt, percent, min, max);

                slider.onValueChanged.AddListener(delegate (float v)
                {
                    if (isInt) entry.BoxedValue = (int)v;
                    else entry.BoxedValue = v;
                    UpdateNumberLabel(valueLabel, v, isInt, percent, min, max);
                });

                // 重置：从 entry 重读默认值，滑条与数值标签一并回同步
                AddResetButton(area, entry, delegate
                {
                    float dv = GetNumber(entry, isInt);
                    slider.SetValueWithoutNotify(Mathf.Clamp(dv, min, max));
                    UpdateNumberLabel(valueLabel, dv, isInt, percent, min, max);
                });
            }
            else
            {
                // 无范围 → − / + 步进
                RectTransform area;
                CreateRow(parent, label, tooltip, 30f, setHint, out area, GetIndentLevel(entry));

                var minus = CreateLayoutButton(area, "Minus", "-", 30f);
                var valueLabel = AddAreaText(area, 80f);
                var plus = CreateLayoutButton(area, "Plus", "+", 30f);

                float step = isInt ? 1f : 0.1f;
                Action refresh = delegate
                {
                    UpdateNumberLabel(valueLabel, GetNumber(entry, isInt), isInt, false, 0f, 0f);
                };
                refresh();
                minus.onClick.AddListener(delegate { StepNumber(entry, isInt, -step); refresh(); });
                plus.onClick.AddListener(delegate { StepNumber(entry, isInt, step); refresh(); });
                AddResetButton(area, entry, refresh);
            }
        }

        private static void BuildEnumCycle(ConfigEntryBase entry, RectTransform parent, string label, string tooltip, Action<string> setHint)
        {
            RectTransform area;
            CreateRow(parent, label, tooltip, 30f, setHint, out area, GetIndentLevel(entry));

            var values = Enum.GetValues(entry.SettingType);
            var btn = CreateLayoutButton(area, "Cycle", "", 160f);
            var txt = btn.GetComponentInChildren<Text>(true);

            Action refresh = delegate
            {
                var cur = entry.BoxedValue;
                // 枚举值显示名走 I18n（Enum.<类型名>.<值名>），语言表缺失时回落到原始枚举名
                txt.text = cur != null
                    ? I18n.Text("Enum." + entry.SettingType.Name + "." + cur, cur.ToString())
                    : "?";
            };
            refresh();
            btn.onClick.AddListener(delegate
            {
                // 点击切到下一个枚举值（循环）
                var cur = entry.BoxedValue;
                int idx = -1;
                for (int i = 0; i < values.Length; i++)
                {
                    if (Equals(values.GetValue(i), cur)) { idx = i; break; }
                }
                idx = (idx + 1) % values.Length;
                entry.BoxedValue = values.GetValue(idx);
                refresh();
            });
            AddResetButton(area, entry, refresh);
        }

        private static void BuildListCycle(ConfigEntryBase entry, RectTransform parent, string label, string tooltip,
            Action<string> setHint, object[] values)
        {
            RectTransform area;
            CreateRow(parent, label, tooltip, 30f, setHint, out area, GetIndentLevel(entry));

            var btn = CreateLayoutButton(area, "Cycle", "", 160f);
            var txt = btn.GetComponentInChildren<Text>(true);

            Action refresh = delegate
            {
                var cur = entry.BoxedValue;
                txt.text = cur != null ? cur.ToString() : "?";
            };
            refresh();
            btn.onClick.AddListener(delegate
            {
                // 点击在可选项列表里循环
                var cur = entry.BoxedValue;
                int idx = -1;
                for (int i = 0; i < values.Length; i++)
                {
                    if (Equals(values[i], cur)) { idx = i; break; }
                }
                idx = (idx + 1) % values.Length;
                entry.BoxedValue = values[idx];
                refresh();
            });
            AddResetButton(area, entry, refresh);
        }

        private static void BuildStringInput(ConfigEntryBase entry, RectTransform parent, string label, string tooltip, Action<string> setHint)
        {
            RectTransform area;
            CreateRow(parent, label, tooltip, 30f, setHint, out area, GetIndentLevel(entry));

            var go = new GameObject("Input");
            go.transform.SetParent(area, false);
            var rt = go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 24f;
            var img = go.AddComponent<Image>();
            img.color = InputBg;

            var input = go.AddComponent<InputField>();

            var text = CreateFullChildText(rt, "Text", 13, Color.white);
            text.supportRichText = false;
            var ph = CreateFullChildText(rt, "Placeholder", 13, HintGray);
            ph.text = I18n.Text("InputPlaceholder", "输入文本…");

            input.textComponent = text;
            input.placeholder = ph;
            input.SetTextWithoutNotify(entry.BoxedValue as string ?? string.Empty);
            input.onEndEdit.AddListener(delegate (string v) { entry.BoxedValue = v; });

            AddResetButton(area, entry, delegate
            {
                input.SetTextWithoutNotify(entry.BoxedValue as string ?? string.Empty);
            });
        }

        private static void BuildColor(ConfigEntryBase entry, RectTransform parent, string label, string tooltip, Action<string> setHint)
        {
            // 颜色块：标题行（名称 + 色板）+ 4 行 RGBA 滑条
            var blockGo = new GameObject("ColorBlock");
            blockGo.transform.SetParent(parent, false);
            var blockRt = blockGo.AddComponent<RectTransform>();
            var le = blockGo.AddComponent<LayoutElement>();
            le.preferredHeight = 26f + 4f * 24f + 4f * 2f + 4f;
            le.minHeight = le.preferredHeight;
            var bg = blockGo.AddComponent<Image>();
            bg.color = RowBg;

            var v = blockGo.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.spacing = 2f;
            v.padding = new RectOffset(8 + GetIndentLevel(entry) * IndentPxPerLevel, 8, 2, 2);

            // 标题行
            var head = CreateInnerRow(blockRt, 26f);
            CreateLayoutLabel(head, label, 0f, 14, TextAnchor.MiddleLeft, Color.white);

            var swGo = new GameObject("Swatch");
            swGo.transform.SetParent(head, false);
            swGo.AddComponent<RectTransform>();
            var swLe = swGo.AddComponent<LayoutElement>();
            swLe.preferredWidth = 48f;
            swLe.minWidth = 48f;
            swLe.preferredHeight = 18f;
            var swatch = swGo.AddComponent<Image>();
            swatch.color = GetColor(entry);

            AttachHint(blockGo, tooltip, setHint);

            string[] names = { "R", "G", "B", "A" };
            var channelSliders = new List<Slider>(4);
            var channelLabels = new List<Text>(4);
            for (int ch = 0; ch < 4; ch++)
            {
                var row = CreateInnerRow(blockRt, 24f);
                CreateLayoutLabel(row, names[ch], 20f, 12, TextAnchor.MiddleCenter, HintGray);

                Text valueLabel;
                var slider = CreateSlider(row, 0f, 1f, false, out valueLabel);
                channelSliders.Add(slider);
                channelLabels.Add(valueLabel);

                int captured = ch;
                var cur = GetColor(entry);
                slider.SetValueWithoutNotify(GetChannel(cur, captured));
                valueLabel.text = GetChannel(cur, captured).ToString("0.00");

                slider.onValueChanged.AddListener(delegate (float val)
                {
                    var c = SetChannel(GetColor(entry), captured, val);
                    entry.BoxedValue = c;
                    swatch.color = c;
                    valueLabel.text = val.ToString("0.00");
                });
            }

            // 整块重置（标题行右端）：色板 + 4 条 RGBA 滑条一并回同步到默认值
            AddResetButton(head, entry, delegate
            {
                var dc = GetColor(entry);
                swatch.color = dc;
                for (int ch = 0; ch < 4; ch++)
                {
                    float cv = GetChannel(dc, ch);
                    channelSliders[ch].SetValueWithoutNotify(cv);
                    channelLabels[ch].text = cv.ToString("0.00");
                }
            });
        }

        private static void BuildShortcut(ConfigEntryBase entry, RectTransform parent, string label, string tooltip, Action<string> setHint)
        {
            RectTransform area;
            CreateRow(parent, label, tooltip, 30f, setHint, out area, GetIndentLevel(entry));

            // 第一阶段不在界面内捕获按键改绑，只显示当前绑定；改键仍走 F12
            var txt = AddAreaText(area, 160f);
            txt.color = HintGray;
            Action refresh = delegate
            {
                var v = entry.BoxedValue;
                txt.text = v != null ? v.ToString() : "?";
            };
            refresh();
            AddResetButton(area, entry, refresh);
        }

        private static void BuildUnsupported(ConfigEntryBase entry, RectTransform parent, string label, string tooltip, Action<string> setHint)
        {
            RectTransform area;
            CreateRow(parent, label, tooltip, 30f, setHint, out area, GetIndentLevel(entry));
            var txt = AddAreaText(area, 160f);
            txt.text = I18n.Text("Unsupported", "暂不支持在GUI编辑");
            txt.color = HintGray;
        }

        // ---------- 单项重置 ----------

        // 行最右端 40px 小按钮：把该 entry 恢复默认值（BoxedValue=DefaultValue 触发 SettingChanged → 既有刷新链路），
        // 再调用 refresh 让本行控件立即显示默认值。单项重置无需确认。
        private static void AddResetButton(RectTransform area, ConfigEntryBase entry, Action refresh)
        {
            var btn = CreateLayoutButton(area, "Reset", I18n.Text("Reset.Option", "重置"), 40f);
            btn.onClick.AddListener(delegate
            {
                Settings.ResetEntryToDefault(entry);
                if (refresh != null) refresh();
            });
        }

        // ---------- 数值辅助 ----------

        private static float GetNumber(ConfigEntryBase entry, bool isInt)
        {
            var v = entry.BoxedValue;
            if (v == null) return 0f;
            return isInt ? (int)v : (float)v;
        }

        private static void StepNumber(ConfigEntryBase entry, bool isInt, float delta)
        {
            if (isInt) entry.BoxedValue = (int)entry.BoxedValue + (int)delta;
            else entry.BoxedValue = (float)entry.BoxedValue + delta;
        }

        private static bool TryGetRange(ConfigEntryBase entry, bool isInt, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            var av = entry.Description != null ? entry.Description.AcceptableValues : null;
            if (av == null) return false;

            if (isInt)
            {
                var r = av as AcceptableValueRange<int>;
                if (r == null) return false;
                min = r.MinValue;
                max = r.MaxValue;
            }
            else
            {
                var r = av as AcceptableValueRange<float>;
                if (r == null) return false;
                min = r.MinValue;
                max = r.MaxValue;
            }
            return max > min;
        }

        private static bool IsShowAsPercent(ConfigEntryBase entry)
        {
            var attrs = GetCmAttributes(entry);
            return attrs != null && attrs.ShowRangeAsPercent == true;
        }

        private static void UpdateNumberLabel(Text label, float v, bool isInt, bool percent, float min, float max)
        {
            if (label == null) return;
            if (percent && max > min)
                label.text = ((v - min) / (max - min)).ToString("P0");
            else if (isInt)
                label.text = ((int)v).ToString();
            else
                label.text = v.ToString("0.##");
        }

        // AcceptableValueList<T> 只在泛型子类上暴露 GetAcceptableValues()，反射取一次
        private static object[] GetAcceptableList(ConfigEntryBase entry)
        {
            var av = entry.Description != null ? entry.Description.AcceptableValues : null;
            if (av == null) return null;
            var t = av.GetType();
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(AcceptableValueList<>)) return null;

            var m = t.GetMethod("GetAcceptableValues");
            var arr = m != null ? m.Invoke(av, null) as Array : null;
            if (arr == null || arr.Length == 0) return null;

            var result = new object[arr.Length];
            for (int i = 0; i < arr.Length; i++) result[i] = arr.GetValue(i);
            return result;
        }

        // ---------- 颜色辅助 ----------

        private static Color GetColor(ConfigEntryBase entry)
        {
            var v = entry.BoxedValue;
            return v is Color ? (Color)v : Color.white;
        }

        private static float GetChannel(Color c, int ch)
        {
            switch (ch)
            {
                case 0: return c.r;
                case 1: return c.g;
                case 2: return c.b;
                default: return c.a;
            }
        }

        private static Color SetChannel(Color c, int ch, float v)
        {
            switch (ch)
            {
                case 0: c.r = v; break;
                case 1: c.g = v; break;
                case 2: c.b = v; break;
                default: c.a = v; break;
            }
            return c;
        }

        // ---------- 布局小件 ----------

        // 一行：左侧名称 + 右侧控件区；整行挂悬停提示。indentLevel>0 时左侧按级别留白（子选项缩进）
        private static RectTransform CreateRow(RectTransform parent, string label, string tooltip, float height,
            Action<string> setHint, out RectTransform controlArea, int indentLevel = 0)
        {
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(parent, false);
            var rowRt = rowGo.AddComponent<RectTransform>();
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            // 近乎透明的底：只为让整行都能接收悬停事件
            var bg = rowGo.AddComponent<Image>();
            bg.color = RowBg;

            var h = rowGo.AddComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.spacing = 6f;
            h.padding = new RectOffset(8 + indentLevel * IndentPxPerLevel, 8, 2, 2);
            h.childAlignment = TextAnchor.MiddleLeft;

            CreateLayoutLabel(rowRt, label, 0f, 14, TextAnchor.MiddleLeft, Color.white)
                .GetComponent<LayoutElement>().flexibleWidth = 1f;

            var areaGo = new GameObject("Controls");
            areaGo.transform.SetParent(rowRt, false);
            controlArea = areaGo.AddComponent<RectTransform>();
            var areaLe = areaGo.AddComponent<LayoutElement>();
            areaLe.flexibleWidth = 1.2f;
            var areaLayout = areaGo.AddComponent<HorizontalLayoutGroup>();
            areaLayout.childControlWidth = true;
            areaLayout.childControlHeight = true;
            areaLayout.childForceExpandWidth = false;
            areaLayout.childForceExpandHeight = false;
            areaLayout.spacing = 4f;
            areaLayout.childAlignment = TextAnchor.MiddleRight;

            AttachHint(rowGo, tooltip, setHint);
            return rowRt;
        }

        private static RectTransform CreateInnerRow(RectTransform parent, float height)
        {
            var go = new GameObject("Row");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.spacing = 4f;
            h.childAlignment = TextAnchor.MiddleLeft;
            return rt;
        }

        // width > 0 → 固定宽；width <= 0 → 弹性宽
        private static Text CreateLayoutLabel(RectTransform parent, string text, float width, int size,
            TextAnchor align, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            if (width > 0f)
            {
                le.preferredWidth = width;
                le.minWidth = width;
            }
            else
            {
                le.flexibleWidth = 1f;
            }
            var t = go.AddComponent<Text>();
            t.font = UiWidgets.DefaultFont;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            return t;
        }

        private static Text AddAreaText(RectTransform parent, float width)
        {
            return CreateLayoutLabel(parent, "", width, 13, TextAnchor.MiddleCenter, Color.white);
        }

        private static Button CreateLayoutButton(RectTransform parent, string name, string label, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            if (width > 0f)
            {
                le.preferredWidth = width;
                le.minWidth = width;
            }
            else
            {
                le.flexibleWidth = 1f;
            }
            le.preferredHeight = 24f;

            var img = go.AddComponent<Image>();
            img.color = BtnNormal;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.38f, 0.38f, 0.38f, 1f);
            colors.pressedColor = new Color(0.20f, 0.20f, 0.20f, 1f);
            btn.colors = colors;

            UiWidgets.CreateText(rt, "Label", label, 13, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero);
            return btn;
        }

        private static Slider CreateSlider(RectTransform parent, float min, float max, bool wholeNumbers, out Text valueLabel)
        {
            var go = new GameObject("Slider");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth = 60f;
            le.preferredHeight = 24f;

            // 底条
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(rt, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(1f, 0.5f);
            bgRt.sizeDelta = new Vector2(0f, 6f);
            bgRt.anchoredPosition = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = SliderBg;

            // 手柄滑动区
            var areaGo = new GameObject("Handle Slide Area");
            areaGo.transform.SetParent(rt, false);
            var areaRt = areaGo.AddComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(7f, 0f);
            areaRt.offsetMax = new Vector2(-7f, 0f);

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(areaRt, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(14f, 0f);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = SliderHandle;

            var slider = go.AddComponent<Slider>();
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;

            valueLabel = AddAreaText(parent, 64f);
            return slider;
        }

        private static Text CreateFullChildText(RectTransform parent, string name, int size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = UiWidgets.DefaultFont;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6f, 0f);
            rt.offsetMax = new Vector2(-6f, 0f);
            return t;
        }

        // 悬停时把 Description.Description 显示到中栏底部说明栏，同时弹出跟随鼠标的浮动说明框
        // （浮动框文本为空时 SettingsWindow 内部自动不显示，说明栏默认文本行为不变）
        private static void AttachHint(GameObject go, string tooltip, Action<string> setHint)
        {
            if (go == null || setHint == null) return;
            var trigger = go.AddComponent<EventTrigger>();
            trigger.triggers = new List<EventTrigger.Entry>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(delegate (BaseEventData d)
            {
                setHint(tooltip);
                SettingsWindow.ShowFloatingHint(tooltip);
            });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(delegate (BaseEventData d)
            {
                setHint(null);
                SettingsWindow.HideFloatingHint();
            });
            trigger.triggers.Add(exit);
        }
    }
}
