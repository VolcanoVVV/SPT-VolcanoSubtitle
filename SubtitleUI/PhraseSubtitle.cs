using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Subtitle.Utils;

namespace SubtitleSystem
{
    public static class PhraseSubtitle
    {
        private const string DefaultVoice = "Default_Voice";

        // 跨语言最终兜底的缓存键（带 @ 避免与真实声线名冲突）
        private const string FallbackChCacheKey = "default_voice@fallback_ch";

        // 缓存：voiceKey -> phrase(trigger) -> (netId | General) -> List<string>
        private static Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> _cache =
            new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>(StringComparer.OrdinalIgnoreCase);

        static PhraseSubtitle()
        {
            // 预加载 default（可选）
            EnsureLoaded(DefaultVoice);
        }

        public static void InvalidateCache()
        {
            _cache.Clear();
        }

        /// <summary>
        /// 对外查询：
        /// 1) 尝试读取 voiceKey.jsonc；
        /// 2) 命中 netId 则取首条；否则从 General 随机一条；
        /// 3) voiceKey 未命或缺失时，降级到当前语言的 Default_Voice.jsonc 按相同逻辑；
        ///    角色台词不跨语言回退；当前语言非默认语言时，最终兜底到 ch 的 Default_Voice.jsonc。
        /// </summary>
        public static string GetSubtitleForChannel(string channel, string voiceKey, string phrase, string netId)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                return I18n.Text("Phrase.UnknownVoice", "(未知语音)");

            // 归一化：文件名一律用小写
            string vk = string.IsNullOrWhiteSpace(voiceKey) ? DefaultVoice : voiceKey.Trim();
            vk = vk.ToLowerInvariant();

            // 先加载指定 voiceKey
            bool loadedVk = EnsureLoaded(vk);

            string p = phrase.Trim();
            string n = string.IsNullOrWhiteSpace(netId) ? null : netId.Trim();

            bool allowNetId;
            bool allowGeneral;
            Subtitle.Config.PhraseFilterManager.GetAllowFlags(channel, vk, p, n, out allowNetId, out allowGeneral);
            if (!allowNetId && !allowGeneral) return null;

            string s;

            // 1) voiceKey + netId（若是数组，取第 0 条）
            if (allowNetId && loadedVk && TryPick(_cache[vk], p, n, false, out s)) return StreamerFilter.Apply(s);

            // 2) voiceKey + General（随机一条）
            if (allowGeneral && loadedVk && TryPick(_cache[vk], p, "General", true, out s)) return StreamerFilter.Apply(s);

            // 全局默认台词有独立过滤状态：关闭 Default_Voice 后，所有声线都不再回退到该文件。
            bool allowDefaultNetId;
            bool allowDefaultGeneral;
            Subtitle.Config.PhraseFilterManager.GetAllowFlags(channel, DefaultVoice, p, n,
                out allowDefaultNetId, out allowDefaultGeneral);
            if (!allowDefaultNetId && !allowDefaultGeneral) return null;

            // 3) default + netId
            EnsureLoaded(DefaultVoice);
            if (allowDefaultNetId && _cache.ContainsKey(DefaultVoice) && TryPick(_cache[DefaultVoice], p, n, false, out s)) return StreamerFilter.Apply(s);

            // 4) default + General（随机一条）
            if (allowDefaultGeneral && _cache.ContainsKey(DefaultVoice) && TryPick(_cache[DefaultVoice], p, "General", true, out s)) return StreamerFilter.Apply(s);

            // 5) 跨语言最终兜底：当前语言非默认语言时，尝试 ch 的 Default_Voice（复用 Default_Voice 过滤标志）
            if (IsNonDefaultLanguage() && EnsureFallbackChLoaded())
            {
                if (allowDefaultNetId && TryPick(_cache[FallbackChCacheKey], p, n, false, out s)) return StreamerFilter.Apply(s);
                if (allowDefaultGeneral && TryPick(_cache[FallbackChCacheKey], p, "General", true, out s)) return StreamerFilter.Apply(s);
            }

            Debug.LogWarning(string.Format("[SubtitleSystem] Miss voice='{0}' phrase='{1}' netId='{2}'.",
                vk, p, n));
            return I18n.Text("Phrase.MissingText", "(嘀咕声)");
        }

