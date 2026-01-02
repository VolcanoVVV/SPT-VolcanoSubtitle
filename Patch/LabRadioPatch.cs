using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Subtitle
{
   
    /// <summary>
    /// 实验室“广播语音”事件捕获（A方案：白名单拦截）
    /// - 针对 Announcer 队列播放器（QueuePlayer）播放到 announcer_* 系列 AudioClip 时拦截
    /// - 依据 BepInEx\plugins\subtitle\locales\ch\LabBroadcast.jsonc 做文本映射（支持单剪辑与组合序列）
    /// - 同时走字幕与弹幕（由 Setting 开关控制），颜色使用 BroadcastColor
    /// - 提供 Debug 日志：开关由 MapBroadcastDebug 控制
    /// </summary>
    [HarmonyPatch]
    internal static class LabRadioPatch
    {
        // 统一的 RoleType.jsonc 键名，你也可以改成 "LabBroadcast" 等，只要和 RoleType.jsonc 一致
        private const string BROADCAST_ROLE_KEY = "LabAnnouncer";

        // 简易上色（避免引用其它文件的内部方法）
        private static string ColorWrap(string s, UnityEngine.Color c)
        {
            string hex = UnityEngine.ColorUtility.ToHtmlStringRGB(c);
            return "<color=#" + hex + ">" + s + "</color>";
        }
        // ======== 外部要求：调试/总开关/颜色 ========
        private static bool EnableSub { get { try { return Subtitle.Config.Settings.EnableMapBroadcastSubtitle != null ? Subtitle.Config.Settings.EnableMapBroadcastSubtitle.Value : true; } catch { return true; } } }
        private static bool EnableDm { get { try { return Subtitle.Config.Settings.EnableMapBroadcastDanmaku != null ? Subtitle.Config.Settings.EnableMapBroadcastDanmaku.Value : true; } catch { return true; } } }
        private static bool DebugOn { get { try { return Subtitle.Config.Settings.MapBroadcastDebug != null && Subtitle.Config.Settings.MapBroadcastDebug.Value; } catch { return false; } } }
  
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Subtitle.Debug");

        // ======== JSONC 映射：单剪辑 -> 文本；序列匹配 ========
        private static readonly Dictionary<string, string> s_ClipText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private sealed class Seq
        {
            public string[] Clips;
            public string Text;
        }
        private static readonly List<Seq> s_Seqs = new List<Seq>();
        private static int s_SeqMaxLen = 1;

        // 每个 QueuePlayer（Announcer）维护一个最近播放的剪辑尾巴（用于序列匹配）
        // 用 instanceID 做 key，避免引用存留
        private static readonly Dictionary<int, List<string>> s_Tails = new Dictionary<int, List<string>>();

        private static bool s_Initialized;

        // ============ 外部入口：在 Plugin.EnablePatches() 里调用 ============
        public static void Bootstrap()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            try
            {
                LoadMapping();
                if (DebugOn) Log.LogInfo("[LabBroadcast] init bootstrap.");
            }
            catch (Exception e)
            {
                Log.LogWarning("[LabBroadcast] bootstrap failed: " + e);
            }
        }

        // ============ Patch 目标：QueuePlayer.Play(AudioClip) ============
        // Announcer 节点上就挂着 QueuePlayer，触发时会依次对列表里的 AudioSource 设 clip 并播放
        // 我们拦在 Play(clip) 的 Postfix 里拿到 clip.name
        [HarmonyPatch(typeof(global::QueuePlayer), "Play", new Type[] { typeof(AudioClip) })]
        [HarmonyPostfix]
        private static void QueuePlayer_Play_Postfix(global::QueuePlayer __instance, AudioClip clip)
        {
            try
            {
                if (clip == null) return;

                // 1) 白名单：仅拦 announcer_* 系列
                // （你用 UnityExplorer 确认：announcer_elevators_O / announcer_composite_*_* 等）
                string name = clip.name ?? string.Empty;
                if (!name.StartsWith("announcer_", StringComparison.OrdinalIgnoreCase))
                    return;

                int id = __instance != null ? __instance.GetInstanceID() : 0;

                // 2) 调试日志
                if (DebugOn)
                {
                    string path = TryBuildPath(__instance);
                    Log.LogInfo("[LabBroadcast] hit: clip=" + name + " len=" + clip.length.ToString("0.00") + "s  src=" + path);
                }

                // 3) 单剪辑映射（例如 elevators_O / elevators_R 这类整段）
                string textSingle;
                if (s_ClipText.TryGetValue(name, out textSingle) && !string.IsNullOrEmpty(textSingle))
                {
                    Show(textSingle, clip.length);
                    return;
                }

                // 4) 序列匹配（composite 安全 / 报警 / 3_11 等组合）
                List<string> tail;
                if (!s_Tails.TryGetValue(id, out tail))
                {
                    tail = new List<string>(s_SeqMaxLen + 2);
                    s_Tails[id] = tail;
                }
                tail.Add(name);
                if (tail.Count > s_SeqMaxLen) tail.RemoveAt(0);

                // 尝试匹配所有序列（取“最长匹配”）
                Seq matched = null;
                for (int i = 0; i < s_Seqs.Count; i++)
                {
                    var seq = s_Seqs[i];
                    if (seq == null || seq.Clips == null || seq.Clips.Length == 0) continue;
                    if (EndsWith(tail, seq.Clips))
                    {
                        if (matched == null || seq.Clips.Length > matched.Clips.Length)
                            matched = seq;
                    }
                }

                if (matched != null)
                {
                    if (DebugOn) Log.LogInfo("[LabBroadcast] seq matched: " + string.Join(" + ", matched.Clips) + " => \"" + matched.Text + "\"");
                    // 序列的持续时间保守给个 3.5s（你要更严谨可以在 JSONC 里为序列加上 dur 秒数）
                    Show(matched.Text, Math.Max(clip.length + 0.5f, 3.5f));
                    // 命中后清尾巴，避免叠触发
                    tail.Clear();
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[LabBroadcast] Postfix failed: " + e);
            }
        }

        // ======== 显示到字幕/弹幕（带“系统广播：”前缀、独立颜色） ========
        private static void Show(string text, float durationSec)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 1) roletag：RoleType.jsonc → 键 "LabAnnouncer"，找不到就回退到“系统广播”
            string roleName = Subtitle.Config.Settings.GetRoleLabel(BROADCAST_ROLE_KEY, "系统广播");
            string roleTag = roleName + "：";

            // 2) 四种颜色（带回退：若新项未设，继续使用你现有的全局色）
            // 字幕用色
            Color subRoleColor = (Subtitle.Config.Settings.SubRole_LabAnnouncer != null)
                ? Subtitle.Config.Settings.SubRole_LabAnnouncer.Value
                :  Color.white;

            Color subBodyColor = (Subtitle.Config.Settings.SubText_LabAnnouncer != null)
                ? Subtitle.Config.Settings.SubText_LabAnnouncer.Value
                : Color.white;

            // 弹幕用色
            Color dmRoleColor = (Subtitle.Config.Settings.DmRole_LabAnnouncer != null)
                ? Subtitle.Config.Settings.DmRole_LabAnnouncer.Value
                :  Color.white;

            Color dmBodyColor = (Subtitle.Config.Settings.DmText_LabAnnouncer != null)
                ? Subtitle.Config.Settings.DmText_LabAnnouncer.Value
                : Color.white;

            // 3) roletag 显示与文本组装（尊重全局“是否显示 roletag”的开关）
            bool showRoleInSubtitle = (Subtitle.Config.Settings.SubtitleShowRoleTag != null) ? Subtitle.Config.Settings.SubtitleShowRoleTag.Value : true;
            bool showRoleInDanmaku = (Subtitle.Config.Settings.DanmakuShowRoleTag != null) ? Subtitle.Config.Settings.DanmakuShowRoleTag.Value : true;

            string roleColoredSub = showRoleInSubtitle ? ColorWrap(roleTag, subRoleColor) : string.Empty;
            string roleColoredDm = showRoleInDanmaku ? ColorWrap(roleTag, dmRoleColor) : string.Empty;

            string subText = roleColoredSub + text;
            string dmText = roleColoredDm + text;

            try
            {
                // 4) 分发
                if (EnableSub && SubtitleSystem.SubtitleManager.Instance != null)
                    SubtitleSystem.SubtitleManager.Instance.AddSubtitle(subText, subBodyColor, Math.Max(0.5f, durationSec));

                if (EnableDm && SubtitleSystem.SubtitleManager.Instance != null)
                    SubtitleSystem.SubtitleManager.Instance.AddDanmaku(dmText, dmBodyColor);

                if (DebugOn)
                    Log.LogInfo("[LabBroadcast] OUT: \"" + text + "\" dur=" + durationSec.ToString("0.00") + "s");
            }
            catch (Exception e)
            {
                Log.LogWarning("[LabBroadcast] show failed: " + e);
            }
        }

        // ======== JSONC 映射载入 ========
        private static string MapPath
        {
            get
            {
                // BepInEx\plugins\subtitle\locales\ch\LabBroadcast.jsonc
                return Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "locales", "ch", "LabBroadcast.jsonc");
            }
        }

        private static void LoadMapping()
        {
            s_ClipText.Clear();
            s_Seqs.Clear();
            s_SeqMaxLen = 1;

            try
            {
                var path = MapPath;
                if (!File.Exists(path))
                {
                    Log.LogWarning("[LabBroadcast] mapping not found: " + path);
                    return;
                }

                string raw = File.ReadAllText(path, Encoding.UTF8);
                string json = StripJsonComments(raw);

                // 1) 顶层 "clip":"text" 映射（忽略保留字）
                // 允许写：
                // {
                //   "announcer_elevators_O": "电梯门即将打开……",
                //   "announcer_elevators_R": "电梯门即将关闭……",
                //   "sequences": [ ... ]
                // }
                var kvRx = new Regex("\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Multiline);
                var ms = kvRx.Matches(json);
                for (int i = 0; i < ms.Count; i++)
                {
                    var m = ms[i];
                    string k = m.Groups[1].Value;
                    if (string.Equals(k, "sequences", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(k, "clips", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(k, "text", StringComparison.OrdinalIgnoreCase)) continue;

                    string v = m.Groups[2].Value;
                    if (k.StartsWith("announcer_", StringComparison.OrdinalIgnoreCase))
                        s_ClipText[k] = v ?? "";
                }

                // 2) 序列： { "clips": ["announcer_composite_1_security3","announcer_composite_2_B","announcer_composite_3_11"], "text": "安保等级提升……" }
                var seqRx = new Regex("\\{\\s*\"clips\"\\s*:\\s*\\[(.*?)\\]\\s*,\\s*\"text\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Singleline);
                var sm = seqRx.Matches(json);
                for (int i = 0; i < sm.Count; i++)
                {
                    string arr = sm[i].Groups[1].Value;
                    string txt = sm[i].Groups[2].Value;
                    var names = new List<string>();
                    var nameRx = new Regex("\"([^\"]+)\"");
                    var nm = nameRx.Matches(arr);
                    for (int j = 0; j < nm.Count; j++)
                    {
                        string n = nm[j].Groups[1].Value;
                        if (!string.IsNullOrEmpty(n)) names.Add(n);
                    }
                    if (names.Count > 0)
                    {
                        s_Seqs.Add(new Seq { Clips = names.ToArray(), Text = txt ?? "" });
                        if (names.Count > s_SeqMaxLen) s_SeqMaxLen = names.Count;
                    }
                }

                if (DebugOn)
                {
                    Log.LogInfo("[LabBroadcast] mapping loaded: singles=" + s_ClipText.Count + " seq=" + s_Seqs.Count + " maxLen=" + s_SeqMaxLen);
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[LabBroadcast] load mapping failed: " + e);
            }
        }

        // ======== 小工具 ========

        private static string StripJsonComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);
            bool inStr = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '"')
                {
                    bool escaped = (i > 0 && src[i - 1] == '\\');
                    if (!escaped) inStr = !inStr;
                    sb.Append(c);
                }
                else if (!inStr && c == '/' && i + 1 < src.Length)
                {
                    char n = src[i + 1];
                    if (n == '/')
                    {
                        i += 2;
                        while (i < src.Length && src[i] != '\n') i++;
                        sb.Append('\n');
                    }
                    else if (n == '*')
                    {
                        i += 2;
                        while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                        i++;
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static bool EndsWith(List<string> tail, string[] seq)
        {
            if (tail == null || seq == null) return false;
            if (tail.Count < seq.Length) return false;
            int offset = tail.Count - seq.Length;
            for (int i = 0; i < seq.Length; i++)
            {
                if (!string.Equals(tail[offset + i], seq[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static string TryBuildPath(global::QueuePlayer qp)
        {
            try
            {
                if (qp == null) return "(null)";
                var t = (qp as Component) != null ? (qp as Component).transform : null;
                if (t == null) return qp.GetInstanceID().ToString();
                var names = new List<string>();
                while (t != null)
                {
                    names.Add(t.name);
                    t = t.parent;
                }
                names.Reverse();
                return string.Join("/", names.ToArray());
            }
            catch { return qp.GetInstanceID().ToString(); }
        }


    }
}
