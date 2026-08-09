using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Subtitle.Utils
{
    // 主播模式打码样式
    public enum StreamerMaskStyle
    {
        Asterisks,  // 等长星号：他妈的 -> ***
        Blocks,     // 等长方块：他妈的 -> ■■■
        Grawlix     // 整词替换：他妈的 -> %&*￥%
    }

    // 主播模式（脏话打码）过滤器：
    // - 词表来自 locales/ch/StreamerWords.jsonc（JSON 数组，支持注释）
    // - 文件缺失时自动写出一份默认文件供玩家编辑，并退回内置词表
    // - 文件损坏时打警告日志并退回内置词表
    // - 匹配为子串匹配（拉丁字母不区分大小写），词按长度降序拼成一个正则
    public static class StreamerFilter
    {
        private const string WordsFileName = "StreamerWords.jsonc";
        private const string GrawlixReplacement = "@#$^#";

        // 内置默认词表：文件缺失/损坏/目录不存在时的兜底
        private static readonly string[] s_defaultWords =
        {
            "操你妈", "狗娘养的", "王八蛋", "他妈的",
            "我操", "我草", "卧槽", "操你",
            "他妈", "特么", "妈的",
            "傻逼", "煞笔", "啥比", "沙比", "傻屄",
            "鸡巴", "混蛋", "狗日的",
            "屌", "操", "草"
        };

        private static bool s_loaded;
        private static Regex s_regex;

        // 词表缓存失效（下次 Apply 时重新读盘，供将来“重载词表”使用）
        public static void InvalidateCache()
        {
            s_loaded = false;
            s_regex = null;
        }

        // 对一行字幕文本打码；未启用/文本为空/无词表时原样返回
        public static string Apply(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            bool enabled;
            try
            {
                enabled = Subtitle.Config.Settings.StreamerModeEnabled != null &&
                          Subtitle.Config.Settings.StreamerModeEnabled.Value;
            }
            catch { enabled = false; }
            if (!enabled) return text;

            EnsureLoaded();
            if (s_regex == null) return text;

            StreamerMaskStyle style = StreamerMaskStyle.Asterisks;
            try
            {
                if (Subtitle.Config.Settings.StreamerMaskStyle != null)
                    style = Subtitle.Config.Settings.StreamerMaskStyle.Value;
            }
            catch { }

            return s_regex.Replace(text, delegate (Match m) { return MaskToken(m.Value, style); });
        }

        private static string MaskToken(string matched, StreamerMaskStyle style)
        {
            switch (style)
            {
                case StreamerMaskStyle.Blocks:
                    return new string('■', matched.Length);
                case StreamerMaskStyle.Grawlix:
                    return GrawlixReplacement;
                default:
                    return new string('*', matched.Length);
            }
        }

        // 懒加载 + 缓存：与项目内其它 locales 数据同一模式
        private static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;

            List<string> words = null;
            try
            {
                string dir = Subtitle.Config.PhraseFilterManager.LocalesDir;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    string path = Path.Combine(dir, WordsFileName);
                    if (!File.Exists(path))
                    {
                        // 文件缺失：写出一份默认文件供玩家编辑，随后用内置词表
                        TryWriteDefaultFile(path);
                    }
                    else
                    {
                        words = TryParseWordsFile(path);
                    }
                }
                // 目录本身不存在：静默退回内置词表
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StreamerFilter] load failed: " + e);
            }

            // 解析失败（或目录不存在）时退回内置词表；
            // 注意：解析成功但数组为空视为玩家刻意清空，此时不打码
            if (words == null)
                words = new List<string>(s_defaultWords);

            BuildRegex(words);
        }

        // 解析词表文件；失败返回 null（调用方退回内置词表）
        private static List<string> TryParseWordsFile(string path)
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                json = JsoncUtils.StripJsonComments(json);

                var list = new List<string>();
                var arr = JArray.Parse(json);
                foreach (var item in arr)
                {
                    string w = item != null ? (item.ToString() ?? "").Trim() : null;
                    if (string.IsNullOrEmpty(w)) continue;
                    if (!list.Contains(w)) list.Add(w);
                }
                return list;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StreamerFilter] parse '" + path + "' failed, fallback to built-in list: " + e.Message);
                return null;
            }
        }

        // 词表拼成一个正则：先转义，按长度降序（保证“他妈的”先于“他妈”命中）
        private static void BuildRegex(List<string> words)
        {
            if (words == null || words.Count == 0)
            {
                s_regex = null;
                return;
            }

            words.Sort(delegate (string a, string b) { return b.Length.CompareTo(a.Length); });

            var sb = new StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(Regex.Escape(words[i]));
            }

            s_regex = new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        // 文件缺失时写出默认词表（带说明注释头）；写失败不影响内置词表兜底
        private static void TryWriteDefaultFile(string path)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("// 主播模式 打码词表");
                sb.AppendLine("// - 每行一个词（JSON 字符串数组），可自由增删");
                sb.AppendLine("// - 改完后重开战局（或重启游戏）生效");
                sb.AppendLine("// - 匹配为子串匹配，拉丁字母不区分大小写");
                sb.AppendLine("// - 无需关心顺序：引擎会按词长降序匹配（长词优先）");
                sb.AppendLine("[");
                for (int i = 0; i < s_defaultWords.Length; i++)
                {
                    sb.Append("    \"").Append(s_defaultWords[i]).Append("\"");
                    sb.AppendLine(i + 1 < s_defaultWords.Length ? "," : "");
                }
                sb.AppendLine("]");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StreamerFilter] write default words file failed: " + e.Message);
            }
        }
    }
}
