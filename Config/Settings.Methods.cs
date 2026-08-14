// 文件：Subtitle/Config/Settings.Methods.cs
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using Newtonsoft.Json.Linq;
using Subtitle.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Subtitle.Config
{
    internal partial class Settings
    {
        private static List<string> s_FontBundleNames = new List<string>();
        private static bool s_FontBundleListLoaded = false;
        private static readonly Dictionary<ConfigEntryBase, int> s_FontBundleSelection =
            new Dictionary<ConfigEntryBase, int>();
        private static readonly Dictionary<ConfigEntryBase, string> s_FontBundleSelectionValue =
            new Dictionary<ConfigEntryBase, string>();
        private const string DefaultTextPresetName = "default";
        private const string DefaultTextPresetBaseName = "塔科夫战术";
        private const string FontReplacePluginGuid = "hiddenhiragi.Volcano.fontreplace";

        // —— 批量应用预设时的刷新抑制：批量期间只记录受影响子系统，结束后每个子系统只刷新一次 ——
        private static bool s_BatchApplying;
        private static readonly List<Action> s_BatchPendingRefreshes = new List<Action>();

        // —— ConfigurationManager 反射缓存：折叠开关每次都会触发刷新，避免每次都全程序集扫描 ——
        private static Type s_CmType;
        private static object s_CmInstance;
        private static MethodInfo s_CmRefreshMethod;
        private static object[] s_CmRefreshArgs;

        // ConfigEntryBase.Description 的私有 BackingField（启动时上百次调用，只反射一次）
        private static readonly FieldInfo s_ConfigEntryDescriptionField =
            typeof(ConfigEntryBase).GetField("<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        // ① 共享的写入/读取动作表（由 Setting.cs Init 里的 Reg 在 Bind 时统一注册填充）
        private static readonly List<Action<JObject>> s_SnapshotWriters = new List<Action<JObject>>();
        private static readonly List<Action<JObject>> s_SnapshotReaders = new List<Action<JObject>>();

        // ② 取 key（大小写不敏感）
        private static JToken PickKey(JObject o, string key)
        {
            if (o == null || string.IsNullOrEmpty(key)) return null;
            JToken tok = null;
            o.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out tok);
            return tok;
        }

        // ③ 解析 bool
        private static bool ToBool(JToken t)
        {
            try
            {
                if (t == null) return false;
                if (t.Type == JTokenType.Boolean) return t.Value<bool>();
                if (t.Type == JTokenType.Integer) return t.Value<int>() != 0;
                return string.Equals(t.Value<string>(), "true", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ④ 注册器（按类型）
        private static void RegBool(string key, BepInEx.Configuration.ConfigEntry<bool> e)
        {
            if (e == null) return;
            s_SnapshotWriters.Add(S => { S[key] = e.Value; });
            s_SnapshotReaders.Add(S => { var t = PickKey(S, key); if (t != null) e.Value = ToBool(t); });
        }
        private static void RegInt(string key, BepInEx.Configuration.ConfigEntry<int> e)
        {
            if (e == null) return;
            s_SnapshotWriters.Add(S => { S[key] = e.Value; });
            s_SnapshotReaders.Add(S => { var t = PickKey(S, key); if (t != null) try { e.Value = t.Value<int>(); } catch { } });
        }
        private static void RegFloat(string key, BepInEx.Configuration.ConfigEntry<float> e)
        {
            if (e == null) return;
            s_SnapshotWriters.Add(S => { S[key] = (double)e.Value; });
            s_SnapshotReaders.Add(S => { var t = PickKey(S, key); if (t != null) try { e.Value = (float)t.Value<double>(); } catch { } });
        }
        private static void RegStr(string key, BepInEx.Configuration.ConfigEntry<string> e)
        {
            if (e == null) return;
            s_SnapshotWriters.Add(S => { S[key] = e.Value ?? ""; });
            s_SnapshotReaders.Add(S => { var t = PickKey(S, key); if (t != null) try { e.Value = t.Value<string>(); } catch { } });
        }
        private static void RegCsv(string key, BepInEx.Configuration.ConfigEntry<string> e)
        {
            if (e == null) return;
            s_SnapshotWriters.Add(S =>
            {
                var csv = e.Value ?? "";
                var arr = new JArray();
                if (!string.IsNullOrEmpty(csv))
                {
                    var parts = csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var s = parts[i].Trim();
                        if (!string.IsNullOrEmpty(s)) arr.Add(s);
                    }
                }
                S[key] = arr;
            });
            s_SnapshotReaders.Add(S =>
            {
                var t = PickKey(S, key);
                if (t == null) return;
                try
                {
                    if (t.Type == JTokenType.Array)
                    {
                        var sb = new StringBuilder();
                        foreach (var it in (JArray)t)
                        {
                            var s = it != null ? it.ToString() : null;
                            if (string.IsNullOrEmpty(s)) continue;
                            if (sb.Length > 0) sb.Append(", ");
                            sb.Append(s);
                        }
                        e.Value = sb.ToString();
                    }
                    else if (t.Type == JTokenType.String)
                    {
                        e.Value = t.Value<string>();
                    }
                }
                catch { }
            });
        }
        private static void RegColor(string key, BepInEx.Configuration.ConfigEntry<Color> e)
        {
            if (e == null) return;
            s_SnapshotWriters.Add(S =>
            {
                var c = e.Value;
                S[key] = new JArray((double)c.r, (double)c.g, (double)c.b, (double)c.a);
            });
            s_SnapshotReaders.Add(S =>
            {
                var t = PickKey(S, key);
                if (t == null) return;
                try
                {
                    // 支持 "#RRGGBB[AA]" 或 [r,g,b,a]
                    if (t.Type == JTokenType.String)
                    {
                        Color c;
                        if (SubtitleSystem.ColorUtil.TryParseColor(t.Value<string>(), out c)) { e.Value = c; return; }
                    }
                    else if (t is JArray arr && (arr.Count == 3 || arr.Count == 4))
                    {
                        float r = (float)arr[0].Value<double>();
                        float g = (float)arr[1].Value<double>();
                        float b = (float)arr[2].Value<double>();
                        float a = arr.Count == 4 ? (float)arr[3].Value<double>() : 1f;
                        if (r > 1 || g > 1 || b > 1 || a > 1) { r /= 255f; g /= 255f; b /= 255f; a /= 255f; }
                        e.Value = new Color(r, g, b, a);
                    }
                }
                catch { }
            });
        }

        // ⑤ 统一快照注册入口：由 Setting.cs 的 Reg 在 Bind 时调用，按 T 的实际类型分发到上面的注册器
        // enum 类型在这里内联处理（存枚举名，读取时忽略大小写 TryParse，行为与原 RegEnum 一致）
        private static void RegSnapshot<T>(string key, BepInEx.Configuration.ConfigEntry<T> e, bool csv)
        {
            if (e == null || string.IsNullOrEmpty(key)) return;
            var t = typeof(T);
            if (t == typeof(bool)) { RegBool(key, (BepInEx.Configuration.ConfigEntry<bool>)(object)e); return; }
            if (t == typeof(int)) { RegInt(key, (BepInEx.Configuration.ConfigEntry<int>)(object)e); return; }
            if (t == typeof(float)) { RegFloat(key, (BepInEx.Configuration.ConfigEntry<float>)(object)e); return; }
            if (t == typeof(string))
            {
                if (csv) RegCsv(key, (BepInEx.Configuration.ConfigEntry<string>)(object)e);
                else RegStr(key, (BepInEx.Configuration.ConfigEntry<string>)(object)e);
                return;
            }
            if (t == typeof(Color)) { RegColor(key, (BepInEx.Configuration.ConfigEntry<Color>)(object)e); return; }
            if (t.IsEnum)
            {
                s_SnapshotWriters.Add(S => { S[key] = e.Value.ToString(); });
                s_SnapshotReaders.Add(S =>
                {
                    var tok = PickKey(S, key);
                    if (tok == null) return;
                    try
                    {
                        var s = tok.Value<string>();
                        // net471 没有忽略大小写的 Enum.TryParse(Type,…) 重载，用 Parse + try/catch 等价实现
                        if (!string.IsNullOrEmpty(s)) e.Value = (T)Enum.Parse(t, s, true);
                    }
                    catch { }
                });
            }
        }

        private static void PushClientToast(string text)
        {
            try
            {
                var mgr = Subtitle.Plugin.Instance != null
                    ? Subtitle.Plugin.Instance.GetOrCreateSubtitleManagerAnyScene()
                    : null;
                if (mgr == null) return;

                // 优先用弹幕做“Toast”，否则用 2.5 秒字幕
                if (EnableDanmaku != null && EnableDanmaku.Value)
                {
                    // 柔和一点的提示色
                    mgr.AddDanmaku(text, new Color(0.9f, 0.95f, 1f, 1f));
                }
                else
                {
                    mgr.AddSubtitle(text, new Color(0.9f, 0.95f, 1f, 1f), 2.5f);
                }
            }
            catch { }
        }

        internal static void TryApplySubtitleLayoutRuntime()
        {
            try
            {
                var mgr = SubtitleSystem.SubtitleManager.Instance;
                if (mgr != null) mgr.ApplySubtitleLayoutSettings();
            }
            catch { }
        }

        internal static void TryApplyDanmakuRuntime()
        {
            try
            {
                var mgr = SubtitleSystem.SubtitleManager.Instance;
                if (mgr != null) mgr.ApplyDanmakuSettings();
            }
            catch { }
        }

        internal static void TryRefreshSubtitleStyleRuntime()
        {
            try
            {
                var mgr = SubtitleSystem.SubtitleManager.Instance;
                if (mgr != null) mgr.RefreshSubtitleStyles();
            }
            catch { }
        }

        internal static void TryRefreshDanmakuStyleRuntime()
        {
            try
            {
                var mgr = SubtitleSystem.SubtitleManager.Instance;
                if (mgr != null) mgr.RefreshDanmakuStyles();
            }
            catch { }
        }

        internal static void TryRefreshWorld3DStyleRuntime()
        {
            try
            {
                var mgr = SubtitleSystem.SubtitleManager.Instance;
                if (mgr != null) mgr.RefreshWorld3DStyles();
            }
            catch { }
        }

        private static bool TryGetTextAnchor(Settings.TextAnchorOption opt, out TextAnchor anchor)
        {
            anchor = TextAnchor.UpperLeft;
            if (opt == Settings.TextAnchorOption.None) return false;
            anchor = (TextAnchor)opt;
            return true;
        }

        // ===================== 预设应用/扫描 =====================
        private static void ApplyPresetByName(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(s_PresetsDir))
                    s_PresetsDir = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "presets");

                var fallbackPreset = DefaultTextPresetName;
                var pick = string.IsNullOrWhiteSpace(name) ? fallbackPreset : name.Trim();
                var path = GetTextPresetPath(pick);
                if (!File.Exists(path))
                {
                    s_Log.LogWarning("[Settings] Preset file not found: " + path + ", fallback to " + fallbackPreset + ".");
                    pick = fallbackPreset;
                    path = GetTextPresetPath(fallbackPreset);
                }

                var preset = SubtitleSystem.SubtitleTextPreset.LoadFromFile(path);

                // 从预设 Setting 写回 cfg
                ApplySettingsOverrideFromPreset(preset);

                // 把生效的名字写回 cfg（保存）
                if (TextPresetName != null) TextPresetName.Value = pick;
                if (Config != null) Config.Save();

                // 让弹幕层复位（按你原有逻辑）
                var mgr = Subtitle.Plugin.Instance != null ? Subtitle.Plugin.Instance.GetOrCreateSubtitleManagerAnyScene() : null;
                if (mgr != null)
                {
                    mgr.ApplyDanmakuSettings();
                    mgr.InitializeDanmakuLayer();
                }

                s_Log.LogInfo("[Settings] Preset applied: " + pick);
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Settings] ApplyPresetByName failed: " + e);
            }
        }

        private static void ScanPresets(bool resetSelectionToCurrent)
        {
            try
            {
                if (string.IsNullOrEmpty(s_PresetsDir))
                    s_PresetsDir = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "presets");

                var list = new List<string>();
                if (Directory.Exists(s_PresetsDir))
                {
                    var files = Directory.GetFiles(s_PresetsDir, "*.jsonc", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < files.Length; i++)
                    {
                        var n = Path.GetFileNameWithoutExtension(files[i]);
                        if (!string.IsNullOrEmpty(n) && !list.Contains(n, StringComparer.OrdinalIgnoreCase))
                            list.Add(n);
                    }
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);
                if (!list.Exists(n => string.Equals(n, DefaultTextPresetName, StringComparison.OrdinalIgnoreCase)))
                    list.Insert(0, DefaultTextPresetName);

                s_PresetNames = list;
                s_PresetListLoaded = true;

                if (resetSelectionToCurrent)
                {
                    var cur = TextPresetName != null ? (TextPresetName.Value ?? DefaultTextPresetName) : DefaultTextPresetName;
                    int idx = s_PresetNames.FindIndex(n => string.Equals(n, cur, StringComparison.OrdinalIgnoreCase));
                    s_SelectedPresetIndex = idx >= 0 ? idx : 0;
                }
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Settings] ScanPresets failed: " + e);
                s_PresetNames = new List<string> { DefaultTextPresetName };
                s_SelectedPresetIndex = 0;
                s_PresetListLoaded = true;
            }
        }

        private static void RecalcOrder()
        {
            // 防御：字段本身为空就直接返回
            if (ConfigEntries == null) return;

            int order = ConfigEntries.Count;
            for (int i = 0; i < ConfigEntries.Count; i++)
            {
                var entry = ConfigEntries[i];
                if (entry == null) { order--; continue; }   // 防御：列表里混入了 null

                var desc = entry.Description;
                if (desc == null || desc.Tags == null || desc.Tags.Length == 0)
                {
                    order--;
                    continue; // 没有 CM 的 Attributes 就跳过，不要崩
                }

                var attrs = desc.Tags[0] as ConfigurationManagerAttributes;
                if (attrs != null) attrs.Order = order;
                order--;
            }
        }

        // ===================== F12 精简：只保留两个入口 =====================
        // 设置项已全部收进图形化设置界面，ConfigurationManager(F12) 只显示「图形化设置界面」按钮和热键。
        private static void ApplySlimConfigurationManagerVisibility()
        {
            if (ConfigEntries == null) return;
            for (int i = 0; i < ConfigEntries.Count; i++)
            {
                var entry = ConfigEntries[i];
                if (entry == null) continue;

                var attrs = GetCmAttributes(entry);
                if (attrs == null) continue;

                bool keep = ReferenceEquals(entry, SettingsWindowButton) ||
                            ReferenceEquals(entry, SettingsWindowHotkey);
                attrs.Browsable = keep;
            }

            // Browsable 变更需要 CM 重建设置列表才生效（沿用原折叠机制的刷新路径）
            TryRefreshConfigurationManager();
        }

        private static void ApplySlimConfigurationManagerLocalization()
        {
            string pluginTitle = string.Equals(I18n.CurrentLanguage, "ch", StringComparison.OrdinalIgnoreCase)
                ? "Volcano-Subtitle 火山家的实时字幕"
                : "Volcano-subtitle";
            string category = I18n.Category(GeneralSection, "通用");

            // ConfigurationManager 的插件标题读取 BepInEx 运行时元数据，而不是配置项 DispName。
            // 在语言切换后更新 Metadata.Name 并重建列表，F12 标题即可同步变化。
            try
            {
                if (Chainloader.PluginInfos != null &&
                    Chainloader.PluginInfos.ContainsKey("Volcano.Subtitle"))
                {
                    var info = Chainloader.PluginInfos["Volcano.Subtitle"];
                    if (info != null && info.Metadata != null)
                    {
                        var nameProperty = info.Metadata.GetType().GetProperty("Name",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var setter = nameProperty != null ? nameProperty.GetSetMethod(true) : null;
                        if (setter != null) setter.Invoke(info.Metadata, new object[] { pluginTitle });
                        else
                        {
                            var nameField = info.Metadata.GetType().GetField("<Name>k__BackingField",
                                BindingFlags.Instance | BindingFlags.NonPublic);
                            if (nameField != null) nameField.SetValue(info.Metadata, pluginTitle);
                        }
                    }
                }
            }
            catch { }

            var buttonAttrs = GetCmAttributes(SettingsWindowButton);
            if (buttonAttrs != null) buttonAttrs.Category = category;
            var hotkeyAttrs = GetCmAttributes(SettingsWindowHotkey);
            if (hotkeyAttrs != null)
            {
                hotkeyAttrs.Category = category;
                hotkeyAttrs.DispName = I18n.Text("F12.SettingsHotkey.Name", "设置界面 打开热键");
                hotkeyAttrs.Description = I18n.Text("F12.SettingsHotkey.Desc",
                    "打开/关闭 图形化设置界面 的热键（默认 F9）。也可点界面右上角“关闭”退出。");
            }
            TryRefreshConfigurationManager();
        }

        private static ConfigurationManagerAttributes GetCmAttributes(ConfigEntryBase entry)
        {
            if (entry == null) return null;
            var desc = entry.Description;
            if (desc == null || desc.Tags == null || desc.Tags.Length == 0) return null;

            for (int i = 0; i < desc.Tags.Length; i++)
            {
                var attrs = desc.Tags[i] as ConfigurationManagerAttributes;
                if (attrs != null) return attrs;
            }
            return null;
        }

        private static void EnsureConfigurationManagerAttributes(IEnumerable<ConfigEntryBase> entries)
        {
            if (entries == null) return;
            foreach (var entry in entries)
                EnsureConfigurationManagerAttributes(entry);
        }

        private static void EnsureConfigurationManagerAttributes(ConfigEntryBase entry)
        {
            if (entry == null) return;
            var desc = entry.Description;
            if (desc == null) return;

            var tags = desc.Tags;
            if (tags != null)
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    if (tags[i] is ConfigurationManagerAttributes)
                        return;
                }
            }

            var list = new List<object>();
            if (tags != null && tags.Length > 0) list.AddRange(tags);
            list.Add(new ConfigurationManagerAttributes());

            var newDesc = new ConfigDescription(desc.Description, desc.AcceptableValues, list.ToArray());
            try
            {
                // 字段引用已提升为静态只读（启动时上百个条目只反射一次）
                if (s_ConfigEntryDescriptionField != null)
                    s_ConfigEntryDescriptionField.SetValue(entry, newDesc);
            }
            catch { }
        }

        private static void TryRefreshConfigurationManager()
        {
            try
            {
                // 快路径：已解析出 实例+方法，直接调用（调用失败/对象销毁时清缓存重解析）
                if (s_CmRefreshMethod != null && s_CmInstance != null)
                {
                    var asUnityObj = s_CmInstance as UnityEngine.Object;
                    bool destroyed = !ReferenceEquals(asUnityObj, null) && asUnityObj == null;
                    if (!destroyed)
                    {
                        try
                        {
                            s_CmRefreshMethod.Invoke(s_CmInstance, s_CmRefreshArgs);
                            return;
                        }
                        catch { }
                    }
                    InvalidateConfigurationManagerCache();
                }

                ResolveConfigurationManagerRefresh();
            }
            catch { }
        }

        private static void InvalidateConfigurationManagerCache()
        {
            s_CmInstance = null;
            s_CmRefreshMethod = null;
            s_CmRefreshArgs = null;
            // 注意：s_CmType 保留——类型不会消失，避免再次全程序集扫描
        }

        private static void ResolveConfigurationManagerRefresh()
        {
            Type cmType = FindConfigurationManagerType();
            if (cmType == null) return;

            object instance = null;
            var instanceProp = cmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceProp != null)
                instance = instanceProp.GetValue(null, null);

            if (instance == null)
            {
                var objs = UnityEngine.Object.FindObjectsOfType(cmType);
                if (objs != null && objs.Length > 0)
                    instance = objs[0];
            }

            if (instance == null)
            {
                var all = Resources.FindObjectsOfTypeAll(cmType);
                if (all != null && all.Length > 0)
                    instance = all[0];
            }

            if (instance == null) return;

            string[] methods = {
                "BuildSettingList",
                "RefreshSettingList",
                "UpdateSettingList",
                "SettingListChanged",
                "OnSettingsChanged",
                "OnSettingChanged",
                "Reload"
            };
            for (int i = 0; i < methods.Length; i++)
            {
                var method = cmType.GetMethod(methods[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) continue;
                var pars = method.GetParameters();
                object[] args = null;
                if (pars.Length == 0)
                    args = null;
                else if (pars.Length == 1 && pars[0].ParameterType == typeof(bool))
                    args = new object[] { true };
                else
                    continue;

                // 解析成功：缓存 实例+方法+参数，之后每次折叠开关都走快路径
                s_CmInstance = instance;
                s_CmRefreshMethod = method;
                s_CmRefreshArgs = args;
                method.Invoke(instance, args);
                return;
            }
        }

        private static Type FindConfigurationManagerType()
        {
            if (s_CmType != null) return s_CmType;

            Type cmType = Type.GetType("ConfigurationManager.ConfigurationManager, ConfigurationManager");
            if (cmType != null) { s_CmType = cmType; return cmType; }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    var asm = assemblies[i];
                    cmType = asm.GetType("ConfigurationManager.ConfigurationManager");
                    if (cmType != null) { s_CmType = cmType; return cmType; }

                    var types = asm.GetTypes();
                    for (int t = 0; t < types.Length; t++)
                    {
                        var type = types[t];
                        if (type == null) continue;
                        if (!string.Equals(type.Name, "ConfigurationManager", StringComparison.Ordinal)) continue;
                        if (type.GetMethod("BuildSettingList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null ||
                            type.GetMethod("RefreshSettingList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null ||
                            type.GetMethod("UpdateSettingList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
                        {
                            s_CmType = type;
                            return type;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        // ===================== 图形化设置界面（UGUI）复用入口 =====================
        // 图形化设置界面复用的扫描、选择、应用与测试发送入口。

        // —— 预设选择器 ——
        internal static List<string> GetPresetNames()
        {
            if (!s_PresetListLoaded) ScanPresets(true);
            return s_PresetNames;
        }

        internal static int GetSelectedPresetIndex()
        {
            return s_SelectedPresetIndex;
        }

        // 选择越界时自动回绕
        internal static void SetSelectedPresetIndex(int index)
        {
            int count = s_PresetNames != null ? s_PresetNames.Count : 0;
            if (count > 0)
            {
                if (index < 0) index = count - 1;
                if (index >= count) index = 0;
            }
            s_SelectedPresetIndex = index;
        }

        // 把选择索引回同步到 cfg 当前值（GUI 每次构建选择行时调用，保证显示反映 TextPresetName.Value）
        internal static void SyncPresetSelectionToCurrent()
        {
            if (!s_PresetListLoaded) { ScanPresets(true); return; }
            var cur = TextPresetName != null ? (TextPresetName.Value ?? DefaultTextPresetName) : DefaultTextPresetName;
            int idx = s_PresetNames.FindIndex(n => string.Equals(n, cur, StringComparison.OrdinalIgnoreCase));
            s_SelectedPresetIndex = idx >= 0 ? idx : 0;
        }

        private static string GetTextPresetPath(string name)
        {
            if (string.IsNullOrEmpty(s_PresetsDir))
                s_PresetsDir = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "presets");
            string fileName = string.Equals(name, DefaultTextPresetName, StringComparison.OrdinalIgnoreCase)
                ? DefaultTextPresetBaseName
                : name;
            return Path.Combine(s_PresetsDir, fileName + ".jsonc");
        }

        internal static int RefreshPresetList()
        {
            ScanPresets(true);
            s_Log.LogInfo("[Settings] Preset list refreshed. Count=" + s_PresetNames.Count);
            PushClientToast($"已刷新预设：{s_PresetNames.Count} 个");
            return s_PresetNames.Count;
        }

        // 应用当前选中的预设，返回生效的预设名（无可选项时返回 null）
        internal static string ApplySelectedPreset()
        {
            if (s_PresetNames == null || s_PresetNames.Count == 0) return null;
            int idx = s_SelectedPresetIndex;
            if (idx < 0 || idx >= s_PresetNames.Count) idx = 0;
            var pick = s_PresetNames[idx];
            ApplyPresetByName(pick);
            PushClientToast($"已应用预设：{pick}");
            return pick;
        }

        // —— 字体包选择器（三个渠道共用一份扫描结果，选择索引按 entry 分别记忆） ——
        internal static List<string> GetFontBundleNames(ConfigEntry<string> e)
        {
            if (!s_FontBundleListLoaded) ScanFontBundles();
            return s_FontBundleNames;
        }

        internal static int GetFontBundleSelection(ConfigEntryBase entry, string currentValue)
        {
            return EnsureFontBundleSelection(entry, currentValue);
        }

        internal static void SetFontBundleSelection(ConfigEntryBase entry, int index, string currentValue)
        {
            UpdateFontBundleSelection(entry, index, currentValue);
        }

        internal static int RefreshFontBundles(ConfigEntry<string> e)
        {
            ScanFontBundles();
            // 定向失效资源包字体缓存并重刷本渠道样式：
            // 局内替换同名字体文件后点「刷新」即可立即生效，无需重开战局
            SubtitleSystem.SubtitleFontLoader.InvalidateBundleFontCache();
            TryRefreshByFontBundleEntry(e);
            return s_FontBundleNames != null ? s_FontBundleNames.Count : 0;
        }

        // 把当前选择写入 entry 并触发对应渠道的运行期样式刷新；返回应用后的显示名
        internal static string ApplyFontBundleSelection(ConfigEntry<string> e)
        {
            if (e == null || s_FontBundleNames == null || s_FontBundleNames.Count == 0) return null;
            int idx = EnsureFontBundleSelection(e, e.Value);
            if (idx < 0 || idx >= s_FontBundleNames.Count) idx = 0;
            var pick = s_FontBundleNames[idx];
            e.Value = pick;
            UpdateFontBundleSelection(e, idx, e.Value);
            TryRefreshByFontBundleEntry(e);
            return FormatFontBundleLabel(pick);
        }

        // 保存当前设置为预设文件（同名覆盖）。
        // 成功返回展示路径并重扫预设列表（ScanPresets(true) 会把选择回同步到当前 cfg 值，
        // 与旧抽屉一致：保存后不自动选中新预设）；失败返回 null。
        internal static string SavePresetAs(string rawName)
        {
            string savedPath = SaveCurrentSettingsToPresetFile(rawName);
            if (string.IsNullOrEmpty(savedPath))
            {
                return null;
            }
            PushClientToast($"已成功保存预设文件，位于 {savedPath}");
            ScanPresets(true);
            return savedPath;
        }

        // —— 随机测试发送（两个测试按钮的实际逻辑；IMGUI 抽屉与 GUI 行共用） ——
        internal static void SendRandomTestSubtitle()
        {
            try
            {
                var mgr = Subtitle.Plugin.Instance != null
                    ? Subtitle.Plugin.Instance.GetOrCreateSubtitleManagerAnyScene()
                    : null;

                if (mgr == null)
                {
                    s_Log.LogWarning("[Subtitle] SubtitleManager 未就绪，无法发送测试字幕。");
                }
                else
                {
                    SendRandomTestLine("Subtitle", Channel.Subtitle, SubtitleShowRoleTag, SubtitleShowDistance,
                        "（占位）这是随机测试字幕。", "[Subtitle] 未找到可用的本地台词文件，改用占位文本。",
                        delegate (string shown, Color c) { mgr.AddSubtitle(shown, c, 3.0f); });
                }
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Subtitle] TestSubtitle random failed: " + e);
            }
        }

        internal static void SendRandomTestDanmaku()
        {
            try
            {
                var mgr = Subtitle.Plugin.Instance != null
                    ? Subtitle.Plugin.Instance.GetOrCreateSubtitleManagerAnyScene()
                    : null;

                if (mgr != null)
                {
                    mgr.ApplyDanmakuSettings();
                    mgr.InitializeDanmakuLayer();

                    for (int i = 0; i < 3; i++)
                    {
                        SendRandomTestLine("Danmaku", Channel.Danmaku, DanmakuShowRoleTag, DanmakuShowDistance,
                            "（占位）这是随机测试弹幕。", null,
                            delegate (string shown, Color c) { mgr.AddDanmaku(shown, c); });
                    }
                }
                else
                {
                    s_Log.LogWarning("[Subtitle] SubtitleManager 未就绪，无法发送测试弹幕。");
                }
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Subtitle] TestDanmaku random failed: " + e);
            }
        }

        private static int EnsureFontBundleSelection(ConfigEntryBase entry, string currentValue)
        {
            if (entry == null) return GetFontBundleIndex(currentValue);
            string last;
            int idx;
            if (!s_FontBundleSelection.TryGetValue(entry, out idx) ||
                !s_FontBundleSelectionValue.TryGetValue(entry, out last) ||
                !string.Equals(last ?? "", currentValue ?? "", StringComparison.OrdinalIgnoreCase))
            {
                idx = GetFontBundleIndex(currentValue);
                UpdateFontBundleSelection(entry, idx, currentValue);
            }
            return idx;
        }

        private static void UpdateFontBundleSelection(ConfigEntryBase entry, int index, string currentValue)
        {
            if (entry == null) return;
            int idx = index;
            if (s_FontBundleNames != null && s_FontBundleNames.Count > 0)
            {
                if (idx < 0) idx = 0;
                if (idx >= s_FontBundleNames.Count) idx = s_FontBundleNames.Count - 1;
            }
            s_FontBundleSelection[entry] = idx;
            s_FontBundleSelectionValue[entry] = currentValue ?? "";
        }

        private static void TryRefreshByFontBundleEntry(ConfigEntry<string> e)
        {
            if (e == null) return;
            if (ReferenceEquals(e, SubtitleFontBundleName)) { TryRefreshSubtitleStyleRuntime(); return; }
            if (ReferenceEquals(e, DanmakuFontBundleName)) { TryRefreshDanmakuStyleRuntime(); return; }
            if (ReferenceEquals(e, World3DFontBundleName)) { TryRefreshWorld3DStyleRuntime(); return; }
        }

        internal static string FormatFontBundleLabel(string name)
        {
            return string.IsNullOrEmpty(name) ? I18n.Text("FontBundle.NoOverride", "(不覆盖)") : name;
        }

        internal static void SendRandomTestWorld3D()
        {
            try
            {
                var world = Singleton<GameWorld>.Instance;
                IPlayer player = world != null ? world.MainPlayer as IPlayer : null;
                var mgr = Subtitle.Plugin.Instance != null
                    ? Subtitle.Plugin.Instance.GetOrCreateSubtitleManagerAnyScene()
                    : null;
                if (mgr == null || player == null)
                {
                    PushClientToast(I18n.Text("Debug.World3DUnavailable", "3D 气泡测试需要进入藏身处或战局。"));
                    return;
                }

                string voiceKey, line;
                if (!TryPickRandomAllowedLine("World3D", out voiceKey, out line))
                    line = I18n.Text("Debug.World3DPlaceholder", "这是一条 3D 气泡测试。 ");
                line = Subtitle.Utils.StreamerFilter.Apply(line);
                mgr.AddWorld3D(player, line, Color.white, 4f);
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Subtitle] TestWorld3D random failed: " + e);
            }
        }

        internal static void OpenVoiceDebugPanel()
        {
            try
            {
                if (EnableDebugTools != null && !EnableDebugTools.Value)
                    EnableDebugTools.Value = true;
                if (Subtitle.Plugin.Instance == null || !Subtitle.Plugin.Instance.OpenVoiceDebugPanel())
                    PushClientToast(I18n.Text("Debug.RequiresGameWorld", "语音浏览需要进入藏身处或战局。"));
            }
            catch { }
        }

        internal static void OpenDiagnosticsPanel()
        {
            try
            {
                if (EnableDebugTools != null && !EnableDebugTools.Value)
                    EnableDebugTools.Value = true;
                Subtitle.DebugTools.DebugDiagnosticsPanel.ToggleVisible();
            }
            catch (Exception e) { s_Log.LogWarning("[Debug] Open diagnostics failed: " + e); }
        }

        internal static void ReloadCurrentLocaleResources()
        {
            string language = UiLanguage != null ? UiLanguage.Value : I18n.CurrentLanguage;
            ReloadLocalizedResources(language);
            Subtitle.DebugTools.DebugDiagnostics.RecordSystem(
                I18n.Text("Debug.LocaleReloaded", "已重新加载当前语言资源。"));
            PushClientToast(I18n.Text("Debug.LocaleReloaded", "已重新加载当前语言资源。"));
        }

        internal static bool IsFontReplaceInstalled()
        {
            try
            {
                return Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(FontReplacePluginGuid);
            }
            catch { return false; }
        }

        private static int GetFontBundleIndex(string name)
        {
            if (s_FontBundleNames == null || s_FontBundleNames.Count == 0) return 0;
            if (string.IsNullOrEmpty(name)) return 0;
            for (int i = 0; i < s_FontBundleNames.Count; i++)
            {
                if (string.Equals(s_FontBundleNames[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }

        private static string GetFontBundleDir()
        {
            try
            {
                return Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "FontReplace", "Font");
            }
            catch { return null; }
        }

        private static void ScanFontBundles()
        {
            try
            {
                var dir = GetFontBundleDir();
                var list = new List<string>();
                list.Add(""); // 不覆盖
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < files.Length; i++)
                    {
                        var n = Path.GetFileName(files[i]);
                        if (string.IsNullOrEmpty(n)) continue;
                        var ext = Path.GetExtension(n);
                        if (string.Equals(ext, ".manifest", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ext, ".meta", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (!list.Exists(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))
                            list.Add(n);
                    }
                }

                s_FontBundleNames = list;
                s_FontBundleListLoaded = true;
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Settings] ScanFontBundles failed: " + e);
                s_FontBundleNames = new List<string> { "" };
                s_FontBundleListLoaded = true;
            }
        }

        private static string ShortPathFromBepInEx(string absPath)
        {
            try
            {
                var p = (absPath ?? "").Replace('\\', '/');
                const string mark = "/BepInEx/";
                int i = p.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
                if (i >= 0)
                    return p.Substring(i + 1);   // 去掉前导 '/'，得到 "BepInEx/..."
                                                 // 兜底：找不到就只返回文件名
                return System.IO.Path.GetFileName(p);
            }
            catch { return absPath; }
        }

        // 两个“随机测试”按钮共用的 组句+发送 逻辑（fallbackWarning 为 null 时不打日志）
        private static void SendRandomTestLine(string channel, Channel ch, ConfigEntry<bool> showRoleEntry, ConfigEntry<bool> showDistEntry,
            string placeholderLine, string fallbackWarning, Action<string, Color> send)
        {
            string voiceKey, line;
            if (!TryPickRandomAllowedLine(channel, out voiceKey, out line))
            {
                if (!string.IsNullOrEmpty(fallbackWarning))
                    s_Log.LogWarning(fallbackWarning);
                voiceKey = "_default";
                line = placeholderLine;
            }

            // 主播模式：测试按钮直读 JSON（绕过 GetSubtitleForChannel），这里单独打码
            line = Subtitle.Utils.StreamerFilter.Apply(line);

            string aiType = GetRandomAiTypeForTest(voiceKey);
            var kind = GuessRoleKindFromAiType(aiType);
            string roleName = MapAITypeLabelLocal(aiType);
            string roleTag = roleName + "：";

            bool showRole = showRoleEntry != null ? showRoleEntry.Value : true;
            bool showDist = showDistEntry != null ? showDistEntry.Value : true;
            int randM = UnityEngine.Random.Range(10, 151);
            string distSuffix = showDist ? (" <b>·</b>" + randM + "m") : "";

            string shown = showRole ? (WrapRoleTag(roleTag, kind, ch) + line) : line;
            shown += distSuffix;

            var textColor = GetTextColor(kind, ch);
            send(shown, textColor);
        }

        // 设置预览面板「随机」按钮复用：随机挑一条通过台词过滤的真实语音行，
        // 并顺手给出猜测的角色类别（与 F12 测试按钮同一条挑选链路：voiceKey → aiType → RoleKind）。
        // 同文件内组合现有 private 实现，无需改动原方法的可见性。
        internal static bool TryPickRandomPreviewLine(string channel, out string aiType, out RoleKind kind, out string text)
        {
            aiType = null;
            kind = RoleKind.Player;
            text = null;

            string voiceKey, line;
            if (!TryPickRandomAllowedLine(channel, out voiceKey, out line)) return false;

            aiType = GetRandomAiTypeForTest(voiceKey);
            kind = GuessRoleKindFromAiType(aiType);
            text = line;
            return true;
        }

        private static void DrawSettingsWindowButton(ConfigEntryBase entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(I18n.Text("F12.SettingsWindow.Open", "打开图形化设置界面"), GUILayout.Height(24), GUILayout.Width(260)))
            {
                try { Subtitle.SettingsUI.SettingsWindow.ToggleVisible(); } catch { }
            }
            GUILayout.EndHorizontal();
        }

        // —— 硬编码的 AI 类型别名列表（全部小写；支持 * 前缀通配）——
        private static readonly string[] kRogueAliasesLC = { "exusec" };
        private static readonly string[] kRaiderAliasesLC = { "pmcbot" };
        private static readonly string[] kScavAliasesLC = {
        "assault",
        "marksman",
        "test",
        "assaultgroup",
        "cursedassault",
        "crazyassaultevent",
        "skier",
        "peacemaker",
        "gifter",
        "arenafighter",
        "shooterbtr",
        "spiritwinter",
        "spiritspring",
        "arenafighterevent"
    };
        private static readonly string[] kCultistAliasesLC = {
        "sectantpriest",
        "sectantwarrior",
        "sectactpriestevent"
    };
        private static readonly string[] kGoonsAliasesLC = {
        "followerbigpipe",
        "followerbirdeye",
        "bossknight",
        "sectantoni",
        "sectantpredvestnik",
        "sectantprizrak"
    };
        private static readonly string[] kBossFollowerAliasesLC = {
        "followerboarclose1",
        "followertest",
        "followergluharassault",
        "followergluharscout",
        "followergluharsecurity",
        "followergluharsnipe",
        "followerboarclose2",
        "followerboar",
        "bossboarsniper",
        "followerkolontayassault",
        "followerkolontaysecurity",
        "followerbully",
        "followersanitar",
        "followerkojaniy",
        "followerzryachiy",
        "followertagilla",
        "tagillahelperagro",
        "blackdivision"
    };
        private static readonly string[] kZombieAliasesLC = {
        "infectedpmc",
        "infectedlaborant",
        "infectedassault",
        "infectedcivil"
    };
        private static readonly string[] kBossAliasesLC = {
        "bosstest",
        "sectantprizrak",
        "bossgluhar",
        "bossboar",
        "bosskilla",
        "bosskolontay",
        "sectantoni",
        "sectantpredvestnik",
        "bosspartisan",
        "bossbully",
        "bosssanitar",
        "bosstagillaagro",
        "bosskojaniy",
        "bosstagilla",
        "bosskillaagro",
        "infectedtagilla",
        "bosszryachiy",
        "peacefullzryachiyevent",
        "ravangezryachiyevent"
    };

        // 别名 -> 角色类别 的字典（按原 if 链顺序构建，重复别名只保留先命中的角色；
        // 键保持数组里的原始写法、用 Ordinal 比较，与原先 In(t, arr) 的逐字节比较完全等价）
        private static readonly Dictionary<string, RoleKind> s_RoleKindByAlias = BuildRoleKindAliasMap();

        private static Dictionary<string, RoleKind> BuildRoleKindAliasMap()
        {
            var map = new Dictionary<string, RoleKind>(StringComparer.Ordinal);
            AddRoleAliases(map, kRogueAliasesLC, RoleKind.Rogue);
            AddRoleAliases(map, kRaiderAliasesLC, RoleKind.Raider);
            AddRoleAliases(map, kScavAliasesLC, RoleKind.Scav);
            AddRoleAliases(map, kCultistAliasesLC, RoleKind.Cultist);
            AddRoleAliases(map, kGoonsAliasesLC, RoleKind.Goons);
            AddRoleAliases(map, kBossFollowerAliasesLC, RoleKind.BossFollower);
            AddRoleAliases(map, kZombieAliasesLC, RoleKind.Zombie);
            AddRoleAliases(map, kBossAliasesLC, RoleKind.Bosses);
            return map;
        }

        private static void AddRoleAliases(Dictionary<string, RoleKind> map, string[] aliases, RoleKind kind)
        {
            for (int i = 0; i < aliases.Length; i++)
            {
                if (!map.ContainsKey(aliases[i]))
                    map.Add(aliases[i], kind);
            }
        }

        private static bool TryParseRoleKind(string value, out RoleKind kind)
        {
            kind = RoleKind.Scav;
            if (string.IsNullOrEmpty(value)) return false;
            string normalized = value.Trim();
            if (string.Equals(normalized, "Boss", StringComparison.OrdinalIgnoreCase)) normalized = "Bosses";
            if (string.Equals(normalized, "Follower", StringComparison.OrdinalIgnoreCase)) normalized = "BossFollower";
            return Enum.TryParse(normalized, true, out kind);
        }

        private static bool TryGetUserRoleKind(string aiTypeLower, out RoleKind kind)
        {
            kind = RoleKind.Scav;
            if (string.IsNullOrEmpty(aiTypeLower)) return false;
            EnsureUserRoleMapLoaded();

            if (s_UserRoleKindMapExact != null && s_UserRoleKindMapExact.TryGetValue(aiTypeLower, out kind))
                return true;

            if (s_UserRoleKindMapPrefix != null)
            {
                for (int i = 0; i < s_UserRoleKindMapPrefix.Count; i++)
                {
                    var kv = s_UserRoleKindMapPrefix[i];
                    if (aiTypeLower.StartsWith(kv.Key))
                    {
                        kind = kv.Value;
                        return true;
                    }
                }
            }
            return false;
        }

        public static RoleKind GuessRoleKindFromAiType(string aiType)
        {
            try
            {
                if (string.IsNullOrEmpty(aiType)) return RoleKind.Scav;
                var t = aiType.ToLowerInvariant();

                // RoleType.jsonc 的对象写法可覆盖内置颜色类别，供第三方角色无代码扩展。
                RoleKind userKind;
                if (TryGetUserRoleKind(t, out userKind)) return userKind;

                // 先查别名表（字典 O(1)，替代原来的 8 次数组线性扫描）
                RoleKind aliased;
                if (s_RoleKindByAlias.TryGetValue(t, out aliased)) return aliased;

                // 再做规则 fallback（保持你现有的 startsWith/contains 逻辑）
                if (t.StartsWith("pmcbear") || t == "pmcbear") return RoleKind.PmcBear;
                if (t.StartsWith("pmcusec") || t == "pmcusec") return RoleKind.PmcUsec;
                if (t.StartsWith("assault") || t == "scav") return RoleKind.Scav;
                if (t.Contains("raider")) return RoleKind.Raider;
                if (t.Contains("rogue")) return RoleKind.Rogue;
                if (t.Contains("cultist")) return RoleKind.Cultist;
                if (t.Contains("follower")) return RoleKind.BossFollower;
                if (t.Contains("zombie")) return RoleKind.Zombie;
                if (t.Contains("goons")) return RoleKind.Goons;
                if (t.Contains("boss")) return RoleKind.Bosses;
            }
            catch { }
            // 只有补丁层明确判定为玩家/队友时才覆盖对应类别；未知 AI 按约定归入 Scav。
            return RoleKind.Scav;
        }

        // ===================== 构建：字体/布局/背景 =====================
        public static SubtitleSystem.FontSpec BuildSubtitleFontSpec()
        {
            return BuildFontSpec(SubtitleFontBundleName, SubtitleFontFamilyCsv, SubtitleFontSize, 26, SubtitleFontBold, SubtitleFontItalic);
        }

        // 三个渠道共用的 FontSpec 构建；CSV 统一同时接受 ; 和 , 两种分隔符
        // （原字幕版只按 , 切，弹幕/3D 版按 ;, 切——统一为超集，不影响既有配置）
        private static SubtitleSystem.FontSpec BuildFontSpec(
            ConfigEntry<string> bundleEntry, ConfigEntry<string> csvEntry, ConfigEntry<int> sizeEntry,
            int defaultSize, ConfigEntry<bool> boldEntry, ConfigEntry<bool> italicEntry)
        {
            var spec = new SubtitleSystem.FontSpec();
            try
            {
                var csv = csvEntry != null ? (csvEntry.Value ?? "") : "";
                var list = new List<string>();
                var bundle = bundleEntry != null ? (bundleEntry.Value ?? "") : "";
                if (!string.IsNullOrEmpty(bundle))
                    list.Add("bundle:" + bundle);
                if (!string.IsNullOrEmpty(csv))
                {
                    var arr = csv.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var s = arr[i].Trim();
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                }
                spec.family = list;
                spec.size = sizeEntry != null ? Math.Max(12, sizeEntry.Value) : defaultSize;
                spec.bold = boldEntry != null && boldEntry.Value;
                spec.italic = italicEntry != null && italicEntry.Value;
            }
            catch { }
            return spec;
        }

        public static SubtitleSystem.TextStyle.LayoutSpec BuildSubtitleLayoutSpec()
        {
            var s = new SubtitleSystem.TextStyle.LayoutSpec();
            try
            {
                if (SubtitleLayoutAnchor != null && SubtitleLayoutAnchor.Value != Settings.TextAnchorOption.None)
                    s.anchor = SubtitleLayoutAnchor.Value.ToString();
                else
                    s.anchor = "LowerCenter";
                s.offset = new double[] {
                    SubtitleLayoutOffsetX != null ? SubtitleLayoutOffsetX.Value : 0.0,
                    SubtitleLayoutOffsetY != null ? SubtitleLayoutOffsetY.Value : 110.0
                };
                s.safeArea = SubtitleLayoutSafeArea != null && SubtitleLayoutSafeArea.Value;
                s.maxWidthPercent = SubtitleLayoutMaxWidthPercent != null ? SubtitleLayoutMaxWidthPercent.Value : 0.90f;
                s.lineSpacing = SubtitleLayoutLineSpacing != null ? SubtitleLayoutLineSpacing.Value : 0.0f;
                if (SubtitleLayoutOverrideAlign != null && SubtitleLayoutOverrideAlign.Value != Settings.TextAnchorOption.None)
                    s.overrideTextAlignment = SubtitleLayoutOverrideAlign.Value.ToString();
                else
                    s.overrideTextAlignment = null;
                s.stackOffsetPercent = SubtitleLayoutStackOffsetPercent != null ? SubtitleLayoutStackOffsetPercent.Value : 0.12f;
            }
            catch { }
            return s;
        }

        public static SubtitleSystem.TextStyle.BackgroundSpec BuildSubtitleBackgroundSpec()
        {
            var b = new SubtitleSystem.TextStyle.BackgroundSpec();
            try
            {
                b.enabled = SubtitleBgEnabled != null && SubtitleBgEnabled.Value;
                b.fit = SubtitleBgFit != null ? (SubtitleBgFit.Value ?? "text") : "text";
                b.color = SubtitleBgColor != null ? ColorUtility.ToHtmlStringRGBA(SubtitleBgColor.Value) : null;
                if (!string.IsNullOrEmpty(b.color)) b.color = "#" + b.color;

                b.padding = new double[] {
                    SubtitleBgPaddingX != null ? SubtitleBgPaddingX.Value : 12.0,
                    SubtitleBgPaddingY != null ? SubtitleBgPaddingY.Value : 6.0
                };
                b.margin = new double[] {
                    0.0,
                    SubtitleBgMarginY != null ? SubtitleBgMarginY.Value : 6.0
                };
                b.sprite = SubtitleBgSprite != null ? (SubtitleBgSprite.Value ?? "") : "";

                b.shadow = new SubtitleSystem.ShadowSpec
                {
                    enabled = SubtitleBgShadowEnabled != null && SubtitleBgShadowEnabled.Value,
                    color = (SubtitleBgShadowColor != null)
                        ? ("#" + ColorUtility.ToHtmlStringRGBA(SubtitleBgShadowColor.Value))
                        : "#00000080",
                    distance = new double[] {
                        SubtitleBgShadowDistX != null ? SubtitleBgShadowDistX.Value : 2.0,
                        SubtitleBgShadowDistY != null ? SubtitleBgShadowDistY.Value : -2.0
                    },
                    useGraphicAlpha = SubtitleBgShadowUseGraphicAlpha != null && SubtitleBgShadowUseGraphicAlpha.Value
                };
            }
            catch { }
            return b;
        }

        // 应用“字幕样式”覆盖到 Text
        public static void ApplySubtitleTextOverrides(Text text)
        {
            if (text == null) return;

            try
            {
                var spec = BuildSubtitleFontSpec();
                var f = SubtitleSystem.SubtitleFontLoader.ResolveFont(spec);
                if (f != null && text.font != f) text.font = f;

                if (SubtitleFontSize != null && SubtitleFontSize.Value > 0)
                    text.fontSize = SubtitleFontSize.Value;

                var bold = SubtitleFontBold != null && SubtitleFontBold.Value;
                var italic = SubtitleFontItalic != null && SubtitleFontItalic.Value;
                text.fontStyle = (bold ? (italic ? FontStyle.BoldAndItalic : FontStyle.Bold)
                                       : (italic ? FontStyle.Italic : FontStyle.Normal));
            }
            catch { }

            try
            {
                Settings.TextAnchorOption pick = Settings.TextAnchorOption.None;

                // ① 优先：布局里的 override
                if (SubtitleLayoutOverrideAlign != null && SubtitleLayoutOverrideAlign.Value != Settings.TextAnchorOption.None)
                    pick = SubtitleLayoutOverrideAlign.Value;

                // ② 其次：常规 Alignment
                if (pick == Settings.TextAnchorOption.None && SubtitleAlignment != null)
                    pick = SubtitleAlignment.Value;

                TextAnchor ta;
                if (TryGetTextAnchor(pick, out ta))
                    text.alignment = ta;
            }
            catch { }

            try
            {
                bool wrap = SubtitleWrap != null && SubtitleWrap.Value;
                text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            }
            catch { }

            ApplyOutlineOverride(text, SubtitleOutlineEnabled, SubtitleOutlineColor, SubtitleOutlineDistX, SubtitleOutlineDistY, 1.5f);
            ApplyShadowOverride(text, SubtitleShadowEnabled, SubtitleShadowColor, SubtitleShadowDistX, SubtitleShadowDistY, SubtitleShadowUseGraphicAlpha);
        }

        // 三个渠道共用的描边组件应用逻辑（各自 try/catch 语义保留在 helper 内）
        private static void ApplyOutlineOverride(Text text, ConfigEntry<bool> enabledEntry, ConfigEntry<Color> colorEntry,
            ConfigEntry<float> distXEntry, ConfigEntry<float> distYEntry, float defaultDist)
        {
            try
            {
                var go = text.gameObject;
                var ol = go.GetComponent<Outline>();
                if (enabledEntry != null && enabledEntry.Value)
                {
                    if (ol == null) ol = go.AddComponent<Outline>();
                    ol.useGraphicAlpha = true;
                    if (colorEntry != null) ol.effectColor = colorEntry.Value;
                    float dx = distXEntry != null ? distXEntry.Value : defaultDist;
                    float dy = distYEntry != null ? distYEntry.Value : defaultDist;
                    ol.effectDistance = new Vector2(dx, dy);
                }
                else
                {
                    if (ol != null) UnityEngine.Object.Destroy(ol);
                }
            }
            catch { }
        }

        // 三个渠道共用的阴影组件应用逻辑
        private static void ApplyShadowOverride(Text text, ConfigEntry<bool> enabledEntry, ConfigEntry<Color> colorEntry,
            ConfigEntry<float> distXEntry, ConfigEntry<float> distYEntry, ConfigEntry<bool> useGraphicAlphaEntry)
        {
            try
            {
                var go = text.gameObject;
                Shadow drop = null;
                var shadows = go.GetComponents<Shadow>();
                if (shadows != null)
                {
                    for (int i = 0; i < shadows.Length; i++)
                        if (!(shadows[i] is Outline)) { drop = shadows[i]; break; }
                }

                if (enabledEntry != null && enabledEntry.Value)
                {
                    if (drop == null) drop = go.AddComponent<Shadow>();
                    if (useGraphicAlphaEntry != null) drop.useGraphicAlpha = useGraphicAlphaEntry.Value;
                    if (colorEntry != null) drop.effectColor = colorEntry.Value;
                    float dx = distXEntry != null ? distXEntry.Value : 2f;
                    float dy = distYEntry != null ? distYEntry.Value : -2f;
                    drop.effectDistance = new Vector2(dx, dy);
                }
                else
                {
                    if (drop != null) UnityEngine.Object.Destroy(drop);
                }
            }
            catch { }
        }

        // ===================== 预设 Setting → cfg 回填 =====================
        private static void ApplySettingsOverrideFromPreset(SubtitleSystem.SubtitleTextPreset preset)
        {
            if (preset == null || preset.Setting == null) return;
            var S = preset.Setting;

            // 批量套入：期间抑制每个 SettingChanged 触发的样式刷新，结束后每个子系统只刷新一次
            s_BatchApplying = true;
            try
            {
                for (int i = 0; i < s_SnapshotReaders.Count; i++)
                {
                    try { s_SnapshotReaders[i](S); } catch { }
                }
            }
            finally
            {
                s_BatchApplying = false;
                for (int i = 0; i < s_BatchPendingRefreshes.Count; i++)
                {
                    try { s_BatchPendingRefreshes[i](); } catch { }
                }
                s_BatchPendingRefreshes.Clear();
            }
        }

        // —— 角色/广播颜色：你现有的 setColor(pick(...), …) 已经是平铺键，保持不变 ——
        // SubRole_LabAnnouncer / SubText_LabAnnouncer / DmRole_LabAnnouncer / DmText_LabAnnouncer

        // ===================== 角色映射/本地资源 =====================
        private static void EnsureUserRoleMapLoaded()
        {
            if (s_RoleTypeLoaded) return;
            s_RoleTypeLoaded = true;
            s_UserRoleMapExact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            s_UserRoleMapPrefix = new List<KeyValuePair<string, string>>();
            s_UserRoleKindMapExact = new Dictionary<string, RoleKind>(StringComparer.OrdinalIgnoreCase);
            s_UserRoleKindMapPrefix = new List<KeyValuePair<string, RoleKind>>();

            try
            {
                var file = PhraseFilterManager.ResolveLocaleFile("RoleType.jsonc");
                if (!File.Exists(file)) return;

                var json = JsoncUtils.StripJsonComments(File.ReadAllText(file, Encoding.UTF8));
                var root = JObject.Parse(json);
                foreach (var p in root.Properties())
                {
                    var key = (p.Name ?? "").Trim();
                    if (string.IsNullOrEmpty(key)) continue;

                    string val = null;
                    string kindText = null;
                    var definition = p.Value as JObject;
                    if (definition != null)
                    {
                        val = (definition.Value<string>("Name") ?? definition.Value<string>("Label") ?? "").Trim();
                        kindText = (definition.Value<string>("Kind") ?? definition.Value<string>("RoleKind") ?? "").Trim();
                    }
                    else
                    {
                        val = (p.Value != null ? p.Value.ToString() : "").Trim();
                    }

                    RoleKind configuredKind;
                    bool hasKind = TryParseRoleKind(kindText, out configuredKind);
                    if (!string.IsNullOrEmpty(kindText) && !hasKind)
                        s_Log.LogWarning("[Settings] RoleType.jsonc 未知 Kind：" + kindText + "（" + key + "）");

                    if (key.EndsWith("*", StringComparison.Ordinal))
                    {
                        string prefix = key.Substring(0, key.Length - 1).Trim();
                        if (!string.IsNullOrEmpty(prefix))
                        {
                            string lowerPrefix = prefix.ToLowerInvariant();
                            if (!string.IsNullOrEmpty(val))
                                s_UserRoleMapPrefix.Add(new KeyValuePair<string, string>(lowerPrefix, val));
                            if (hasKind)
                                s_UserRoleKindMapPrefix.Add(new KeyValuePair<string, RoleKind>(lowerPrefix, configuredKind));
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(val)) s_UserRoleMapExact[key] = val;
                        if (hasKind) s_UserRoleKindMapExact[key] = configuredKind;
                    }
                }
                s_UserRoleMapPrefix.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
                s_UserRoleKindMapPrefix.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Settings] Load RoleType.jsonc failed: " + e);
            }
        }

        private static string MapAITypeLabelLocal(string aiTypeRaw)
        {
            if (string.IsNullOrEmpty(aiTypeRaw)) return "AI";
            EnsureUserRoleMapLoaded();

            string mapped;
            if (s_UserRoleMapExact != null && s_UserRoleMapExact.TryGetValue(aiTypeRaw, out mapped) && !string.IsNullOrEmpty(mapped))
                return mapped;

            if (s_UserRoleMapPrefix != null && s_UserRoleMapPrefix.Count > 0)
            {
                var lower = aiTypeRaw.ToLowerInvariant();
                for (int i = 0; i < s_UserRoleMapPrefix.Count; i++)
                {
                    var kv = s_UserRoleMapPrefix[i];
                    if (lower.StartsWith(kv.Key)) return string.IsNullOrEmpty(kv.Value) ? aiTypeRaw : kv.Value;
                }
            }

            if (Subtitle.Utils.SubtitleEnum.DEFAULT_AI_TYPE_LABELS.TryGetValue(aiTypeRaw, out mapped) && !string.IsNullOrEmpty(mapped))
                return mapped;

            return aiTypeRaw;
        }

        public static string GetRoleLabel(string key, string fallback)
        {
            try
            {
                string s = MapAITypeLabelLocal(key);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            return string.IsNullOrEmpty(fallback) ? key : fallback;
        }

        private static void InvalidateRoleLabelCache()
        {
            s_RoleTypeLoaded = false;
            s_UserRoleMapExact = null;
            s_UserRoleMapPrefix = null;
            s_UserRoleKindMapExact = null;
            s_UserRoleKindMapPrefix = null;
            s_AllAiTypeKeysCache = null;
        }

        private static void ReloadLocalizedResources(string language)
        {
            PhraseFilterManager.InvalidateLocaleCaches();
            InvalidateRoleLabelCache();
            SubtitleSystem.PhraseSubtitle.InvalidateCache();
            Subtitle.Utils.StreamerFilter.InvalidateCache();
            I18n.Reload(language);
            ApplySlimConfigurationManagerLocalization();
            Subtitle.LabRadioPatch.ReloadLocaleResources();
            SettingsUI.SettingsWindow.RebuildAll();
            PhraseFilterPanel.RefreshLocalization();
            Subtitle.DebugTools.DebugPhrasePanel.RefreshLocalization();
        }

        private static string GetRandomAiTypeForTest(string voiceKey)
        {
            if (!string.IsNullOrEmpty(voiceKey))
            {
                var vk = voiceKey.ToLowerInvariant();
                if (vk.StartsWith("usec")) return "pmcUSEC";
                if (vk.StartsWith("bear")) return "pmcBEAR";
            }

            if (s_AllAiTypeKeysCache == null || s_AllAiTypeKeysCache.Count == 0)
            {
                EnsureUserRoleMapLoaded();
                s_AllAiTypeKeysCache = new List<string>();

                if (s_UserRoleMapExact != null)
                {
                    foreach (var k in s_UserRoleMapExact.Keys) if (!string.IsNullOrEmpty(k)) s_AllAiTypeKeysCache.Add(k);
                }
                if (s_UserRoleKindMapExact != null)
                {
                    foreach (var k in s_UserRoleKindMapExact.Keys)
                    {
                        if (!string.IsNullOrEmpty(k) && !s_AllAiTypeKeysCache.Contains(k, StringComparer.OrdinalIgnoreCase))
                            s_AllAiTypeKeysCache.Add(k);
                    }
                }
                foreach (var k in Subtitle.Utils.SubtitleEnum.DEFAULT_AI_TYPE_LABELS.Keys)
                {
                    if (!string.IsNullOrEmpty(k) && !s_AllAiTypeKeysCache.Contains(k, StringComparer.OrdinalIgnoreCase))
                        s_AllAiTypeKeysCache.Add(k);
                }
                if (s_AllAiTypeKeysCache.Count == 0)
                    s_AllAiTypeKeysCache.Add("assault");
            }

            int idx = UnityEngine.Random.Range(0, s_AllAiTypeKeysCache.Count);
            return s_AllAiTypeKeysCache[idx];
        }

        // ===================== 杂项工具 =====================
        private static string GetLocalesDir()
        {
            // 统一由 PhraseFilterManager 解析（含多级回退与缓存；正常安装布局下路径不变）
            return PhraseFilterManager.LocalesDir;
        }

        public static SubtitleSystem.FontSpec BuildDanmakuFontSpec()
        {
            return BuildFontSpec(DanmakuFontBundleName, DanmakuFontFamilyCsv, DanmakuFontSize, 24, DanmakuFontBold, DanmakuFontItalic);
        }

        public static void ApplyDanmakuTextOverrides(UnityEngine.UI.Text text)
        {
            if (text == null) return;
            try
            {
                var spec = BuildDanmakuFontSpec();
                var f = SubtitleSystem.SubtitleFontLoader.ResolveFont(spec);
                if (f != null && text.font != f) text.font = f;

                if (DanmakuFontSize != null && DanmakuFontSize.Value > 0)
                    text.fontSize = DanmakuFontSize.Value;

                var bold = DanmakuFontBold != null && DanmakuFontBold.Value;
                var italic = DanmakuFontItalic != null && DanmakuFontItalic.Value;
                text.fontStyle = (bold ? (italic ? FontStyle.BoldAndItalic : FontStyle.Bold)
                                       : (italic ? FontStyle.Italic : FontStyle.Normal));
            }
            catch { }

            ApplyOutlineOverride(text, DanmakuOutlineEnabled, DanmakuOutlineColor, DanmakuOutlineDistX, DanmakuOutlineDistY, 1.2f);
            ApplyShadowOverride(text, DanmakuShadowEnabled, DanmakuShadowColor, DanmakuShadowDistX, DanmakuShadowDistY, DanmakuShadowUseGraphicAlpha);
        }

        public static SubtitleSystem.FontSpec BuildWorld3DFontSpec()
        {
            return BuildFontSpec(World3DFontBundleName, World3DFontFamilyCsv, World3DFontSize, 26, World3DFontBold, World3DFontItalic);
        }

        public static void ApplyWorld3DTextOverrides(UnityEngine.UI.Text text)
        {
            if (text == null) return;
            try
            {
                var spec = BuildWorld3DFontSpec();
                var f = SubtitleSystem.SubtitleFontLoader.ResolveFont(spec);
                if (f != null && text.font != f) text.font = f;

                if (World3DFontSize != null && World3DFontSize.Value > 0)
                    text.fontSize = World3DFontSize.Value;

                var bold = World3DFontBold != null && World3DFontBold.Value;
                var italic = World3DFontItalic != null && World3DFontItalic.Value;
                text.fontStyle = (bold ? (italic ? FontStyle.BoldAndItalic : FontStyle.Bold)
                                       : (italic ? FontStyle.Italic : FontStyle.Normal));
            }
            catch { }

            try
            {
                if (World3DAlignment != null)
                {
                    TextAnchor ta;
                    if (TryGetTextAnchor(World3DAlignment.Value, out ta))
                        text.alignment = ta;
                }
            }
            catch { }

            try
            {
                bool wrap = World3DWrap != null && World3DWrap.Value;
                text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            }
            catch { }

            ApplyOutlineOverride(text, World3DOutlineEnabled, World3DOutlineColor, World3DOutlineDistX, World3DOutlineDistY, 1.5f);
            ApplyShadowOverride(text, World3DShadowEnabled, World3DShadowColor, World3DShadowDistX, World3DShadowDistY, World3DShadowUseGraphicAlpha);
        }

        public static bool ApplyWorld3DTMPOverrides(TextMeshProUGUI text)
        {
            if (text == null) return false;
            TMP_FontAsset font = null;
            try
            {
                font = SubtitleSystem.SubtitleFontLoader.ResolveTMPFont(BuildWorld3DFontSpec());
                if (font == null) return false;
                if (text.font != font) text.font = font;

                if (World3DFontSize != null && World3DFontSize.Value > 0)
                    text.fontSize = World3DFontSize.Value;

                FontStyles style = FontStyles.Normal;
                if (World3DFontBold != null && World3DFontBold.Value) style |= FontStyles.Bold;
                if (World3DFontItalic != null && World3DFontItalic.Value) style |= FontStyles.Italic;
                text.fontStyle = style;
                text.enableWordWrapping = World3DWrap != null && World3DWrap.Value;
                text.overflowMode = TextOverflowModes.Overflow;
                text.richText = true;
                text.raycastTarget = false;

                if (World3DAlignment != null)
                {
                    TextAnchor anchor;
                    if (TryGetTextAnchor(World3DAlignment.Value, out anchor))
                        text.alignment = ToTMPAlignment(anchor);
                }

                bool outlineEnabled = World3DOutlineEnabled != null && World3DOutlineEnabled.Value;
                float outlineX = World3DOutlineDistX != null ? Mathf.Abs(World3DOutlineDistX.Value) : 1.5f;
                float outlineY = World3DOutlineDistY != null ? Mathf.Abs(World3DOutlineDistY.Value) : 1.5f;
                text.outlineWidth = outlineEnabled
                    ? Mathf.Clamp(Mathf.Max(outlineX, outlineY) * 2f / Mathf.Max(1f, text.fontSize), 0f, 1f)
                    : 0f;
                if (outlineEnabled && World3DOutlineColor != null)
                    text.outlineColor = World3DOutlineColor.Value;

                ApplyWorld3DTMPShadow(text);
                return true;
            }
            catch (Exception e)
            {
                Subtitle.Plugin.Log?.LogWarning("[World3D] TMP SDF style apply failed: " + e.Message);
                return false;
            }
        }

        private static TextAlignmentOptions ToTMPAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Center;
            }
        }

        private static void ApplyWorld3DTMPShadow(TextMeshProUGUI text)
        {
            Material material = text.fontMaterial;
            if (material == null) return;

            bool enabled = World3DShadowEnabled != null && World3DShadowEnabled.Value;
            if (!enabled)
            {
                material.DisableKeyword("UNDERLAY_ON");
                text.SetMaterialDirty();
                return;
            }

            int colorId = Shader.PropertyToID("_UnderlayColor");
            int offsetXId = Shader.PropertyToID("_UnderlayOffsetX");
            int offsetYId = Shader.PropertyToID("_UnderlayOffsetY");
            if (material.HasProperty(colorId) && World3DShadowColor != null)
                material.SetColor(colorId, World3DShadowColor.Value);
            if (material.HasProperty(offsetXId))
            {
                float offsetX = World3DShadowDistX != null ? World3DShadowDistX.Value : 2f;
                material.SetFloat(offsetXId, Mathf.Clamp(offsetX / 4f, -1f, 1f));
            }
            if (material.HasProperty(offsetYId))
            {
                float offsetY = World3DShadowDistY != null ? World3DShadowDistY.Value : -2f;
                material.SetFloat(offsetYId, Mathf.Clamp(offsetY / 4f, -1f, 1f));
            }
            material.EnableKeyword("UNDERLAY_ON");
            text.SetMaterialDirty();
        }

        private static bool TryPickRandomAllowedLine(string channel, out string voiceKey, out string text)
        {
            voiceKey = null;
            text = null;
            const int maxTries = 20;
            for (int i = 0; i < maxTries; i++)
            {
                string vk, phrase, netId, line;
                if (!TryPickRandomLine(out vk, out phrase, out netId, out line))
                    return false;

                bool allowNetId, allowGeneral;
                var vkLower = string.IsNullOrEmpty(vk) ? "" : vk.ToLowerInvariant();
                Subtitle.Config.PhraseFilterManager.GetAllowFlags(channel, vkLower, phrase, netId, out allowNetId, out allowGeneral);

                bool allowed = string.IsNullOrEmpty(netId) ? allowGeneral : allowNetId;
                if (!allowed) continue;

                voiceKey = vk;
                text = line;
                return true;
            }
            return false;
        }

        private static bool TryPickRandomLine(out string voiceKey, out string phrase, out string netId, out string text)
        {
            voiceKey = null;
            phrase = null;
            netId = null;
            text = null;
            try
            {
                var files = PhraseFilterManager.GetVoiceFiles();
                var picks = new List<string>();
                for (int i = 0; i < files.Count; i++)
                {
                    var name = Path.GetFileName(files[i]);
                    if (string.Equals(name, "RoleType.jsonc", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(name, "PhraseFilter.jsonc", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.StartsWith("_")) continue;
                    picks.Add(files[i]);
                }
                if (picks.Count == 0) return false;

                var file = picks[UnityEngine.Random.Range(0, picks.Count)];
                var vk = Path.GetFileNameWithoutExtension(file);
                voiceKey = string.IsNullOrEmpty(vk) ? "_default" : vk;

                // 复用 PhraseFilterManager 的 JSON 缓存，避免每次点击都 ReadAllText + Parse
                var root = PhraseFilterManager.GetVoiceJsonByPath(file);
                if (root == null) return false;

                var props = new List<JProperty>();
                foreach (var p in root.Properties()) props.Add(p);
                if (props.Count == 0) return false;
                var ph = props[UnityEngine.Random.Range(0, props.Count)];
                phrase = ph != null ? (ph.Name ?? "").Trim() : null;
                if (string.IsNullOrEmpty(phrase)) return false;

                JObject idsObj = ph.Value as JObject;
                if (idsObj == null || idsObj.Count == 0) return false;

                JProperty idProp = null;
                foreach (var it in idsObj.Properties())
                {
                    if (string.Equals(it.Name, "General", StringComparison.OrdinalIgnoreCase))
                    { idProp = it; break; }
                }
                if (idProp == null)
                {
                    var list = new List<JProperty>();
                    foreach (var it in idsObj.Properties()) list.Add(it);
                    idProp = list[UnityEngine.Random.Range(0, list.Count)];
                }
                if (idProp == null || string.IsNullOrEmpty(idProp.Name)) return false;

                netId = string.Equals(idProp.Name, "General", StringComparison.OrdinalIgnoreCase) ? null : idProp.Name;

                if (idProp.Value is JArray arr)
                {
                    if (arr.Count == 0) return false;
                    var idx = (arr.Count == 1) ? 0 : UnityEngine.Random.Range(0, arr.Count);
                    text = arr[idx]?.ToString()?.Trim();
                }
                else
                {
                    text = idProp.Value?.ToString()?.Trim();
                }

                return !string.IsNullOrEmpty(text);
            }
            catch { return false; }
        }

        public static void NormalizeTextRectForBackground(UnityEngine.UI.Text text)
        {
            try
            {
                if (text == null) return;
                var rt = text.rectTransform;
                // 居中锚点 + 居中 pivot，避免背景套上后文本有偏移
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }
            catch { }
        }

        private static string SaveCurrentSettingsToPresetFile(string rawName)
        {
            try
            {
                if (string.IsNullOrEmpty(s_PresetsDir))
                    s_PresetsDir = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "presets");
                if (!Directory.Exists(s_PresetsDir)) Directory.CreateDirectory(s_PresetsDir);

                string name = SanitizeFileNameSimple((rawName ?? "preset").Trim());
                if (string.IsNullOrEmpty(name)) name = "preset";
                string path = Path.Combine(s_PresetsDir, name + ".jsonc"); // 同名强制覆盖

                // ====== 构建 Setting（全部用 Setting.* 的“平铺键”）======
                var S = new JObject();
                for (int i = 0; i < s_SnapshotWriters.Count; i++)
                {
                    try { s_SnapshotWriters[i](S); } catch { }
                }

                // 只写一个最小结构：{ "Setting": { ... } }
                var root = new JObject();
                root["Setting"] = S;

                // pretty 格式写入，强制覆盖
                var txt = root.ToString(Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(path, txt, Encoding.UTF8);
                s_Log.LogInfo("[Settings] Preset saved: " + path);   // 绝对路径写日志
                var display = ShortPathFromBepInEx(path);            // ← 新增：转成 "BepInEx/..." 相对展示
                return display;
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Settings] Save preset failed: " + e);
                return null;
            }
        }

        private static string SanitizeFileNameSimple(string s)
        {
            var bad = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = true;
                for (int j = 0; j < bad.Length; j++) if (c == bad[j]) { ok = false; break; }
                if (ok) sb.Append(c);
            }
            var t = sb.ToString().Trim();
            return string.IsNullOrEmpty(t) ? "preset" : t;
        }

        public enum Channel { Subtitle, Danmaku, World3D }

        // 角色类别（补丁层会根据 IPlayer 判定归类）
        public enum RoleKind
        {
            Unknown,
            Player,
            Teammate,
            PmcBear, PmcUsec,
            Scav, Raider, Rogue, Cultist,
            BossFollower, Zombie, Goons, Bosses
        }

        public static int GetSubtitleRolePriority(RoleKind kind)
        {
            try
            {
                if (SubtitleRolePriorityEnabled != null && !SubtitleRolePriorityEnabled.Value)
                    return 50;

                ConfigEntry<int> entry;
                switch (kind)
                {
                    case RoleKind.Player:
                        entry = SubtitlePriorityPlayer;
                        break;
                    case RoleKind.Teammate:
                        entry = SubtitlePriorityTeammate;
                        break;
                    case RoleKind.PmcBear:
                    case RoleKind.PmcUsec:
                        entry = SubtitlePriorityPmc;
                        break;
                    case RoleKind.Scav:
                        entry = SubtitlePriorityScav;
                        break;
                    case RoleKind.Raider:
                    case RoleKind.Rogue:
                        entry = SubtitlePriorityRaiderRogue;
                        break;
                    case RoleKind.Cultist:
                        entry = SubtitlePriorityCultist;
                        break;
                    case RoleKind.BossFollower:
                        entry = SubtitlePriorityBossFollower;
                        break;
                    case RoleKind.Zombie:
                        entry = SubtitlePriorityZombie;
                        break;
                    case RoleKind.Goons:
                        entry = SubtitlePriorityGoons;
                        break;
                    case RoleKind.Bosses:
                        entry = SubtitlePriorityBosses;
                        break;
                    default:
                        entry = SubtitlePriorityOther;
                        break;
                }
                return entry != null ? Mathf.Clamp(entry.Value, 0, 100) : 50;
            }
            catch
            {
                return 50;
            }
        }

        // —— 颜色查找表：[渠道][角色] -> ConfigEntry（首次使用时构建；键集合与原 switch 完全一致）——
        private static Dictionary<Channel, Dictionary<RoleKind, ConfigEntry<Color>>> s_RoleColorEntries;
        private static Dictionary<Channel, Dictionary<RoleKind, ConfigEntry<Color>>> s_TextColorEntries;

        private static Dictionary<Channel, Dictionary<RoleKind, ConfigEntry<Color>>> GetRoleColorEntries()
        {
            if (s_RoleColorEntries == null)
            {
                s_RoleColorEntries = new Dictionary<Channel, Dictionary<RoleKind, ConfigEntry<Color>>>
                {
                    { Channel.Subtitle, new Dictionary<RoleKind, ConfigEntry<Color>>
                        {
                            { RoleKind.Player, SubRole_Player },
                            { RoleKind.Teammate, SubRole_Teammate },
                            { RoleKind.PmcBear, SubRole_PmcBear },
                            { RoleKind.PmcUsec, SubRole_PmcUsec },
                            { RoleKind.Scav, SubRole_Scav },
                            { RoleKind.Raider, SubRole_Raider },
                            { RoleKind.Rogue, SubRole_Rogue },
                            { RoleKind.Cultist, SubRole_Cultist },
                            { RoleKind.BossFollower, SubRole_BossFollower },
                            { RoleKind.Zombie, SubRole_Zombie },
                            { RoleKind.Goons, SubRole_Goons },
                            { RoleKind.Bosses, SubRole_Bosses },
                        } },
                    { Channel.Danmaku, new Dictionary<RoleKind, ConfigEntry<Color>>
                        {
                            { RoleKind.Player, DmRole_Player },
                            { RoleKind.Teammate, DmRole_Teammate },
                            { RoleKind.PmcBear, DmRole_PmcBear },
                            { RoleKind.PmcUsec, DmRole_PmcUsec },
                            { RoleKind.Scav, DmRole_Scav },
                            { RoleKind.Raider, DmRole_Raider },
                            { RoleKind.Rogue, DmRole_Rogue },
                            { RoleKind.Cultist, DmRole_Cultist },
                            { RoleKind.BossFollower, DmRole_BossFollower },
                            { RoleKind.Zombie, DmRole_Zombie },
                            { RoleKind.Goons, DmRole_Goons },
                            { RoleKind.Bosses, DmRole_Bosses },
                        } },
                    { Channel.World3D, new Dictionary<RoleKind, ConfigEntry<Color>>
                        {
                            { RoleKind.Player, W3dRole_Player },
                            { RoleKind.Teammate, W3dRole_Teammate },
                            { RoleKind.PmcBear, W3dRole_PmcBear },
                            { RoleKind.PmcUsec, W3dRole_PmcUsec },
                            { RoleKind.Scav, W3dRole_Scav },
                            { RoleKind.Raider, W3dRole_Raider },
                            { RoleKind.Rogue, W3dRole_Rogue },
                            { RoleKind.Cultist, W3dRole_Cultist },
                            { RoleKind.BossFollower, W3dRole_BossFollower },
                            { RoleKind.Zombie, W3dRole_Zombie },
                            { RoleKind.Goons, W3dRole_Goons },
                            { RoleKind.Bosses, W3dRole_Bosses },
                        } },
                };
            }
            return s_RoleColorEntries;
        }

        private static Dictionary<Channel, Dictionary<RoleKind, ConfigEntry<Color>>> GetTextColorEntries()
        {
            if (s_TextColorEntries == null)
            {
                s_TextColorEntries = new Dictionary<Channel, Dictionary<RoleKind, ConfigEntry<Color>>>
                {
                    { Channel.Subtitle, new Dictionary<RoleKind, ConfigEntry<Color>>
                        {
                            { RoleKind.Player, SubText_Player },
                            { RoleKind.Teammate, SubText_Teammate },
                            { RoleKind.PmcBear, SubText_PmcBear },
                            { RoleKind.PmcUsec, SubText_PmcUsec },
                            { RoleKind.Scav, SubText_Scav },
                            { RoleKind.Raider, SubText_Raider },
                            { RoleKind.Rogue, SubText_Rogue },
                            { RoleKind.Cultist, SubText_Cultist },
                            { RoleKind.BossFollower, SubText_BossFollower },
                            { RoleKind.Zombie, SubText_Zombie },
                            { RoleKind.Goons, SubText_Goons },
                            { RoleKind.Bosses, SubText_Bosses },
                        } },
                    { Channel.Danmaku, new Dictionary<RoleKind, ConfigEntry<Color>>
                        {
                            { RoleKind.Player, DmText_Player },
                            { RoleKind.Teammate, DmText_Teammate },
                            { RoleKind.PmcBear, DmText_PmcBear },
                            { RoleKind.PmcUsec, DmText_PmcUsec },
                            { RoleKind.Scav, DmText_Scav },
                            { RoleKind.Raider, DmText_Raider },
                            { RoleKind.Rogue, DmText_Rogue },
                            { RoleKind.Cultist, DmText_Cultist },
                            { RoleKind.BossFollower, DmText_BossFollower },
                            { RoleKind.Zombie, DmText_Zombie },
                            { RoleKind.Goons, DmText_Goons },
                            { RoleKind.Bosses, DmText_Bosses },
                        } },
                    { Channel.World3D, new Dictionary<RoleKind, ConfigEntry<Color>>
                        {
                            { RoleKind.Player, W3dText_Player },
                            { RoleKind.Teammate, W3dText_Teammate },
                            { RoleKind.PmcBear, W3dText_PmcBear },
                            { RoleKind.PmcUsec, W3dText_PmcUsec },
                            { RoleKind.Scav, W3dText_Scav },
                            { RoleKind.Raider, W3dText_Raider },
                            { RoleKind.Rogue, W3dText_Rogue },
                            { RoleKind.Cultist, W3dText_Cultist },
                            { RoleKind.BossFollower, W3dText_BossFollower },
                            { RoleKind.Zombie, W3dText_Zombie },
                            { RoleKind.Goons, W3dText_Goons },
                            { RoleKind.Bosses, W3dText_Bosses },
                        } },
                };
            }
            return s_TextColorEntries;
        }

        private static Color LookupColor(Dictionary<Channel, Dictionary<RoleKind, ConfigEntry<Color>>> table, RoleKind kind, Channel ch)
        {
            Dictionary<RoleKind, ConfigEntry<Color>> byKind;
            ConfigEntry<Color> entry;
            if (table != null && table.TryGetValue(ch, out byKind) && byKind != null &&
                byKind.TryGetValue(kind, out entry) && entry != null)
                return entry.Value;
            return Color.white; // 没命中就回退纯白
        }

        // 角色名前缀颜色
        public static Color GetRoleColor(RoleKind kind, Channel ch)
        {
            try
            {
                return LookupColor(GetRoleColorEntries(), kind, ch);
            }
            catch { }
            return Color.white; // 没命中就回退纯白
        }

        // 正文颜色（整行）
        public static Color GetTextColor(RoleKind kind, Channel ch)
        {
            try
            {
                return LookupColor(GetTextColorEntries(), kind, ch);
            }
            catch { }
            return Color.white; // 没命中就回退纯白
        }

        // 把“角色名：”包上颜色（供补丁层直接调用）
        public static string WrapRoleTag(string roleTag, RoleKind kind, Channel ch)
        {
            try
            {
                var c = GetRoleColor(kind, ch);
                string hex = ColorUtility.ToHtmlStringRGB(c);
                return "<color=#" + hex + ">" + roleTag + "</color>";
            }
            catch { return roleTag; }
        }
    }
}
