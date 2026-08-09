using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;

namespace Subtitle.Utils
{
    /// <summary>
    /// 设置界面显示层国际化（i18n）。
    ///
    /// 只映射“显示文本”，绝不改动配置键本身：cfg 文件里的 Key 永远是中文原文，
    /// 本类负责在 GUI 构建时把 Key → 当前语言的显示名/说明。
    ///
    /// 语言文件：&lt;locales 根目录&gt;/&lt;语言代码&gt;/UI.jsonc
    /// （locales 根目录由 PhraseFilterManager 统一解析；ch/en/ru 等语言目录平级）。
    ///
    /// UI.jsonc 结构（支持 // 与 块注释）：
    /// {
    ///   "Common":     { "WindowTitle": "…", "Close": "…", … },          // 界面通用文本（窗口标题/按钮/预览面板等）
    ///   "Categories": { "1. 通用": "通用", … },                            // 分类 section 原名 → 显示名
    ///   "Settings":   { "字幕 显示PMC名字": { "Name": "显示PMC名字", "Desc": "…" }, … } // 配置键 → 显示名/说明
    /// }
    ///
    /// 新增语言：把 locales/ch/UI.jsonc 复制为 locales/&lt;lang&gt;/UI.jsonc，只翻译值，
    /// 键（Common id / Categories 的 section 原名 / Settings 的配置键）必须原样保留。
    ///
    /// 文件缺失或损坏：记警告日志，所有查询直接回落到调用方传入的 fallback（即今天的中文行为）。
    /// </summary>
    internal static class I18n
    {
        /// <summary>默认语言（始终可用，语言目录扫描失败时也保证它在可选项里）。</summary>
        public const string DefaultLanguage = "ch";

        /// <summary>当前语言代码（目录名，如 ch/en/ru）。</summary>
        public static string CurrentLanguage { get; private set; }

        private static readonly ManualLogSource s_Log = Logger.CreateLogSource("Subtitle.I18n");

        // 已解析的三张表；文件缺失/损坏时为 null，查询全部回落 fallback
        private static JObject s_Common;
        private static JObject s_Categories;
        private static JObject s_Settings;

        /// <summary>启动时加载（Settings.Init 末尾调用，传入 UiLanguage.Value）。</summary>
        public static void Init(string lang)
        {
            Reload(lang);
        }

        /// <summary>切换语言：重新读取 &lt;lang&gt;/UI.jsonc 并替换缓存表。</summary>
        public static void Reload(string lang)
        {
            if (string.IsNullOrEmpty(lang)) lang = DefaultLanguage;
            CurrentLanguage = lang;
            s_Common = null;
            s_Categories = null;
            s_Settings = null;

            string file = null;
            try
            {
                file = GetUiFile(lang);
                if (string.IsNullOrEmpty(file) || !File.Exists(file))
                {
                    s_Log.LogWarning("[I18n] 语言文件不存在：" + (file ?? ("locales/" + lang + "/UI.jsonc")) + "，界面文本回落到内置中文。");
                    return;
                }

                var json = JsoncUtils.StripJsonComments(File.ReadAllText(file, Encoding.UTF8));
                var root = JObject.Parse(json);
                s_Common = root["Common"] as JObject;
                s_Categories = root["Categories"] as JObject;
                s_Settings = root["Settings"] as JObject;
                s_Log.LogInfo("[I18n] 已加载语言文件：" + file);
            }
            catch (Exception e)
            {
                s_Common = null;
                s_Categories = null;
                s_Settings = null;
                s_Log.LogWarning("[I18n] 语言文件解析失败：" + (file ?? lang) + "，界面文本回落到内置中文。" + e.Message);
            }
        }

        // ---------- 路径 ----------

        // locales 根目录由 PhraseFilterManager 统一解析，避免从当前语言目录反推时产生歧义
        internal static string LocaleRootDir
        {
            get
            {
                try
                {
                    return Subtitle.Config.PhraseFilterManager.LocaleRootDir;
                }
                catch { return null; }
            }
        }

        private static string GetUiFile(string lang)
        {
            string root = LocaleRootDir;
            if (string.IsNullOrEmpty(root)) return null;
            string current = Path.Combine(root, lang, "UI.jsonc");
            if (File.Exists(current)) return current;
            return Path.Combine(root, DefaultLanguage, "UI.jsonc");
        }

        /// <summary>扫描 locales 根目录下的子目录作为可选语言；无论扫描结果如何都包含 ch 并置顶。</summary>
        public static string[] ScanLanguages()
        {
            var list = new List<string>();
            try
            {
                string root = LocaleRootDir;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    var dirs = Directory.GetDirectories(root);
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        var name = Path.GetFileName(dirs[i]);
                        if (!string.IsNullOrEmpty(name)) list.Add(name);
                    }
                    list.Sort(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }

            list.RemoveAll(delegate (string s) { return string.Equals(s, DefaultLanguage, StringComparison.OrdinalIgnoreCase); });
            list.Insert(0, DefaultLanguage);
            return list.ToArray();
        }

        // ---------- 查询（全部带 fallback，永不抛异常） ----------

        /// <summary>设置项显示名：Settings[配置键].Name。</summary>
        public static string SettingName(string key, string fallback)
        {
            return LookupSetting(key, "Name", fallback);
        }

        /// <summary>设置项悬停说明：Settings[配置键].Desc。</summary>
        public static string SettingDesc(string key, string fallback)
        {
            return LookupSetting(key, "Desc", fallback);
        }

        /// <summary>分类显示名：Categories[section 原名]。</summary>
        public static string Category(string section, string fallback)
        {
            return Lookup(s_Categories, section, fallback);
        }

        /// <summary>界面通用文本：Common[id]（窗口标题、按钮、预览面板等）。</summary>
        public static string Text(string id, string fallback)
        {
            return Lookup(s_Common, id, fallback);
        }

        private static string LookupSetting(string key, string field, string fallback)
        {
            if (s_Settings == null || string.IsNullOrEmpty(key)) return fallback;
            try
            {
                var entry = s_Settings[key] as JObject;
                if (entry == null) return fallback;
                var tok = entry[field];
                if (tok != null && tok.Type == JTokenType.String)
                {
                    var s = (string)tok;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return fallback;
        }

        private static string Lookup(JObject table, string key, string fallback)
        {
            if (table == null || string.IsNullOrEmpty(key)) return fallback;
            try
            {
                var tok = table[key];
                if (tok != null && tok.Type == JTokenType.String)
                {
                    var s = (string)tok;
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return fallback;
        }
    }
}