        internal static string DescribeResolution(string channel, string voiceKey, string phrase, string netId)
        {
            string vk = string.IsNullOrWhiteSpace(voiceKey) ? DefaultVoice : voiceKey.Trim();
            vk = vk.ToLowerInvariant();
            string p = string.IsNullOrWhiteSpace(phrase) ? "" : phrase.Trim();
            string n = string.IsNullOrWhiteSpace(netId) ? null : netId.Trim();

            bool allowNetId;
            bool allowGeneral;
            Subtitle.Config.PhraseFilterManager.GetAllowFlags(channel, vk, p, n,
                out allowNetId, out allowGeneral);
            if (!allowNetId && !allowGeneral) return "FILTER: PhraseFilter";

            EnsureLoaded(vk);
            string ignored;
            Dictionary<string, Dictionary<string, List<string>>> table;
            if (_cache.TryGetValue(vk, out table))
            {
                if (allowNetId && TryPick(table, p, n, false, out ignored))
                    return "VOICE: " + vk + ".jsonc / " + p + " / #" + n;
                if (allowGeneral && TryPick(table, p, "General", false, out ignored))
                    return "VOICE GENERAL: " + vk + ".jsonc / " + p;
            }

            bool defaultNetId;
            bool defaultGeneral;
            Subtitle.Config.PhraseFilterManager.GetAllowFlags(channel, DefaultVoice, p, n,
                out defaultNetId, out defaultGeneral);
            if (!defaultNetId && !defaultGeneral) return "FILTER: Default_Voice disabled";
            EnsureLoaded(DefaultVoice);
            if (_cache.TryGetValue(DefaultVoice, out table))
            {
                if (defaultNetId && TryPick(table, p, n, false, out ignored))
                    return "DEFAULT: Default_Voice.jsonc / " + p + " / #" + n;
                if (defaultGeneral && TryPick(table, p, "General", false, out ignored))
                    return "DEFAULT GENERAL: Default_Voice.jsonc / " + p;
            }
            if (IsNonDefaultLanguage() && EnsureFallbackChLoaded() && _cache.TryGetValue(FallbackChCacheKey, out table))
            {
                if (defaultNetId && TryPick(table, p, n, false, out ignored))
                    return "DEFAULT CH: ch/Default_Voice.jsonc / " + p + " / #" + n;
                if (defaultGeneral && TryPick(table, p, "General", false, out ignored))
                    return "DEFAULT CH GENERAL: ch/Default_Voice.jsonc / " + p;
            }
            return "MISSING: current locale";
        }

        // ================= 内部实现 =================

        // 确保某个 voiceKey 的文件已加载到缓存
        private static bool EnsureLoaded(string voiceKey)
        {
            if (_cache.ContainsKey(voiceKey)) return true;

            try
            {
                var path = string.Equals(voiceKey, DefaultVoice, StringComparison.OrdinalIgnoreCase)
                    ? Subtitle.Config.PhraseFilterManager.ResolveCurrentLocaleFile(voiceKey + ".jsonc")
                    : Subtitle.Config.PhraseFilterManager.ResolveCurrentLocaleVoiceFile(voiceKey);
                return LoadIntoCache(voiceKey, path);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SubtitleSystem] Load voice '" + voiceKey + "' failed: " + ex);
                return false;
            }
        }

        // 跨语言最终兜底：加载 ch 目录的 Default_Voice.jsonc，目录或文件缺失时静默失败
        private static bool EnsureFallbackChLoaded()
        {
            if (_cache.ContainsKey(FallbackChCacheKey)) return true;

            string dir = Subtitle.Config.PhraseFilterManager.DefaultLocalesDir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            return LoadIntoCache(FallbackChCacheKey, Path.Combine(dir, DefaultVoice + ".jsonc"));
        }

        // 当前语言为空视为默认语言，此时不启用跨语言兜底
        private static bool IsNonDefaultLanguage()
        {
            string lang = I18n.CurrentLanguage;
            return !string.IsNullOrEmpty(lang)
                && !string.Equals(lang, I18n.DefaultLanguage, StringComparison.OrdinalIgnoreCase);
        }

        // 读路径→解析→填缓存，任何失败静默返回 false
        private static bool LoadIntoCache(string cacheKey, string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return false;
                }

                var json = File.ReadAllText(path);
                // 支持 .jsonc：去注释（逐字符扫描，字符串字面量内的 // 不会被误删）
                json = JsoncUtils.StripJsonComments(json);

                var root = JObject.Parse(json);

                // phrase -> (id | "General") -> List<string>
                var table = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

                foreach (var phProp in root.Properties())
                {
                    var phKey = (phProp.Name ?? "").Trim();
                    if (phKey.Length == 0) continue;

                    var idMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                    var idsObj = phProp.Value as JObject;
                    if (idsObj != null)
                    {
                        foreach (var idProp in idsObj.Properties())
                        {
                            var idKey = (idProp.Name ?? "").Trim();
                            if (idKey.Length == 0) continue;

                            var list = new List<string>();
                            if (idProp.Value is JArray arr)
                            {
                                foreach (var item in arr)
                                {
                                    var s = item != null ? item.ToString().Trim() : null;
                                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                                }
                            }
                            else
                            {
                                var s = idProp.Value != null ? idProp.Value.ToString().Trim() : null;
                                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                            }

                            if (list.Count > 0)
                                idMap[idKey] = list;
                        }
                    }

                    table[phKey] = idMap;
                }

                _cache[cacheKey] = table;
                Debug.Log("[SubtitleSystem] Loaded: " + cacheKey + "  from " + path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[SubtitleSystem] Load voice '" + cacheKey + "' failed: " + ex);
                return false;
            }
        }

        // 从某个 voice 的表里取一条（netId 精确/General 随机）
        private static bool TryPick(
            Dictionary<string, Dictionary<string, List<string>>> voiceTable,
            string phrase, string key, bool random, out string result)
        {
            result = null;
            if (voiceTable == null || string.IsNullOrEmpty(key)) return false;

            Dictionary<string, List<string>> idMap;
            if (!voiceTable.TryGetValue(phrase, out idMap) || idMap == null) return false;

            List<string> list;
            if (!idMap.TryGetValue(key, out list) || list == null || list.Count == 0) return false;

            if (!random)
            {
                result = list[0];
                return true;
            }

            int idx = (list.Count == 1) ? 0 : UnityEngine.Random.Range(0, list.Count);
            result = list[idx];
            return true;
        }
    }
}
