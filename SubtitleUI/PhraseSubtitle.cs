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
        /// 3) voiceKey 未命或缺失时，降级到 default.jsonc 按相同逻辑。
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

            Debug.LogWarning(string.Format("[SubtitleSystem] Miss voice='{0}' phrase='{1}' netId='{2}'.",
                vk, p, n));
            return "！——未找到对应台词——！";
        }

        // ================= 内部实现 =================

        // 确保某个 voiceKey 的文件已加载到缓存
        private static bool EnsureLoaded(string voiceKey)
        {
            if (_cache.ContainsKey(voiceKey)) return true;

            try
            {
                var path = string.Equals(voiceKey, DefaultVoice, StringComparison.OrdinalIgnoreCase)
                    ? Subtitle.Config.PhraseFilterManager.ResolveLocaleFile(voiceKey + ".jsonc")
                    : Subtitle.Config.PhraseFilterManager.ResolveVoiceFile(voiceKey);
                if (!File.Exists(path))
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

                _cache[voiceKey] = table;
                Debug.Log("[SubtitleSystem] Loaded: " + voiceKey + "  from " + path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[SubtitleSystem] Load voice '" + voiceKey + "' failed: " + ex);
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
