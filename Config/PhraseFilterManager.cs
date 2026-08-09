
using Newtonsoft.Json.Linq;
using Subtitle.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Subtitle.Config
{
    internal static class PhraseFilterManager
    {
        private const string PresetFileName = "PhraseFilter.jsonc";
        private const string PresetFileExtension = ".jsonc";
        private const string DefaultPresetName = "PhraseFilter";
        private const string DefaultChannel = "Subtitle";
        internal const string DefaultVoiceKey = "Default_Voice";

        private static readonly string[] s_channels = { "Subtitle", "Danmaku", "World3D" };
        private static readonly string[] s_defaultIgnoredTriggers = { "OnAgony", "OnBreath", "OnBeingHurt", "None" };

        private static bool s_loaded;
        private static readonly Dictionary<string, PhraseFilterPreset> s_currentByChannel =
            new Dictionary<string, PhraseFilterPreset>(StringComparer.OrdinalIgnoreCase);
        private static string s_currentName;
        private static string s_currentChannel = DefaultChannel;

        private static readonly List<string> s_cachedVoiceNames = new List<string>();
        private static readonly Dictionary<string, Dictionary<string, List<string>>> s_cachedVoiceMap =
            new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, JObject> s_voiceJsonCache =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        // 已解析的声线文件路径缓存：voiceKey -> 磁盘路径（仅缓存“存在”的结果，缺失时下次仍重新探测）
        private static readonly Dictionary<string, string> s_voicePathCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // locales 根目录与当前语言目录缓存
        private static string s_cachedLocalesRootDir;
        private static string s_cachedLocalesDir;
        private static string s_cachedLocaleLanguage;
        // voices 子目录解析结果缓存（随 locales 目录失效而失效）
        private static string s_cachedVoicesDir;
        private static string s_cachedVoicesDirBase;

        internal static string CurrentPresetName
        {
            get { return string.IsNullOrEmpty(s_currentName) ? DefaultPresetName : s_currentName; }
        }

        internal static string CurrentChannel
        {
            get { return s_currentChannel; }
        }

        internal static IReadOnlyList<string> Channels
        {
            get { return s_channels; }
        }

        internal static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;
            if (!TryLoadPreset(DefaultPresetName))
            {
                for (int i = 0; i < s_channels.Length; i++)
                {
                    PhraseFilterPreset preset = GetOrCreateCurrent(s_channels[i]);
                    EnsureDefaultIgnoredTriggers(preset);
                }
                SavePreset(DefaultPresetName, GetOrCreateCurrent(DefaultChannel));
            }
        }

        internal static bool TryLoadPreset(string name)
        {
            try
            {
                string path = GetPresetPath(name);
                bool loadedLegacyPath = false;
                if (!File.Exists(path))
                {
                    string legacyPath = GetLegacyPresetPath(name);
                    if (!File.Exists(legacyPath)) return false;
                    path = legacyPath;
                    loadedLegacyPath = true;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                json = JsoncUtils.StripJsonComments(json);
                JObject root = JObject.Parse(json);
                JObject channelsObj = root["Channels"] as JObject;

                s_currentByChannel.Clear();
                if (channelsObj != null)
                {
                    foreach (JProperty cp in channelsObj.Properties())
                    {
                        string channelName = (cp.Name ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(channelName)) continue;
                        JObject cObj = cp.Value as JObject;
                        if (cObj == null) continue;
                        s_currentByChannel[channelName] = ParsePreset(cObj);
                    }
                }
                else
                {
                    s_currentByChannel[DefaultChannel] = ParsePreset(root);
                }

                for (int i = 0; i < s_channels.Length; i++)
                {
                    PhraseFilterPreset preset = GetOrCreateCurrent(s_channels[i]);
                    EnsureDefaultIgnoredTriggers(preset);
                }

                s_currentName = NormalizePresetName(name);
                if (loadedLegacyPath)
                {
                    SavePreset(name, GetOrCreateCurrent(DefaultChannel));
                    Debug.Log("[PhraseFilter] 已迁移过滤状态到全语言共享路径：" + GetPresetPath(name));
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PhraseFilter] Load preset failed: " + e);
                return false;
            }
        }

        internal static bool SavePreset(string name, PhraseFilterPreset preset)
        {
            try
            {
                if (preset == null) return false;
                string path = GetPresetPath(name);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                JObject root = new JObject();
                JObject channelsObj = new JObject();
                for (int i = 0; i < s_channels.Length; i++)
                {
                    string ch = s_channels[i];
                    PhraseFilterPreset p;
                    if (!s_currentByChannel.TryGetValue(ch, out p) || p == null) continue;
                    JObject block = WritePreset(p);
                    if (block != null && block.Count > 0)
                        channelsObj[ch] = block;
                }
                if (channelsObj.Count == 0)
                    channelsObj[DefaultChannel] = WritePreset(preset);

                root["Channels"] = channelsObj;

                string txt = root.ToString(Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(path, txt, Encoding.UTF8);
                s_currentName = NormalizePresetName(name);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PhraseFilter] Save preset failed: " + e);
                return false;
            }
        }

        internal static PhraseFilterPreset GetOrCreateCurrent()
        {
            return GetOrCreateCurrent(s_currentChannel);
        }

        internal static PhraseFilterPreset GetOrCreateCurrent(string channel)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(channel)) channel = DefaultChannel;
            PhraseFilterPreset preset;
            if (!s_currentByChannel.TryGetValue(channel, out preset) || preset == null)
            {
                preset = new PhraseFilterPreset();
                s_currentByChannel[channel] = preset;
            }
            return preset;
        }

        internal static void SetCurrentChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel)) channel = DefaultChannel;
            s_currentChannel = channel;
            GetOrCreateCurrent(channel);
        }

        internal static VoiceFilter GetOrCreateVoice(string channel, string voiceKey)
        {
            PhraseFilterPreset preset = GetOrCreateCurrent(channel);
            VoiceFilter vf;
            if (preset.Voices.TryGetValue(voiceKey, out vf) && vf != null) return vf;
            vf = new VoiceFilter();
            preset.Voices[voiceKey] = vf;
            return vf;
        }

        internal static TriggerFilter GetOrCreateTrigger(string channel, string voiceKey, string trigger)
        {
            VoiceFilter vf = GetOrCreateVoice(channel, voiceKey);
            if (vf.Triggers == null) vf.Triggers = new Dictionary<string, TriggerFilter>(StringComparer.OrdinalIgnoreCase);
            TriggerFilter tf;
            if (vf.Triggers.TryGetValue(trigger, out tf) && tf != null) return tf;
            tf = new TriggerFilter();
            if (IsDefaultDisabledTrigger(trigger))
                tf.Enabled = false;
            vf.Triggers[trigger] = tf;
            return tf;
        }

        internal static void GetAllowFlags(string voiceKey, string trigger, string netId, out bool allowNetId, out bool allowGeneral)
        {
            GetAllowFlags(DefaultChannel, voiceKey, trigger, netId, out allowNetId, out allowGeneral);
        }

        internal static void GetAllowFlags(string channel, string voiceKey, string trigger, string netId,
            out bool allowNetId, out bool allowGeneral)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(channel)) channel = DefaultChannel;
            PhraseFilterPreset preset;
            if (!s_currentByChannel.TryGetValue(channel, out preset) || preset == null)
            {
                allowNetId = true;
                allowGeneral = true;
                return;
            }

            bool presetDefault = preset.DefaultAllow;
            if (string.IsNullOrEmpty(voiceKey) || string.IsNullOrEmpty(trigger))
            {
                allowNetId = presetDefault;
                allowGeneral = presetDefault;
                return;
            }

            if (preset.GlobalTriggers != null)
            {
                TriggerFilter gf;
                if (preset.GlobalTriggers.TryGetValue(trigger, out gf) && gf != null)
                {
                    if (!gf.Enabled)
                    {
                        allowNetId = false;
                        allowGeneral = false;
                        return;
                    }
                    bool globalDefault = gf.DefaultAllow ?? presetDefault;
                    allowGeneral = ResolveNetIdAllowed(gf, "General", globalDefault);
                    if (gf.GeneralOnly)
                    {
                        allowNetId = false;
                        return;
                    }
                }
            }

            VoiceFilter vf;
            if (!preset.Voices.TryGetValue(voiceKey, out vf) || vf == null)
            {
                allowNetId = presetDefault;
                allowGeneral = presetDefault;
                return;
            }
            if (!vf.Enabled)
            {
                allowNetId = false;
                allowGeneral = false;
                return;
            }

            bool voiceDefault = vf.DefaultAllow ?? presetDefault;
            if (vf.Triggers == null)
            {
                allowNetId = voiceDefault;
                allowGeneral = voiceDefault;
                return;
            }

            TriggerFilter tf;
            if (!vf.Triggers.TryGetValue(trigger, out tf) || tf == null)
            {
                allowNetId = voiceDefault;
                allowGeneral = voiceDefault;
                return;
            }
            if (!tf.Enabled)
            {
                allowNetId = false;
                allowGeneral = false;
                return;
            }

            bool triggerDefault = tf.DefaultAllow ?? voiceDefault;
            allowGeneral = ResolveNetIdAllowed(tf, "General", triggerDefault);
            if (tf.GeneralOnly)
            {
                allowNetId = false;
                return;
            }

            if (string.IsNullOrEmpty(netId))
            {
                allowNetId = false;
                return;
            }

            allowNetId = ResolveNetIdAllowed(tf, netId, triggerDefault);
        }

        private static bool ResolveNetIdAllowed(TriggerFilter tf, string netId, bool fallback)
        {
            if (tf != null && tf.NetIds != null)
            {
                bool allowed;
                if (tf.NetIds.TryGetValue(netId, out allowed))
                    return allowed;
            }
            return fallback;
        }

        // ================= Local Voices =================

        internal static List<string> ListVoiceKeys()
        {
            List<string> list = LoadVoiceKeysFromDisk();
            if (list.Count == 0 && s_cachedVoiceNames.Count > 0)
            {
                List<string> cached = new List<string>(s_cachedVoiceNames);
                MoveDefaultVoiceFirst(cached);
                return cached;
            }

            MoveDefaultVoiceFirst(list);
            CacheVoiceNames(list);
            return list;
        }

        internal static Dictionary<string, List<string>> LoadVoiceTriggerNetIds(string voiceKey)
        {
            Dictionary<string, List<string>> map = BuildVoiceTriggerNetIds(voiceKey);
            if (map.Count == 0)
            {
                Dictionary<string, List<string>> cached;
                if (s_cachedVoiceMap.TryGetValue(voiceKey, out cached))
                    return CloneTriggerMap(cached);
            }
            if (map.Count > 0)
                s_cachedVoiceMap[voiceKey] = CloneTriggerMap(map);
            return map;
        }

        private static List<string> LoadVoiceKeysFromDisk()
        {
            List<string> list = new List<string>();
            try
            {
                List<string> dirs = GetVoiceSearchDirs();
                for (int d = 0; d < dirs.Count; d++)
                {
                    string dir = dirs[d];
                    if (!Directory.Exists(dir)) continue;
                    string[] files = Directory.GetFiles(dir, "*.json*", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string path = files[i];
                        string ext = Path.GetExtension(path);
                        if (!string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(ext, ".jsonc", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string name = Path.GetFileNameWithoutExtension(path);
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!ListContainsIgnoreCase(list, name))
                            list.Add(name);
                    }
                }
            }
            catch { }

            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static void MoveDefaultVoiceFirst(List<string> list)
        {
            if (list == null || list.Count == 0) return;
            int idx = list.FindIndex(n => string.Equals(n, DefaultVoiceKey, StringComparison.OrdinalIgnoreCase));
            if (idx <= 0) return;
            string name = list[idx];
            list.RemoveAt(idx);
            list.Insert(0, name);
        }

        private static Dictionary<string, List<string>> BuildVoiceTriggerNetIds(string voiceKey)
        {
            Dictionary<string, List<string>> map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(voiceKey)) return map;

            JObject root = GetVoiceRoot(voiceKey);
            if (root == null) return map;

            foreach (JProperty prop in root.Properties())
            {
                string trigger = (prop.Name ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(trigger)) continue;

                List<string> ids = ExtractNetIds(prop.Value);
                ids.Sort(StringComparer.OrdinalIgnoreCase);
                map[trigger] = ids;
            }

            return map;
        }

        internal static string GetVoiceLineText(string voiceKey, string trigger, string netId)
        {
            string text = TryGetVoiceLineText(voiceKey, trigger, netId);
            if (string.IsNullOrEmpty(text) && !string.Equals(netId, "General", StringComparison.OrdinalIgnoreCase))
                text = TryGetVoiceLineText(voiceKey, trigger, "General");
            if (string.IsNullOrEmpty(text) && !string.Equals(voiceKey, DefaultVoiceKey, StringComparison.OrdinalIgnoreCase))
                text = TryGetVoiceLineText(DefaultVoiceKey, trigger, "General");
            return text ?? string.Empty;
        }

        internal static string GetGlobalLineText(string voiceKey, string trigger)
        {
            string text = TryGetVoiceLineText(voiceKey, trigger, "General");
            if (string.IsNullOrEmpty(text))
                text = TryGetVoiceLineText(DefaultVoiceKey, trigger, "General");
            return text ?? string.Empty;
        }

        private static string TryGetVoiceLineText(string voiceKey, string trigger, string netId)
        {
            if (string.IsNullOrEmpty(voiceKey) || string.IsNullOrEmpty(trigger) || string.IsNullOrEmpty(netId))
                return string.Empty;

            JObject root = GetVoiceRoot(voiceKey);
            if (root == null) return string.Empty;

            JToken trigToken = root[trigger];
            if (trigToken == null) return string.Empty;

            JObject obj = trigToken as JObject;
            if (obj != null)
            {
                JToken lineToken = obj[netId];
                if (lineToken == null) return string.Empty;
                return TokenToLineText(lineToken);
            }

            JArray arr = trigToken as JArray;
            if (arr != null)
            {
                int index;
                if (int.TryParse(netId, out index) && index >= 0 && index < arr.Count)
                {
                    JToken lineToken = arr[index];
                    return TokenToLineText(lineToken);
                }
                return string.Empty;
            }

            if (trigToken is JValue)
                return TokenToLineText(trigToken);

            return string.Empty;
        }

        private static string TokenToLineText(JToken token)
        {
            if (token == null) return string.Empty;
            if (token.Type == JTokenType.String) return token.Value<string>();

            JArray arr = token as JArray;
            if (arr != null)
            {
                List<string> parts = new List<string>();
                for (int i = 0; i < arr.Count; i++)
                {
                    JToken item = arr[i];
                    if (item == null) continue;
                    string part = item.Type == JTokenType.String ? item.Value<string>() : item.ToString();
                    if (!string.IsNullOrEmpty(part)) parts.Add(part);
                }
                return string.Join("\n", parts.ToArray());
            }

            return token.ToString();
        }

        private static JObject LoadVoiceJson(string path)
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                json = JsoncUtils.StripJsonComments(json);
                return JObject.Parse(json);
            }
            catch
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.Default);
                    json = JsoncUtils.StripJsonComments(json);
                    return JObject.Parse(json);
                }
                catch { return null; }
            }
        }

        private static JObject GetVoiceRoot(string voiceKey)
        {
            if (string.IsNullOrEmpty(voiceKey)) return null;

            // 先查路径缓存，命中则跳过一切磁盘探测
            string path;
            if (!s_voicePathCache.TryGetValue(voiceKey, out path) || string.IsNullOrEmpty(path))
            {
                path = ResolveVoiceFile(voiceKey);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                s_voicePathCache[voiceKey] = path;
            }

            return GetVoiceJsonByPath(path);
        }

        // 供外部（设置面板“随机测试”等）按路径复用同一份 JSON 缓存
        internal static JObject GetVoiceJsonByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            JObject cached;
            if (s_voiceJsonCache.TryGetValue(path, out cached))
                return cached;

            if (!File.Exists(path)) return null;

            JObject root = LoadVoiceJson(path);
            if (root != null)
                s_voiceJsonCache[path] = root;
            return root;
        }

        private static List<string> ExtractNetIds(JToken token)
        {
            List<string> ids = new List<string>();
            if (token == null) return ids;

            JObject obj = token as JObject;
            if (obj != null)
            {
                foreach (JProperty p in obj.Properties())
                {
                    string key = (p.Name ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(key) && !ListContainsIgnoreCase(ids, key))
                        ids.Add(key);
                }
                return ids;
            }

            JArray arr = token as JArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    JToken item = arr[i];
                    if (item == null) continue;

                    if (item is JValue)
                    {
                        string v = item.ToString();
                        if (!string.IsNullOrEmpty(v) && !ListContainsIgnoreCase(ids, v))
                            ids.Add(v);
                        continue;
                    }

                    JObject itemObj = item as JObject;
                    if (itemObj != null)
                    {
                        foreach (JProperty p in itemObj.Properties())
                        {
                            if (string.Equals(p.Name, "NetId", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
                            {
                                string v = p.Value != null ? p.Value.ToString() : string.Empty;
                                if (!string.IsNullOrEmpty(v) && !ListContainsIgnoreCase(ids, v))
                                    ids.Add(v);
                            }
                        }
                        continue;
                    }
                }
            }

            if (token is JValue)
            {
                string v = token.ToString();
                if (!string.IsNullOrEmpty(v) && !ListContainsIgnoreCase(ids, v))
                    ids.Add(v);
            }

            return ids;
        }

        private static void CacheVoiceNames(List<string> list)
        {
            if (list == null || list.Count == 0) return;
            if (list.Count <= s_cachedVoiceNames.Count) return;
            s_cachedVoiceNames.Clear();
            s_cachedVoiceNames.AddRange(list);
        }

        private static Dictionary<string, List<string>> CloneTriggerMap(Dictionary<string, List<string>> source)
        {
            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return result;
            foreach (KeyValuePair<string, List<string>> kv in source)
                result[kv.Key] = kv.Value != null ? new List<string>(kv.Value) : new List<string>();
            return result;
        }

        internal static string ResolveVoiceFile(string voiceKey)
        {
            if (string.IsNullOrEmpty(voiceKey)) return null;
            string name = voiceKey.Trim();

            try
            {
                if (Path.IsPathRooted(name) ||
                    name.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                    name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
                {
                    if (File.Exists(name)) return name;
                    if (!Path.HasExtension(name))
                    {
                        string jsonc = name + ".jsonc";
                        if (File.Exists(jsonc)) return jsonc;
                        string json = name + ".json";
                        if (File.Exists(json)) return json;
                    }
                }
            }
            catch { }

            List<string> dirs = GetVoiceSearchDirs();
            for (int d = 0; d < dirs.Count; d++)
            {
                string dir = dirs[d];
                if (!Directory.Exists(dir)) continue;
                string[] files = Directory.GetFiles(dir, name + ".*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]);
                    if (string.Equals(ext, ".jsonc", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
                        return files[i];
                }
            }

            return Path.Combine(GetVoicesDir(), name + ".jsonc");
        }

        private static string GetVoicesDir()
        {
            string baseDir = GetLocalesDir();
            if (s_cachedVoicesDir != null &&
                string.Equals(s_cachedVoicesDirBase, baseDir, StringComparison.OrdinalIgnoreCase))
                return s_cachedVoicesDir;

            string lower = Path.Combine(baseDir, "voices");
            string upper = Path.Combine(baseDir, "Voices");
            string resolved = Directory.Exists(lower) ? lower : (Directory.Exists(upper) ? upper : lower);
            s_cachedVoicesDir = resolved;
            s_cachedVoicesDirBase = baseDir;
            return resolved;
        }

        private static string GetVoicesDirForLocale(string localeDir)
        {
            string lower = Path.Combine(localeDir, "voices");
            string upper = Path.Combine(localeDir, "Voices");
            return Directory.Exists(lower) ? lower : (Directory.Exists(upper) ? upper : lower);
        }

        private static List<string> GetVoiceSearchDirs()
        {
            List<string> result = new List<string>();
            string current = GetVoicesDir();
            result.Add(current);
            string fallback = GetVoicesDirForLocale(DefaultLocalesDir);
            if (!string.Equals(current, fallback, StringComparison.OrdinalIgnoreCase))
                result.Add(fallback);
            return result;
        }

        internal static List<string> GetVoiceFiles()
        {
            List<string> result = new List<string>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> dirs = GetVoiceSearchDirs();
            for (int d = 0; d < dirs.Count; d++)
            {
                string dir = dirs[d];
                if (!Directory.Exists(dir)) continue;
                string[] files = Directory.GetFiles(dir, "*.json*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]);
                    if (!string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ext, ".jsonc", StringComparison.OrdinalIgnoreCase)) continue;
                    string name = Path.GetFileNameWithoutExtension(files[i]);
                    if (names.Add(name)) result.Add(files[i]);
                }
            }
            return result;
        }

        // 对外暴露解析好的目录（全项目唯一来源，供 Settings/PhraseSubtitle 复用）
        internal static string LocalesDir
        {
            get { return GetLocalesDir(); }
        }

        internal static string LocaleRootDir
        {
            get { return GetLocalesRootDir(); }
        }

        internal static string DefaultLocalesDir
        {
            get { return Path.Combine(GetLocalesRootDir(), I18n.DefaultLanguage); }
        }

        internal static string VoicesDir
        {
            get { return GetVoicesDir(); }
        }

        internal static string ResolveLocaleFile(string relativePath)
        {
            string current = Path.Combine(GetLocalesDir(), relativePath);
            if (File.Exists(current)) return current;
            string fallback = Path.Combine(DefaultLocalesDir, relativePath);
            if (File.Exists(fallback)) return fallback;
            return current;
        }

        private static PhraseFilterPreset ParsePreset(JObject root)
        {
            PhraseFilterPreset preset = new PhraseFilterPreset();
            if (root == null) return preset;
            preset.DefaultAllow = root.Value<bool?>("DefaultAllow") ?? true;

            JObject globalsObj = root["GlobalTriggers"] as JObject;
            if (globalsObj != null)
            {
                preset.GlobalTriggers = new Dictionary<string, TriggerFilter>(StringComparer.OrdinalIgnoreCase);
                foreach (JProperty gp in globalsObj.Properties())
                {
                    string trig = (gp.Name ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(trig)) continue;
                    TriggerFilter tf = new TriggerFilter();
                    JObject tObj = gp.Value as JObject;
                    if (tObj != null)
                    {
                        tf.Enabled = tObj.Value<bool?>("Enabled") ?? true;
                        tf.GeneralOnly = tObj.Value<bool?>("GeneralOnly") ?? false;
                        tf.DefaultAllow = tObj.Value<bool?>("DefaultAllow");

                        JObject netObj = tObj["NetIds"] as JObject;
                        if (netObj != null)
                        {
                            tf.NetIds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                            foreach (JProperty np in netObj.Properties())
                            {
                                string nk = (np.Name ?? string.Empty).Trim();
                                if (string.IsNullOrEmpty(nk)) continue;
                                bool nv = false;
                                try { nv = np.Value.Value<bool>(); } catch { }
                                tf.NetIds[nk] = nv;
                            }
                        }
                    }
                    JObject backupObj = tObj["NetIdsBackup"] as JObject;
                    if (backupObj != null)
                    {
                        tf.NetIdsBackup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                        foreach (JProperty np in backupObj.Properties())
                        {
                            string nk = (np.Name ?? string.Empty).Trim();
                            if (string.IsNullOrEmpty(nk)) continue;
                            bool nv = false;
                            try { nv = np.Value.Value<bool>(); } catch { }
                            tf.NetIdsBackup[nk] = nv;
                        }
                    }
                    preset.GlobalTriggers[trig] = tf;
                }
            }

            JObject voicesObj = root["Voices"] as JObject;
            if (voicesObj != null)
            {
                foreach (JProperty vp in voicesObj.Properties())
                {
                    string vk = (vp.Name ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(vk)) continue;

                    VoiceFilter vf = new VoiceFilter();
                    JObject vObj = vp.Value as JObject;
                    if (vObj != null)
                    {
                        vf.Enabled = vObj.Value<bool?>("Enabled") ?? true;
                        vf.DefaultAllow = vObj.Value<bool?>("DefaultAllow");

                        JObject triggersObj = vObj["Triggers"] as JObject;
                        if (triggersObj != null)
                        {
                            vf.Triggers = new Dictionary<string, TriggerFilter>(StringComparer.OrdinalIgnoreCase);
                            foreach (JProperty tp in triggersObj.Properties())
                            {
                                string trig = (tp.Name ?? string.Empty).Trim();
                                if (string.IsNullOrEmpty(trig)) continue;
                                TriggerFilter tf = new TriggerFilter();
                                JObject tObj = tp.Value as JObject;
                                if (tObj != null)
                                {
                                    tf.Enabled = tObj.Value<bool?>("Enabled") ?? true;
                                    tf.GeneralOnly = tObj.Value<bool?>("GeneralOnly") ?? false;
                                    tf.DefaultAllow = tObj.Value<bool?>("DefaultAllow");

                                    JObject netObj = tObj["NetIds"] as JObject;
                                    if (netObj != null)
                                    {
                                        tf.NetIds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                                        foreach (JProperty np in netObj.Properties())
                                        {
                                            string nk = (np.Name ?? string.Empty).Trim();
                                            if (string.IsNullOrEmpty(nk)) continue;
                                            bool nv = false;
                                            try { nv = np.Value.Value<bool>(); } catch { }
                                            tf.NetIds[nk] = nv;
                                        }
                                    }
                                }
                                JObject backupObj = tObj["NetIdsBackup"] as JObject;
                                if (backupObj != null)
                                {
                                    tf.NetIdsBackup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                                    foreach (JProperty np in backupObj.Properties())
                                    {
                                        string nk = (np.Name ?? string.Empty).Trim();
                                        if (string.IsNullOrEmpty(nk)) continue;
                                        bool nv = false;
                                        try { nv = np.Value.Value<bool>(); } catch { }
                                        tf.NetIdsBackup[nk] = nv;
                                    }
                                }
                                vf.Triggers[trig] = tf;
                            }
                        }
                    }
                    preset.Voices[vk] = vf;
                }
            }
            return preset;
        }

        private static JObject WritePreset(PhraseFilterPreset preset)
        {
            JObject root = new JObject();
            if (!preset.DefaultAllow)
                root["DefaultAllow"] = false;

            JObject globalsObj = new JObject();
            if (preset.GlobalTriggers != null)
            {
                foreach (KeyValuePair<string, TriggerFilter> tk in preset.GlobalTriggers)
                {
                    if (string.IsNullOrEmpty(tk.Key) || tk.Value == null) continue;
                    JObject tObj = new JObject();
                    bool triggerHasData = false;
                    if (!tk.Value.Enabled)
                    {
                        tObj["Enabled"] = false;
                        triggerHasData = true;
                    }
                    if (tk.Value.GeneralOnly)
                    {
                        tObj["GeneralOnly"] = true;
                        triggerHasData = true;
                    }

                    JObject netObj = new JObject();
                    if (tk.Value.NetIds != null)
                    {
                        foreach (KeyValuePair<string, bool> nk in tk.Value.NetIds)
                        {
                            if (string.IsNullOrEmpty(nk.Key)) continue;
                            if (!nk.Value)
                            {
                                netObj[nk.Key] = false;
                                triggerHasData = true;
                            }
                        }
                    }
                    JObject backupObj = new JObject();
                    if (tk.Value.NetIdsBackup != null)
                    {
                        foreach (KeyValuePair<string, bool> nk in tk.Value.NetIdsBackup)
                        {
                            if (string.IsNullOrEmpty(nk.Key)) continue;
                            if (!nk.Value)
                            {
                                backupObj[nk.Key] = false;
                                triggerHasData = true;
                            }
                        }
                    }
                    if (netObj.Count > 0) tObj["NetIds"] = netObj;
                    if (backupObj.Count > 0) tObj["NetIdsBackup"] = backupObj;
                    if (triggerHasData) globalsObj[tk.Key] = tObj;
                }
            }
            if (globalsObj.Count > 0) root["GlobalTriggers"] = globalsObj;

            JObject voicesObj = new JObject();
            foreach (KeyValuePair<string, VoiceFilter> kv in preset.Voices)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                JObject vObj = new JObject();
                bool voiceHasData = false;
                if (!kv.Value.Enabled)
                {
                    vObj["Enabled"] = false;
                    voiceHasData = true;
                }

                JObject triggersObj = new JObject();
                if (kv.Value.Triggers != null)
                {
                    foreach (KeyValuePair<string, TriggerFilter> tk in kv.Value.Triggers)
                    {
                        if (string.IsNullOrEmpty(tk.Key) || tk.Value == null) continue;
                        JObject tObj = new JObject();
                        bool triggerHasData = false;
                        if (!tk.Value.Enabled)
                        {
                            tObj["Enabled"] = false;
                            triggerHasData = true;
                        }
                        if (tk.Value.GeneralOnly)
                        {
                            tObj["GeneralOnly"] = true;
                            triggerHasData = true;
                        }

                        JObject netObj = new JObject();
                        if (tk.Value.NetIds != null)
                        {
                            foreach (KeyValuePair<string, bool> nk in tk.Value.NetIds)
                            {
                                if (string.IsNullOrEmpty(nk.Key)) continue;
                                if (!nk.Value)
                                {
                                    netObj[nk.Key] = false;
                                    triggerHasData = true;
                                }
                            }
                        }
                        JObject backupObj = new JObject();
                        if (tk.Value.NetIdsBackup != null)
                        {
                            foreach (KeyValuePair<string, bool> nk in tk.Value.NetIdsBackup)
                            {
                                if (string.IsNullOrEmpty(nk.Key)) continue;
                                if (!nk.Value)
                                {
                                    backupObj[nk.Key] = false;
                                    triggerHasData = true;
                                }
                            }
                        }
                        if (netObj.Count > 0) tObj["NetIds"] = netObj;
                        if (backupObj.Count > 0) tObj["NetIdsBackup"] = backupObj;
                        if (triggerHasData) triggersObj[tk.Key] = tObj;
                    }
                }
                if (triggersObj.Count > 0)
                {
                    vObj["Triggers"] = triggersObj;
                    voiceHasData = true;
                }
                if (voiceHasData) voicesObj[kv.Key] = vObj;
            }

            if (voicesObj.Count > 0) root["Voices"] = voicesObj;
            return root;
        }

        private static string GetLocalesDir()
        {
            string language = GetCurrentLanguage();
            if (!string.IsNullOrEmpty(s_cachedLocalesDir) &&
                string.Equals(s_cachedLocaleLanguage, language, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(s_cachedLocalesDir))
                return s_cachedLocalesDir;

            string root = GetLocalesRootDir();
            string requested = Path.Combine(root, language);
            string fallback = Path.Combine(root, I18n.DefaultLanguage);
            s_cachedLocalesDir = Directory.Exists(requested) ? requested : fallback;
            s_cachedLocaleLanguage = language;
            s_cachedVoicesDir = null;
            s_cachedVoicesDirBase = null;
            return s_cachedLocalesDir;
        }

        private static string GetCurrentLanguage()
        {
            try
            {
                if (Settings.UiLanguage != null && !string.IsNullOrWhiteSpace(Settings.UiLanguage.Value))
                    return Settings.UiLanguage.Value.Trim();
            }
            catch { }
            return I18n.DefaultLanguage;
        }

        private static string GetLocalesRootDir()
        {
            if (!string.IsNullOrEmpty(s_cachedLocalesRootDir) && Directory.Exists(s_cachedLocalesRootDir))
                return s_cachedLocalesRootDir;

            s_cachedLocalesRootDir = ResolveLocalesRootDir();
            return s_cachedLocalesRootDir;
        }

        private static string ResolveLocalesRootDir()
        {
            List<string> candidates = new List<string>();
            string pluginPath = BepInEx.Paths.PluginPath;
            if (!string.IsNullOrEmpty(pluginPath))
            {
                candidates.Add(Path.Combine(pluginPath, "subtitle", "locales"));

                string trimmed = pluginPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string tail = Path.GetFileName(trimmed);
                if (string.Equals(tail, "subtitle", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(Path.Combine(pluginPath, "locales"));
            }

            string bepinexRoot = BepInEx.Paths.BepInExRootPath;
            if (!string.IsNullOrEmpty(bepinexRoot))
                candidates.Add(Path.Combine(bepinexRoot, "plugins", "subtitle", "locales"));

            string gameRoot = BepInEx.Paths.GameRootPath;
            if (!string.IsNullOrEmpty(gameRoot))
                candidates.Add(Path.Combine(gameRoot, "BepInEx", "plugins", "subtitle", "locales"));

            string asmPath = typeof(PhraseFilterManager).Assembly.Location;
            if (!string.IsNullOrEmpty(asmPath))
            {
                string asmDir = Path.GetDirectoryName(asmPath);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    candidates.Add(Path.Combine(asmDir, "locales"));
                    DirectoryInfo parent = Directory.GetParent(asmDir);
                    if (parent != null)
                        candidates.Add(Path.Combine(parent.FullName, "subtitle", "locales"));
                }
            }

            candidates.Add(Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "locales"));

            string existing = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                string root = candidates[i];
                if (!Directory.Exists(root)) continue;
                if (existing == null) existing = root;

                string dir = Path.Combine(root, I18n.DefaultLanguage);
                string voicesLower = Path.Combine(dir, "voices");
                string voicesUpper = Path.Combine(dir, "Voices");
                if (Directory.Exists(voicesLower) || Directory.Exists(voicesUpper))
                    return root;
            }

            return existing ?? (candidates.Count > 0 ? candidates[0] : Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "locales"));
        }

        internal static void InvalidateLocaleCaches()
        {
            s_cachedLocalesDir = null;
            s_cachedLocaleLanguage = null;
            s_cachedVoicesDir = null;
            s_cachedVoicesDirBase = null;
            s_cachedVoiceNames.Clear();
            s_cachedVoiceMap.Clear();
            s_voiceJsonCache.Clear();
            s_voicePathCache.Clear();
        }

        private static string GetPresetPath(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                try
                {
                    if (Path.IsPathRooted(name)) return name;
                    if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
                        return name;
                }
                catch { }
            }

            string dir = Path.GetDirectoryName(GetLocalesRootDir());
            string normalized = NormalizePresetName(name);
            string fileName = string.Equals(normalized, DefaultPresetName, StringComparison.OrdinalIgnoreCase)
                ? PresetFileName
                : normalized + PresetFileExtension;
            return Path.Combine(dir, fileName);
        }

        private static string GetLegacyPresetPath(string name)
        {
            string normalized = NormalizePresetName(name);
            string fileName = string.Equals(normalized, DefaultPresetName, StringComparison.OrdinalIgnoreCase)
                ? PresetFileName
                : normalized + PresetFileExtension;
            return Path.Combine(DefaultLocalesDir, fileName);
        }

        private static string NormalizePresetName(string name)
        {
            if (string.IsNullOrEmpty(name)) return DefaultPresetName;
            name = name.Trim();
            if (name.IndexOf(Path.DirectorySeparatorChar) >= 0 || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
                name = Path.GetFileName(name);
            if (name.EndsWith(PresetFileExtension, StringComparison.OrdinalIgnoreCase))
                name = Path.GetFileNameWithoutExtension(name);
            if (string.IsNullOrEmpty(name)) return DefaultPresetName;
            return name;
        }

        private static void EnsureDefaultIgnoredTriggers(PhraseFilterPreset preset)
        {
            if (preset == null) return;
            if (preset.GlobalTriggers == null)
                preset.GlobalTriggers = new Dictionary<string, TriggerFilter>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < s_defaultIgnoredTriggers.Length; i++)
            {
                string key = s_defaultIgnoredTriggers[i];
                if (string.IsNullOrEmpty(key)) continue;
                TriggerFilter tf;
                if (!preset.GlobalTriggers.TryGetValue(key, out tf) || tf == null)
                {
                    tf = new TriggerFilter { Enabled = false };
                    preset.GlobalTriggers[key] = tf;
                }
            }
        }

        private static bool IsDefaultDisabledTrigger(string trigger)
        {
            if (string.IsNullOrEmpty(trigger)) return false;
            for (int i = 0; i < s_defaultIgnoredTriggers.Length; i++)
            {
                if (string.Equals(s_defaultIgnoredTriggers[i], trigger, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool ListContainsIgnoreCase(List<string> list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    internal sealed class PhraseFilterPreset
    {
        public bool DefaultAllow = true;
        public Dictionary<string, TriggerFilter> GlobalTriggers = new Dictionary<string, TriggerFilter>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, VoiceFilter> Voices = new Dictionary<string, VoiceFilter>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class VoiceFilter
    {
        public bool Enabled = true;
        public bool? DefaultAllow;
        public Dictionary<string, TriggerFilter> Triggers = new Dictionary<string, TriggerFilter>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class TriggerFilter
    {
        public bool Enabled = true;
        public bool? DefaultAllow;
        public bool GeneralOnly = false;
        public Dictionary<string, bool> NetIds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> NetIdsBackup;
    }
}
