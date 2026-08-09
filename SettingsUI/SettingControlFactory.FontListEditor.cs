using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Subtitle.Config;
using Subtitle.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Subtitle.SettingsUI
{
    /// <summary>
    /// 三个 *FontFamilyCsv（字幕/弹幕/3D气泡 字体类型）条目的专用编辑器。
    /// 结构：标题行 + 当前列表（每行：序号 + 字体名 + ↑上移 + ✕移除）+ 两个循环选择器
    /// （游戏字体 / 系统字体，各为 ◀ 名字 ▶ + 添加）。
    /// 所有改动都把列表重写成逗号分隔 CSV 写回 entry.BoxedValue，
    /// 走既有 SettingChanged → 运行期样式刷新链路，预览即时生效。
    /// </summary>
    internal static partial class SettingControlFactory
    {
        // 候选池缓存：Font.GetOSInstalledFontNames 首次调用较慢，只取一次；
        // 游戏字体名来自 SubtitleFontLoader 的游戏字体缓存键（已排序），也只取一次。
        private static List<string> s_OsFontPool;
        private static List<string> s_GameFontPool;

        // 三个 *FontFamilyCsv 条目按引用识别（Setting.cs 里的 public static 字段，实例唯一）
        private static bool IsFontFamilyCsvEntry(ConfigEntryBase entry)
        {
            return ReferenceEquals(entry, Settings.SubtitleFontFamilyCsv)
                || ReferenceEquals(entry, Settings.DanmakuFontFamilyCsv)
                || ReferenceEquals(entry, Settings.World3DFontFamilyCsv);
        }

        private static void BuildFontFamilyEditor(ConfigEntryBase entry, RectTransform parent, string label,
            string tooltip, Action<string> setHint)
        {
            // 悬停提示：在原 Description 前补充优先级规则与字体资源包的覆盖关系
            string hint =
                I18n.Text("FontList.HintPrefix",
                    "优先级从上到下依次尝试（第一行为首选字体），全部不可用则退回内置 Arial。\n" +
                    "支持 game:字体名 使用游戏内置字体；不带前缀的名字按系统已安装字体解析。\n" +
                    "注意：若本渠道的「字体资源包」非空，会优先使用资源包字体，本列表仅作为资源包加载失败时的回退候选。\n") +
                (string.IsNullOrEmpty(tooltip) ? "" : tooltip);

            // 外框：垂直布局 + ContentSizeFitter，高度随列表行数自适应（参照 BuildColor 的块样式）
            var blockGo = new GameObject("FontListEditor");
            blockGo.transform.SetParent(parent, false);
            var blockRt = blockGo.AddComponent<RectTransform>();
            var bg = blockGo.AddComponent<Image>();
            bg.color = RowBg;

            var v = blockGo.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.spacing = 2f;
            v.padding = new RectOffset(8 + GetIndentLevel(entry) * IndentPxPerLevel, 8, 4, 4);
            var fitter = blockGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AttachHint(blockGo, hint, setHint);

            // 先声明再赋值：匿名方法体内要引用自身，一次性赋值会报“未赋值局部变量”
            Action rebuildList = null;

            // 标题行（右端“重置”：整个 CSV 恢复默认候选列表）
            var head = CreateInnerRow(blockRt, 26f);
            CreateLayoutLabel(head, label + I18n.Text("FontList.TitleSuffix", "（优先级从高到低）"), 0f, 14, TextAnchor.MiddleLeft, Color.white);
            AddResetButton(head, entry, delegate { rebuildList(); });

            // 列表容器：增删/排序后整体清空重建
            var listGo = new GameObject("FontList");
            listGo.transform.SetParent(blockRt, false);
            var listRt = listGo.AddComponent<RectTransform>();
            var lv = listGo.AddComponent<VerticalLayoutGroup>();
            lv.childControlWidth = true;
            lv.childControlHeight = true;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;
            lv.spacing = 2f;

            // 上面已声明（标题行的重置按钮也要引用），这里一次性赋值
            rebuildList = delegate
            {
                UiWidgets.ClearChildren(listRt);
                var fonts = ParseFontCsv(entry.BoxedValue as string);
                if (fonts.Count == 0)
                {
                    // 空列表：仅占位提示，不写回任何值
                    var empty = CreateInnerRow(listRt, 24f);
                    CreateLayoutLabel(empty, I18n.Text("FontList.Empty", "（列表为空，将直接使用内置 Arial）"), 0f, 12, TextAnchor.MiddleLeft, HintGray);
                }
                for (int i = 0; i < fonts.Count; i++)
                {
                    int captured = i;
                    var row = CreateInnerRow(listRt, 24f);
                    CreateLayoutLabel(row, (i + 1) + ".", 22f, 12, TextAnchor.MiddleCenter, HintGray);
                    CreateLayoutLabel(row, fonts[i], 0f, 13, TextAnchor.MiddleLeft, Color.white);

                    var up = CreateLayoutButton(row, "Up", "↑", 24f);
                    up.onClick.AddListener(delegate
                    {
                        MoveFont(entry, captured, -1); // 首位再上移为无操作
                        rebuildList();
                    });
                    var del = CreateLayoutButton(row, "Del", "✕", 24f);
                    del.onClick.AddListener(delegate
                    {
                        RemoveFont(entry, captured);
                        rebuildList();
                    });
                }
            };
            rebuildList();

            // 两行添加器：游戏字体写成 game:名字；系统字体写裸名字
            BuildFontAddRow(blockRt, I18n.Text("FontList.GameFonts", "游戏字体"), GetGameFontPool(),
                delegate (string name) { return "game:" + name; }, entry, setHint, rebuildList);
            BuildFontAddRow(blockRt, I18n.Text("FontList.SystemFonts", "系统字体"), GetOsFontPool(),
                delegate (string name) { return name; }, entry, setHint, rebuildList);
        }

        // 一行添加器：渠道名 + ◀ 候选名 ▶ + 添加
        private static void BuildFontAddRow(RectTransform parent, string title, List<string> pool,
            Func<string, string> toCsvToken, ConfigEntryBase entry, Action<string> setHint, Action rebuildList)
        {
            var row = CreateInnerRow(parent, 26f);
            CreateLayoutLabel(row, title, 56f, 12, TextAnchor.MiddleLeft, HintGray);
            var prev = CreateLayoutButton(row, "Prev", "◀", 24f);
            var nameLabel = CreateLayoutLabel(row, "", 0f, 13, TextAnchor.MiddleCenter, Color.white);
            var next = CreateLayoutButton(row, "Next", "▶", 24f);
            var add = CreateLayoutButton(row, "Add", I18n.Text("FontList.Add", "添加"), 44f);

            int idx = 0;
            Action refresh = delegate
            {
                if (pool == null || pool.Count == 0)
                {
                    nameLabel.text = I18n.Text("FontList.None", "(无可用字体)");
                    return;
                }
                if (idx >= pool.Count) idx = 0;
                nameLabel.text = pool[idx];
            };
            refresh();

            prev.onClick.AddListener(delegate
            {
                if (pool == null || pool.Count == 0) return;
                idx = (idx - 1 + pool.Count) % pool.Count;
                refresh();
            });
            next.onClick.AddListener(delegate
            {
                if (pool == null || pool.Count == 0) return;
                idx = (idx + 1) % pool.Count;
                refresh();
            });
            add.onClick.AddListener(delegate
            {
                if (pool == null || pool.Count == 0) return;
                var token = toCsvToken(pool[idx]);
                var fonts = ParseFontCsv(entry.BoxedValue as string);
                // 已在列表中（忽略大小写）：不重复添加，只提示
                for (int i = 0; i < fonts.Count; i++)
                {
                    if (string.Equals(fonts[i], token, StringComparison.OrdinalIgnoreCase))
                    {
                        if (setHint != null) setHint(string.Format(I18n.Text("FontList.AlreadyInList", "「{0}」已在列表中。"), token));
                        return;
                    }
                }
                fonts.Add(token);
                WriteFontCsv(entry, fonts);
                rebuildList();
            });
        }

        // ---------- 列表读写 ----------

        // 与 Settings.Methods.BuildFontSpec 的切分约定一致：同时接受 , 和 ; 两种分隔符
        private static List<string> ParseFontCsv(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(csv)) return list;
            var parts = csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var s = parts[i].Trim();
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return list;
        }

        // 统一用英文逗号写回（BuildFontSpec 与预设快照读写都兼容）
        private static void WriteFontCsv(ConfigEntryBase entry, List<string> fonts)
        {
            entry.BoxedValue = string.Join(",", fonts.ToArray());
        }

        private static void RemoveFont(ConfigEntryBase entry, int index)
        {
            var fonts = ParseFontCsv(entry.BoxedValue as string);
            if (index < 0 || index >= fonts.Count) return;
            fonts.RemoveAt(index);
            WriteFontCsv(entry, fonts);
        }

        private static void MoveFont(ConfigEntryBase entry, int index, int delta)
        {
            var fonts = ParseFontCsv(entry.BoxedValue as string);
            int target = index + delta;
            if (index < 0 || index >= fonts.Count || target < 0 || target >= fonts.Count) return;
            var s = fonts[index];
            fonts.RemoveAt(index);
            fonts.Insert(target, s);
            WriteFontCsv(entry, fonts);
        }

        // ---------- 候选池 ----------

        // 游戏字体：游戏字体缓存的键，返回时已按忽略大小写排序
        private static List<string> GetGameFontPool()
        {
            if (s_GameFontPool == null)
            {
                s_GameFontPool = new List<string>();
                try
                {
                    var names = SubtitleSystem.SubtitleFontLoader.GetGameFontNames();
                    if (names != null)
                    {
                        for (int i = 0; i < names.Count; i++)
                        {
                            if (IsCsvSafeName(names[i])) s_GameFontPool.Add(names[i]);
                        }
                    }
                }
                catch { }
            }
            return s_GameFontPool;
        }

        // 系统字体：GetOSInstalledFontNames 去重排序（SortedSet 顺带完成）；首次调用较慢，只此一次
        private static List<string> GetOsFontPool()
        {
            if (s_OsFontPool == null)
            {
                s_OsFontPool = new List<string>();
                try
                {
                    var names = Font.GetOSInstalledFontNames();
                    if (names != null)
                    {
                        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < names.Length; i++)
                        {
                            var n = names[i];
                            if (string.IsNullOrEmpty(n)) continue;
                            n = n.Trim();
                            if (IsCsvSafeName(n)) set.Add(n);
                        }
                        s_OsFontPool.AddRange(set);
                    }
                }
                catch { }
            }
            return s_OsFontPool;
        }

        // 含 , 或 ; 的名字无法经 CSV 往返（解析时会被切开），直接剔除出候选。
        // 实际上系统字体 family 名几乎不会含这两个字符，这里只是兜底。
        private static bool IsCsvSafeName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOf(',') < 0 && name.IndexOf(';') < 0;
        }
    }
}
