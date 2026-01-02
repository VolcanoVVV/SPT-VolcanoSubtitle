// 文件：Subtitle/Config/Settings.Methods.cs
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using Subtitle.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Subtitle.Config
{
    internal partial class Settings
    {
        private static string s_PresetUiHint;
        private static float s_PresetUiHintUntil;
        private static GUIStyle s_HintStyleGreen;

        // —— 展开/收缩：由主开关控制可见性 —— //
        private static readonly List<ConfigEntryBase> s_SubtitleFoldTargets = new List<ConfigEntryBase>();
        private static readonly List<ConfigEntryBase> s_DanmakuFoldTargets = new List<ConfigEntryBase>();
        private static readonly List<ConfigEntryBase> s_World3DFoldTargets = new List<ConfigEntryBase>();
        private static readonly Dictionary<ConfigEntryBase, bool?> s_FoldBrowsableBackup =
            new Dictionary<ConfigEntryBase, bool?>();

        // ★ 新增：固定占位 & 保存 UI 的状态
        private const float PRESET_HINT_MIN_HEIGHT = 22f;
        private static bool s_SavePresetMode = false;
        private static string s_SavePresetInput = "";

        private const float FONT_BUNDLE_HINT_MIN_HEIGHT = 18f;
        private static List<string> s_FontBundleNames = new List<string>();
        private static int s_SelectedFontBundleIndex = 0;
        private static bool s_FontBundleListLoaded = false;
        private static string s_FontBundleUiHint;
        private static float s_FontBundleUiHintUntil;
        private static GUIStyle s_FontBundleHintStyle;
        private static readonly Dictionary<ConfigEntryBase, int> s_FontBundleSelection =
            new Dictionary<ConfigEntryBase, int>();
        private static readonly Dictionary<ConfigEntryBase, string> s_FontBundleSelectionValue =
            new Dictionary<ConfigEntryBase, string>();

        // ① 共享的写入/读取动作表
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
        private static void RegEnum<T>(string key, BepInEx.Configuration.ConfigEntry<T> e) where T : struct, IConvertible
        {
            if (e == null) return;
            s_SnapshotWriters.Add(S => { S[key] = e.Value.ToString(); });
            s_SnapshotReaders.Add(S =>
            {
                var t = PickKey(S, key);
                if (t == null) return;
                try
                {
                    var s = t.Value<string>();
                    T v;
                    if (Enum.TryParse<T>(s, true, out v))
                        e.Value = v;
                }
                catch { }
            });
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

        private static void ShowPresetUiHint(string msg, float seconds = 2f)
        {
            s_PresetUiHint = msg ?? "";
            s_PresetUiHintUntil = Time.realtimeSinceStartup + Mathf.Max(0.5f, seconds);
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

                const string fallbackPreset = "default";
                var pick = string.IsNullOrEmpty(name) ? fallbackPreset : name;
                var path = Path.Combine(s_PresetsDir, pick + ".jsonc");
                if (!File.Exists(path))
                {
                    s_Log.LogWarning("[Settings] Preset file not found: " + path + ", fallback to " + fallbackPreset + ".");
                    pick = fallbackPreset;
                    path = Path.Combine(s_PresetsDir, fallbackPreset + ".jsonc");
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

        private static string MapVoiceKeyLabelLocal(string vk)
        {
            if (string.IsNullOrEmpty(vk)) return "Voice";
            string mapped;
            EnsureUserRoleMapLoaded();
            if (s_UserRoleMapExact != null && s_UserRoleMapExact.TryGetValue(vk, out mapped) && !string.IsNullOrEmpty(mapped))
                return mapped;
            if (SubtitleEnum.DEFAULT_VOICE_KEY_LABELS.TryGetValue(vk, out mapped) && !string.IsNullOrEmpty(mapped))
                return mapped;
            try { return vk.Replace('_', '-').ToUpperInvariant(); } catch { return "Voice"; }
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
                if (!list.Exists(n => string.Equals(n, "default", StringComparison.OrdinalIgnoreCase)))
                    list.Insert(0, "default");

                s_PresetNames = list;
                s_PresetListLoaded = true;

                if (resetSelectionToCurrent)
                {
                    var cur = TextPresetName != null ? (TextPresetName.Value ?? "default") : "default";
                    int idx = s_PresetNames.FindIndex(n => string.Equals(n, cur, StringComparison.OrdinalIgnoreCase));
                    s_SelectedPresetIndex = idx >= 0 ? idx : 0;
                }
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Settings] ScanPresets failed: " + e);
                s_PresetNames = new List<string> { "default" };
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

        // ===================== 展开/收缩（Browsable） =====================
        private static void RegisterFoldBindings()
        {
            BuildFoldTargets();

            ApplyFoldState(ShowSubtitleOptions == null || ShowSubtitleOptions.Value, s_SubtitleFoldTargets);
            ApplyFoldState(ShowDanmakuOptions == null || ShowDanmakuOptions.Value, s_DanmakuFoldTargets);
            ApplyFoldState(ShowWorld3DOptions == null || ShowWorld3DOptions.Value, s_World3DFoldTargets);

            if (ShowSubtitleOptions != null)
                ShowSubtitleOptions.SettingChanged += (s, e) => ApplyFoldState(ShowSubtitleOptions.Value, s_SubtitleFoldTargets);
            if (ShowDanmakuOptions != null)
                ShowDanmakuOptions.SettingChanged += (s, e) => ApplyFoldState(ShowDanmakuOptions.Value, s_DanmakuFoldTargets);
            if (ShowWorld3DOptions != null)
                ShowWorld3DOptions.SettingChanged += (s, e) => ApplyFoldState(ShowWorld3DOptions.Value, s_World3DFoldTargets);
        }

        private static void BuildFoldTargets()
        {
            s_SubtitleFoldTargets.Clear();
            s_DanmakuFoldTargets.Clear();
            s_World3DFoldTargets.Clear();

            // TODO: 在此添加需要被 EnableSubtitle 控制显示/隐藏的选项
            // AddFoldTarget(s_SubtitleFoldTargets, SubtitleShowRoleTag);
            // AddFoldTarget(s_SubtitleFoldTargets, SubtitleShowPmcName);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleShowRoleTag);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleShowPmcName);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleShowScavName);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitlePlayerSelfPronoun);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleTeammateSelfPronoun);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleMaxDistanceMeters);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleShowDistance);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleDisplayDelaySec);
            AddFoldTarget(s_SubtitleFoldTargets, EnableMapBroadcastSubtitle);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleZombieEnabled);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleZombieCooldownSec);

            // —— Subtitle - Advanced ——//
            // 字体
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleFontBundleName);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleFontFamilyCsv);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleFontSize);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleFontBold);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleFontItalic);

            // 文本对齐 & 换行
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleAlignment);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleWrap);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleWrapLength);

            // 描边
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleOutlineEnabled);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleOutlineColor);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleOutlineDistX);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleOutlineDistY);

            // 阴影
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleShadowEnabled);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleShadowColor);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleShadowDistX);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleShadowDistY);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleShadowUseGraphicAlpha);

            // 布局（LayoutSpec）
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleLayoutAnchor);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleLayoutOffsetX);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleLayoutOffsetY);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleLayoutSafeArea);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleLayoutMaxWidthPercent);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleLayoutLineSpacing);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleLayoutOverrideAlign);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleLayoutStackOffsetPercent);

            // 背景（BackgroundSpec）
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgEnabled);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgFit);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgColor);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgPaddingX);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgPaddingY);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgMarginY);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgSprite);

            // 背景阴影
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgShadowEnabled);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgShadowColor);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgShadowDistX);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgShadowDistY);
            AddFoldTarget(s_SubtitleFoldTargets, SubtitleBgShadowUseGraphicAlpha);

            AddFoldTarget(s_SubtitleFoldTargets, SubRole_Player);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_Teammate);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_PmcBear);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_PmcUsec);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_Scav);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_Raider);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_Rogue);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_Cultist);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_BossFollower);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_Zombie);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_Goons);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_Bosses);
            AddFoldTarget(s_SubtitleFoldTargets, SubRole_LabAnnouncer);

            // ===== 颜色 · 正文颜色（字幕） =====
            AddFoldTarget(s_SubtitleFoldTargets, SubText_Player);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_Teammate);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_PmcBear);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_PmcUsec);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_Scav);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_Raider);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_Rogue);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_Cultist);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_BossFollower);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_Zombie);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_Goons);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_Bosses);
            AddFoldTarget(s_SubtitleFoldTargets, SubText_LabAnnouncer);

            // TODO: 在此添加需要被 EnableDanmaku 控制显示/隐藏的选项
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuLanes);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuSpeed);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuMinGapPx);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuSpawnDelaySec);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuFontSize);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuTopOffsetPercent);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuAreaMaxPercent);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuShowRoleTag);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuShowPmcName);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuShowScavName);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuPlayerSelfPronoun);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuTeammateSelfPronoun);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuMaxDistanceMeters);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuShowDistance);
            AddFoldTarget(s_DanmakuFoldTargets, EnableMapBroadcastDanmaku);

            AddFoldTarget(s_DanmakuFoldTargets, DanmakuZombieEnabled);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuZombieCooldownSec);

            // —— Danmaku - Advanced —— //
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuFontBundleName);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuFontFamilyCsv);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuFontBold);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuFontItalic);

            AddFoldTarget(s_DanmakuFoldTargets, DanmakuOutlineEnabled);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuOutlineColor);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuOutlineDistX);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuOutlineDistY);

            AddFoldTarget(s_DanmakuFoldTargets, DanmakuShadowEnabled);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuShadowColor);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuShadowDistX);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuShadowDistY);
            AddFoldTarget(s_DanmakuFoldTargets, DanmakuShadowUseGraphicAlpha);

            // ===== 颜色 · 角色名颜色（弹幕） =====
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_Player);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_Teammate);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_PmcBear);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_PmcUsec);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_Scav);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_Raider);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_Rogue);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_Cultist);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_BossFollower);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_Zombie);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_Goons);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_Bosses);
            AddFoldTarget(s_DanmakuFoldTargets, DmRole_LabAnnouncer);

            // ===== 颜色 · 正文颜色（弹幕） =====
            AddFoldTarget(s_DanmakuFoldTargets, DmText_Player);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_Teammate);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_PmcBear);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_PmcUsec);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_Scav);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_Raider);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_Rogue);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_Cultist);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_BossFollower);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_Zombie);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_Goons);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_Bosses);
            AddFoldTarget(s_DanmakuFoldTargets, DmText_LabAnnouncer);

            // —— World3D —— //
            AddFoldTarget(s_World3DFoldTargets, World3DShowRoleTag);
            AddFoldTarget(s_World3DFoldTargets, World3DShowPmcName);
            AddFoldTarget(s_World3DFoldTargets, World3DShowScavName);
            AddFoldTarget(s_World3DFoldTargets, World3DPlayerSelfPronoun);
            AddFoldTarget(s_World3DFoldTargets, World3DTeammateSelfPronoun);
            AddFoldTarget(s_World3DFoldTargets, World3DMaxDistanceMeters);
            AddFoldTarget(s_World3DFoldTargets, World3DShowDistance);
            AddFoldTarget(s_World3DFoldTargets, World3DDisplayDelaySec);
            AddFoldTarget(s_World3DFoldTargets, World3DVerticalOffsetY);
            AddFoldTarget(s_World3DFoldTargets, World3DFacePlayer);
            AddFoldTarget(s_World3DFoldTargets, World3DBGEnabled);
            AddFoldTarget(s_World3DFoldTargets, World3DBGColor);
            AddFoldTarget(s_World3DFoldTargets, World3DShowSelf);
            AddFoldTarget(s_World3DFoldTargets, World3DZombieEnabled);
            AddFoldTarget(s_World3DFoldTargets, World3DZombieCooldownSec);

            // —— World3D - Advanced —— //
            AddFoldTarget(s_World3DFoldTargets, World3DFontBundleName);
            AddFoldTarget(s_World3DFoldTargets, World3DFontFamilyCsv);
            AddFoldTarget(s_World3DFoldTargets, World3DFontSize);
            AddFoldTarget(s_World3DFoldTargets, World3DFontBold);
            AddFoldTarget(s_World3DFoldTargets, World3DFontItalic);
            AddFoldTarget(s_World3DFoldTargets, World3DAlignment);
            AddFoldTarget(s_World3DFoldTargets, World3DWrap);
            AddFoldTarget(s_World3DFoldTargets, World3DWrapLength);
            AddFoldTarget(s_World3DFoldTargets, World3DWorldScale);
            AddFoldTarget(s_World3DFoldTargets, World3DDynamicPixelsPerUnit);
            AddFoldTarget(s_World3DFoldTargets, World3DFaceUpdateIntervalSec);
            AddFoldTarget(s_World3DFoldTargets, World3DStackMaxLines);
            AddFoldTarget(s_World3DFoldTargets, World3DStackOffsetY);
            AddFoldTarget(s_World3DFoldTargets, World3DFadeInSec);
            AddFoldTarget(s_World3DFoldTargets, World3DFadeOutSec);
            AddFoldTarget(s_World3DFoldTargets, World3DOutlineEnabled);
            AddFoldTarget(s_World3DFoldTargets, World3DOutlineColor);
            AddFoldTarget(s_World3DFoldTargets, World3DOutlineDistX);
            AddFoldTarget(s_World3DFoldTargets, World3DOutlineDistY);
            AddFoldTarget(s_World3DFoldTargets, World3DShadowEnabled);
            AddFoldTarget(s_World3DFoldTargets, World3DShadowColor);
            AddFoldTarget(s_World3DFoldTargets, World3DShadowDistX);
            AddFoldTarget(s_World3DFoldTargets, World3DShadowDistY);
            AddFoldTarget(s_World3DFoldTargets, World3DShadowUseGraphicAlpha);

            AddFoldTarget(s_World3DFoldTargets, W3dRole_Player);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_Teammate);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_PmcBear);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_PmcUsec);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_Scav);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_Raider);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_Rogue);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_Cultist);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_BossFollower);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_Zombie);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_Goons);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_Bosses);
            AddFoldTarget(s_World3DFoldTargets, W3dRole_LabAnnouncer);

            AddFoldTarget(s_World3DFoldTargets, W3dText_Player);
            AddFoldTarget(s_World3DFoldTargets, W3dText_Teammate);
            AddFoldTarget(s_World3DFoldTargets, W3dText_PmcBear);
            AddFoldTarget(s_World3DFoldTargets, W3dText_PmcUsec);
            AddFoldTarget(s_World3DFoldTargets, W3dText_Scav);
            AddFoldTarget(s_World3DFoldTargets, W3dText_Raider);
            AddFoldTarget(s_World3DFoldTargets, W3dText_Rogue);
            AddFoldTarget(s_World3DFoldTargets, W3dText_Cultist);
            AddFoldTarget(s_World3DFoldTargets, W3dText_BossFollower);
            AddFoldTarget(s_World3DFoldTargets, W3dText_Zombie);
            AddFoldTarget(s_World3DFoldTargets, W3dText_Goons);
            AddFoldTarget(s_World3DFoldTargets, W3dText_Bosses);
            AddFoldTarget(s_World3DFoldTargets, W3dText_LabAnnouncer);

        }

        private static void AddFoldTarget(List<ConfigEntryBase> list, ConfigEntryBase entry)
        {
            if (list == null || entry == null) return;
            if (!list.Contains(entry)) list.Add(entry);
        }

        private static void ApplyFoldState(bool show, List<ConfigEntryBase> targets)
        {
            if (targets == null) return;
            bool changed = false;
            for (int i = 0; i < targets.Count; i++)
            {
                var entry = targets[i];
                var attrs = GetCmAttributes(entry);
                if (attrs == null) continue;

                if (!s_FoldBrowsableBackup.TryGetValue(entry, out var original))
                {
                    original = attrs.Browsable;
                    s_FoldBrowsableBackup[entry] = original;
                }

                if (show)
                {
                    bool next = original ?? true;
                    if (attrs.Browsable != next) changed = true;
                    attrs.Browsable = next;
                }
                else
                {
                    if (attrs.Browsable != false) changed = true;
                    attrs.Browsable = false;
                }
            }

            if (changed)
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
                var field = typeof(ConfigEntryBase).GetField("<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                    field.SetValue(entry, newDesc);
            }
            catch { }
        }

        private static void TryRefreshConfigurationManager()
        {
            try
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
                    if (InvokeConfigManagerMethod(cmType, instance, methods[i]))
                        return;
                }
            }
            catch { }
        }

        private static bool InvokeConfigManagerMethod(Type cmType, object instance, string methodName)
        {
            var method = cmType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) return false;
            var pars = method.GetParameters();
            if (pars.Length == 0)
            {
                method.Invoke(instance, null);
                return true;
            }
            if (pars.Length == 1 && pars[0].ParameterType == typeof(bool))
            {
                method.Invoke(instance, new object[] { true });
                return true;
            }
            return false;
        }

        private static Type FindConfigurationManagerType()
        {
            Type cmType = Type.GetType("ConfigurationManager.ConfigurationManager, ConfigurationManager");
            if (cmType != null) return cmType;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    var asm = assemblies[i];
                    cmType = asm.GetType("ConfigurationManager.ConfigurationManager");
                    if (cmType != null) return cmType;

                    var types = asm.GetTypes();
                    for (int t = 0; t < types.Length; t++)
                    {
                        var type = types[t];
                        if (type == null) continue;
                        if (!string.Equals(type.Name, "ConfigurationManager", StringComparison.Ordinal)) continue;
                        if (type.GetMethod("BuildSettingList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null ||
                            type.GetMethod("RefreshSettingList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null ||
                            type.GetMethod("UpdateSettingList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
                            return type;
                    }
                }
                catch { }
            }
            return null;
        }

        // ===================== 自绘 UI =====================
        private static void DrawPresetPicker(ConfigEntryBase entry)
        {
            if (!s_PresetListLoaded) ScanPresets(true);

            // ★ 新增：用竖向容器包住整块区域（父级只把它当作一个控件）
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            // 原有的横排：选择器 + 刷新 + 应用
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("◀", GUILayout.Width(28)))
            {
                if (s_PresetNames.Count > 0)
                    s_SelectedPresetIndex = (s_SelectedPresetIndex - 1 + s_PresetNames.Count) % s_PresetNames.Count;
            }

            var label = (s_PresetNames.Count > 0 && s_SelectedPresetIndex >= 0 && s_SelectedPresetIndex < s_PresetNames.Count)
                ? s_PresetNames[s_SelectedPresetIndex]
                : "(无预设)";
            GUILayout.Label(label, GUILayout.ExpandWidth(true));

            if (GUILayout.Button("▶", GUILayout.Width(28)))
            {
                if (s_PresetNames.Count > 0)
                    s_SelectedPresetIndex = (s_SelectedPresetIndex + 1) % s_PresetNames.Count;
            }

            if (GUILayout.Button("刷新", GUILayout.Width(64)))
            {
                ScanPresets(true);
                s_Log.LogInfo("[Settings] Preset list refreshed. Count=" + s_PresetNames.Count);
                ShowPresetUiHint($"已刷新预设：{s_PresetNames.Count} 个");
                PushClientToast($"已刷新预设：{s_PresetNames.Count} 个");
            }

            if (GUILayout.Button("应用", GUILayout.Width(64)))
            {
                if (s_PresetNames.Count > 0)
                {
                    var pick = s_PresetNames[s_SelectedPresetIndex];
                    ApplyPresetByName(pick);
                    ShowPresetUiHint($"已应用预设：{pick}");
                    PushClientToast($"已应用预设：{pick}");
                }
            }

            GUILayout.EndHorizontal(); // —— 横排结束 ——

            // —— 保存当前预设设置 —— 
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();

            if (!s_SavePresetMode)
            {
                if (GUILayout.Button("保存预设", GUILayout.Width(70)))
                {
                    // 初始建议名：当前选择或 default
                    s_SavePresetInput = (s_PresetNames != null && s_PresetNames.Count > 0 && s_SelectedPresetIndex >= 0 && s_SelectedPresetIndex < s_PresetNames.Count)
                        ? s_PresetNames[s_SelectedPresetIndex]
                        : (TextPresetName != null ? (TextPresetName.Value ?? "default") : "default");
                    s_SavePresetMode = true;
                }
            }
            else
            {
                GUILayout.Label("预设名：", GUILayout.Width(52));
                s_SavePresetInput = GUILayout.TextField(s_SavePresetInput ?? "", GUILayout.MinWidth(140));
                if (GUILayout.Button("确定", GUILayout.Width(52)))
                {
                    string savedPath = SaveCurrentSettingsToPresetFile(s_SavePresetInput);
                    if (!string.IsNullOrEmpty(savedPath))
                    {
                        ShowPresetUiHint("已成功保存预设文件");
                        PushClientToast($"已成功保存预设文件，位于 {savedPath}");
                        ScanPresets(true);
                    }
                    else
                    {
                        ShowPresetUiHint("保存失败，请查看日志");
                    }
                    s_SavePresetMode = false;
                }
                if (GUILayout.Button("取消", GUILayout.Width(52)))
                {
                    s_SavePresetMode = false;
                }
            }

            GUILayout.EndHorizontal();

            // ★ 新增：提示文字独占新的一行（在竖向容器内就是下一行）
            // —— 提示行：永久占位，不抖动 —— 
            if (s_HintStyleGreen == null)
            {
                s_HintStyleGreen = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft
                };
                s_HintStyleGreen.normal.textColor = new Color(0.7f, 1f, 0.7f, 1f);
            }

            GUILayout.Space(4);
            string hintMsg = (s_PresetUiHintUntil > 0 && Time.realtimeSinceStartup < s_PresetUiHintUntil)
                ? ("✓ " + s_PresetUiHint)
                : " "; // 空白占位
            GUILayout.Label(hintMsg, s_HintStyleGreen, GUILayout.ExpandWidth(true), GUILayout.MinHeight(PRESET_HINT_MIN_HEIGHT));

            GUILayout.EndVertical();   // —— 竖向容器结束 —— 
        }

        private static void DrawFontBundlePicker(ConfigEntryBase entry)
        {
            var e = entry as ConfigEntry<string>;
            if (e == null) return;

            if (!s_FontBundleListLoaded) ScanFontBundles(true, e.Value);

            int currentIndex = EnsureFontBundleSelection(entry, e.Value);

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("<", GUILayout.Width(28)))
            {
                if (s_FontBundleNames.Count > 0)
                    currentIndex = (currentIndex - 1 + s_FontBundleNames.Count) % s_FontBundleNames.Count;
            }

            UpdateFontBundleSelection(entry, currentIndex, e.Value);

            string label = (s_FontBundleNames.Count > 0 && currentIndex >= 0 && currentIndex < s_FontBundleNames.Count)
                ? FormatFontBundleLabel(s_FontBundleNames[currentIndex])
                : "(无字体)";
            GUILayout.Label(label, GUILayout.ExpandWidth(true));

            if (GUILayout.Button(">", GUILayout.Width(28)))
            {
                if (s_FontBundleNames.Count > 0)
                    currentIndex = (currentIndex + 1) % s_FontBundleNames.Count;
            }

            UpdateFontBundleSelection(entry, currentIndex, e.Value);

            if (GUILayout.Button("刷新", GUILayout.Width(64)))
            {
                ScanFontBundles(true, e.Value);
                currentIndex = EnsureFontBundleSelection(entry, e.Value);
                ShowFontBundleUiHint("已刷新字体资源包： " + s_FontBundleNames.Count + " 个");
            }

            if (GUILayout.Button("应用", GUILayout.Width(64)))
            {
                if (s_FontBundleNames.Count > 0)
                {
                    var pick = s_FontBundleNames[currentIndex];
                    e.Value = pick;
                    UpdateFontBundleSelection(entry, currentIndex, e.Value);
                    ShowFontBundleUiHint("已应用： " + FormatFontBundleLabel(pick));
                    TryRefreshByFontBundleEntry(e);
                }
            }

            GUILayout.EndHorizontal();

            if (s_FontBundleHintStyle == null)
            {
                s_FontBundleHintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft
                };
                s_FontBundleHintStyle.normal.textColor = new Color(0.7f, 1f, 0.7f, 1f);
            }

            GUILayout.Space(4);
            string dir = GetFontBundleDir();
            bool dirOk = !string.IsNullOrEmpty(dir) && Directory.Exists(dir);
            string hintMsg = (s_FontBundleUiHintUntil > 0 && Time.realtimeSinceStartup < s_FontBundleUiHintUntil)
                ? (s_FontBundleUiHint)
                : (dirOk ? " " : ("未检测到字体目录： " + ShortPathFromBepInEx(dir)));
            GUILayout.Label(hintMsg, s_FontBundleHintStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(FONT_BUNDLE_HINT_MIN_HEIGHT));

            GUILayout.EndVertical();
        }

        private static void DrawFoldToggleButton(ConfigEntryBase entry)
        {
            var e = entry as ConfigEntry<bool>;
            if (e == null) return;

            string title = entry.Definition != null ? entry.Definition.Key : "展开/收缩";
            string label = title + " " + (e.Value ? "收起" : "展开");
            if (GUILayout.Button(label, GUILayout.Height(24)))
            {
                e.Value = !e.Value;
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
            s_SelectedFontBundleIndex = idx;
        }

        private static void TryRefreshByFontBundleEntry(ConfigEntry<string> e)
        {
            if (e == null) return;
            if (ReferenceEquals(e, SubtitleFontBundleName)) { TryRefreshSubtitleStyleRuntime(); return; }
            if (ReferenceEquals(e, DanmakuFontBundleName)) { TryRefreshDanmakuStyleRuntime(); return; }
            if (ReferenceEquals(e, World3DFontBundleName)) { TryRefreshWorld3DStyleRuntime(); return; }
        }

        private static string FormatFontBundleLabel(string name)
        {
            return string.IsNullOrEmpty(name) ? "(不覆盖)" : name;
        }

        private static void ShowFontBundleUiHint(string msg, float seconds = 2f)
        {
            s_FontBundleUiHint = msg ?? "";
            s_FontBundleUiHintUntil = Time.realtimeSinceStartup + Mathf.Max(0.5f, seconds);
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

        private static void ScanFontBundles(bool resetSelectionToCurrent, string current)
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
                if (resetSelectionToCurrent)
                {
                    s_SelectedFontBundleIndex = GetFontBundleIndex(current);
                }
            }
            catch (Exception e)
            {
                s_Log.LogWarning("[Settings] ScanFontBundles failed: " + e);
                s_FontBundleNames = new List<string> { "" };
                s_SelectedFontBundleIndex = 0;
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

        private static void DrawTestSubtitleButton(ConfigEntryBase entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▶ 随机测试字幕", GUILayout.Height(24), GUILayout.Width(260)))
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
                        string voiceKey, line;
                        if (!TryPickRandomAllowedLine("Subtitle", out voiceKey, out line))
                        {
                            s_Log.LogWarning("[Subtitle] 未找到可用的本地台词文件，改用占位文本。");
                            voiceKey = "_default";
                            line = "（占位）这是随机测试字幕。";
                        }

                        string aiType = GetRandomAiTypeForTest(voiceKey);
                        var kind = GuessRoleKindFromAiType(aiType);   // ★ 新增辅助：见下一个小节
                        string roleName = MapAITypeLabelLocal(aiType);
                        string roleTag = roleName + "：";

                        bool showRole = SubtitleShowRoleTag != null ? SubtitleShowRoleTag.Value : true;
                        bool showDist = SubtitleShowDistance != null ? SubtitleShowDistance.Value : true;
                        int randM = UnityEngine.Random.Range(10, 151);
                        string distSuffix = showDist ? (" <b>·</b>" + randM + "m") : "";

                        string shown = showRole ? (WrapRoleTag(roleTag, kind, Channel.Subtitle) + line) : line;
                        shown += distSuffix;

                        var textColor = GetTextColor(kind, Channel.Subtitle);
                        mgr.AddSubtitle(shown, textColor, 3.0f);
                    }
                }
                catch (Exception e)
                {
                    s_Log.LogWarning("[Subtitle] TestSubtitle random failed: " + e);
                }
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawPhraseFilterPanelButton(ConfigEntryBase entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("打开台词控制面板", GUILayout.Height(24), GUILayout.Width(260)))
            {
                try { PhraseFilterPanel.ToggleVisible(); } catch { }
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawTestDanmakuButton(ConfigEntryBase entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▶ 随机测试弹幕（3条）", GUILayout.Height(24), GUILayout.Width(260)))
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

                        bool showRoleDm = DanmakuShowRoleTag != null ? DanmakuShowRoleTag.Value : true;
                        bool showDistDm = DanmakuShowDistance != null ? DanmakuShowDistance.Value : true;

                        for (int i = 0; i < 3; i++)
                        {
                            string voiceKey, line;
                            if (!TryPickRandomAllowedLine("Danmaku", out voiceKey, out line))
                            {
                                voiceKey = "_default";
                                line = "（占位）这是随机测试弹幕。";
                            }

                            string aiType = GetRandomAiTypeForTest(voiceKey);
                            var kind = GuessRoleKindFromAiType(aiType);
                            string roleName = MapAITypeLabelLocal(aiType);
                            string roleTag = roleName + "：";

                            int randM = UnityEngine.Random.Range(10, 151);
                            string distSuffix = showDistDm ? (" <b>·</b>" + randM + "m") : "";

                            string shown = showRoleDm ? (WrapRoleTag(roleTag, kind, Channel.Danmaku) + line) : line;
                            shown += distSuffix;

                            var textColor = GetTextColor(kind, Channel.Danmaku);
                            mgr.AddDanmaku(shown, textColor);
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
            GUILayout.EndHorizontal();
        }


        // —— 硬编码的 AI 类型别名列表（全部小写；支持 * 前缀通配）——
        private static readonly string[] kRogueAliasesLC = { "exusec" };
        private static readonly string[] kRaiderAliasesLC = { "pmcbot" };
        private static readonly string[] kScavAliasesLC = {
        "assault",
        "cursedassault",
        "crazyassaultevent",
        "skier",
        "peacemaker",
        "arenafighterevent"
    };
        private static readonly string[] kCultistAliasesLC = {
        "sectantpriest",
        "sectantwarrior"
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
        "followergluharassault",
        "followergluharscout",
        "followergluharsecurity",
        "followerboarclose2",
        "followerboar",
        "bossboarsniper",
        "followerkolontayassault",
        "followerkolontaysecurity",
        "followerbully",
        "followersanitar",
        "followerkojaniy",
        "followerzryachiy",
        "tagillahelperagro",
        "blackDivision"
    };
        private static readonly string[] kZombieAliasesLC = {
        "infectedpmc",
        "infectedlaborant",
        "infectedassault",
        "infectedcivil"
    };
        private static readonly string[] kBossAliasesLC = {
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
        "ravangezryachiyevent"
    };

        public static RoleKind GuessRoleKindFromAiType(string aiType)
        {
            try
            {
                if (string.IsNullOrEmpty(aiType)) return RoleKind.Player;
                var t = aiType.ToLowerInvariant();

                // 先查你搬过来的别名表
                if (In(t, kRogueAliasesLC)) return RoleKind.Rogue;
                if (In(t, kRaiderAliasesLC)) return RoleKind.Raider;
                if (In(t, kScavAliasesLC)) return RoleKind.Scav;
                if (In(t, kCultistAliasesLC)) return RoleKind.Cultist;
                if (In(t, kGoonsAliasesLC)) return RoleKind.Goons;
                if (In(t, kBossFollowerAliasesLC)) return RoleKind.BossFollower;
                if (In(t, kZombieAliasesLC)) return RoleKind.Zombie;
                if (In(t, kBossAliasesLC)) return RoleKind.Bosses;

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
            return RoleKind.Player;
        }

        // ===================== 构建：字体/布局/背景 =====================
        public static SubtitleSystem.FontSpec BuildSubtitleFontSpec()
        {
            var spec = new SubtitleSystem.FontSpec();
            try
            {
                var csv = SubtitleFontFamilyCsv != null ? (SubtitleFontFamilyCsv.Value ?? "") : "";
                var list = new List<string>();
                var bundle = SubtitleFontBundleName != null ? (SubtitleFontBundleName.Value ?? "") : "";
                if (!string.IsNullOrEmpty(bundle))
                    list.Add("bundle:" + bundle);
                if (!string.IsNullOrEmpty(csv))
                {
                    var arr = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var s = arr[i].Trim();
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                }
                spec.family = list;
                spec.size = SubtitleFontSize != null ? Math.Max(12, SubtitleFontSize.Value) : 26;
                spec.bold = SubtitleFontBold != null && SubtitleFontBold.Value;
                spec.italic = SubtitleFontItalic != null && SubtitleFontItalic.Value;

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
                s.grow = "both";
                s.bias = 0.5f;
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
                b.cornerRadius = 8;
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
                if (f != null) text.font = f;

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

            try
            {
                var go = text.gameObject;
                var ol = go.GetComponent<Outline>();
                if (SubtitleOutlineEnabled != null && SubtitleOutlineEnabled.Value)
                {
                    if (ol == null) ol = go.AddComponent<Outline>();
                    ol.useGraphicAlpha = true;
                    if (SubtitleOutlineColor != null) ol.effectColor = SubtitleOutlineColor.Value;
                    float dx = SubtitleOutlineDistX != null ? SubtitleOutlineDistX.Value : 1.5f;
                    float dy = SubtitleOutlineDistY != null ? SubtitleOutlineDistY.Value : 1.5f;
                    ol.effectDistance = new Vector2(dx, dy);
                }
                else
                {
                    if (ol != null) UnityEngine.Object.Destroy(ol);
                }
            }
            catch { }

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

                if (SubtitleShadowEnabled != null && SubtitleShadowEnabled.Value)
                {
                    if (drop == null) drop = go.AddComponent<Shadow>();
                    if (SubtitleShadowUseGraphicAlpha != null) drop.useGraphicAlpha = SubtitleShadowUseGraphicAlpha.Value;
                    if (SubtitleShadowColor != null) drop.effectColor = SubtitleShadowColor.Value;
                    float dx = SubtitleShadowDistX != null ? SubtitleShadowDistX.Value : 2f;
                    float dy = SubtitleShadowDistY != null ? SubtitleShadowDistY.Value : -2f;
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

            // 一次性套入
            for (int i = 0; i < s_SnapshotReaders.Count; i++)
            {
                try { s_SnapshotReaders[i](S); } catch { }
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

            try
            {
                var file = Path.Combine(GetLocalesDir(), "RoleType.jsonc");
                if (!File.Exists(file)) return;

                var json = StripJsonComments(File.ReadAllText(file, Encoding.UTF8));
                var root = JObject.Parse(json);
                foreach (var p in root.Properties())
                {
                    var key = (p.Name ?? "").Trim();
                    var val = (p.Value?.ToString() ?? "").Trim();
                    if (string.IsNullOrEmpty(key)) continue;

                    s_UserRoleMapExact[key] = val;
                    s_UserRoleMapPrefix.Add(new KeyValuePair<string, string>(key.ToLowerInvariant(), val));
                }
                s_UserRoleMapPrefix.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
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
            return Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "locales", "ch");
        }

        private static string GetVoicesDir()
        {
            return Path.Combine(GetLocalesDir(), "voices");
        }

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
                        i++; // skip '/'
                    }
                    else sb.Append(c);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static SubtitleSystem.FontSpec BuildDanmakuFontSpec()
        {
            var spec = new SubtitleSystem.FontSpec();
            try
            {
                var csv = DanmakuFontFamilyCsv != null ? (DanmakuFontFamilyCsv.Value ?? "") : "";
                var list = new List<string>();
                var bundle = DanmakuFontBundleName != null ? (DanmakuFontBundleName.Value ?? "") : "";
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
                spec.size = DanmakuFontSize != null ? Math.Max(12, DanmakuFontSize.Value) : 24;
                spec.bold = DanmakuFontBold != null && DanmakuFontBold.Value;
                spec.italic = DanmakuFontItalic != null && DanmakuFontItalic.Value;
            }
            catch { }
            return spec;
        }

        public static void ApplyDanmakuTextOverrides(UnityEngine.UI.Text text)
        {
            if (text == null) return;
            try
            {
                var spec = BuildDanmakuFontSpec();
                var f = SubtitleSystem.SubtitleFontLoader.ResolveFont(spec);
                if (f != null) text.font = f;

                if (DanmakuFontSize != null && DanmakuFontSize.Value > 0)
                    text.fontSize = DanmakuFontSize.Value;

                var bold = DanmakuFontBold != null && DanmakuFontBold.Value;
                var italic = DanmakuFontItalic != null && DanmakuFontItalic.Value;
                text.fontStyle = (bold ? (italic ? FontStyle.BoldAndItalic : FontStyle.Bold)
                                       : (italic ? FontStyle.Italic : FontStyle.Normal));
            }
            catch { }

            try
            {
                var go = text.gameObject;
                var ol = go.GetComponent<UnityEngine.UI.Outline>();
                if (DanmakuOutlineEnabled != null && DanmakuOutlineEnabled.Value)
                {
                    if (ol == null) ol = go.AddComponent<UnityEngine.UI.Outline>();
                    ol.useGraphicAlpha = true;
                    if (DanmakuOutlineColor != null) ol.effectColor = DanmakuOutlineColor.Value;
                    float dx = DanmakuOutlineDistX != null ? DanmakuOutlineDistX.Value : 1.2f;
                    float dy = DanmakuOutlineDistY != null ? DanmakuOutlineDistY.Value : 1.2f;
                    ol.effectDistance = new Vector2(dx, dy);
                }
                else if (ol != null) UnityEngine.Object.Destroy(ol);
            }
            catch { }

            try
            {
                var go = text.gameObject;
                UnityEngine.UI.Shadow drop = null;
                var shadows = go.GetComponents<UnityEngine.UI.Shadow>();
                if (shadows != null)
                {
                    for (int i = 0; i < shadows.Length; i++)
                        if (!(shadows[i] is UnityEngine.UI.Outline)) { drop = shadows[i]; break; }
                }
                if (DanmakuShadowEnabled != null && DanmakuShadowEnabled.Value)
                {
                    if (drop == null) drop = go.AddComponent<UnityEngine.UI.Shadow>();
                    if (DanmakuShadowUseGraphicAlpha != null) drop.useGraphicAlpha = DanmakuShadowUseGraphicAlpha.Value;
                    if (DanmakuShadowColor != null) drop.effectColor = DanmakuShadowColor.Value;
                    float dx = DanmakuShadowDistX != null ? DanmakuShadowDistX.Value : 2f;
                    float dy = DanmakuShadowDistY != null ? DanmakuShadowDistY.Value : -2f;
                    drop.effectDistance = new Vector2(dx, dy);
                }
                else if (drop != null) UnityEngine.Object.Destroy(drop);
            }
            catch { }
        }

        public static SubtitleSystem.FontSpec BuildWorld3DFontSpec()
        {
            var spec = new SubtitleSystem.FontSpec();
            try
            {
                var csv = World3DFontFamilyCsv != null ? (World3DFontFamilyCsv.Value ?? "") : "";
                var list = new List<string>();
                var bundle = World3DFontBundleName != null ? (World3DFontBundleName.Value ?? "") : "";
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
                spec.size = World3DFontSize != null ? Math.Max(12, World3DFontSize.Value) : 26;
                spec.bold = World3DFontBold != null && World3DFontBold.Value;
                spec.italic = World3DFontItalic != null && World3DFontItalic.Value;
            }
            catch { }
            return spec;
        }

        public static void ApplyWorld3DTextOverrides(UnityEngine.UI.Text text)
        {
            if (text == null) return;
            try
            {
                var spec = BuildWorld3DFontSpec();
                var f = SubtitleSystem.SubtitleFontLoader.ResolveFont(spec);
                if (f != null) text.font = f;

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

            try
            {
                var go = text.gameObject;
                var ol = go.GetComponent<UnityEngine.UI.Outline>();
                if (World3DOutlineEnabled != null && World3DOutlineEnabled.Value)
                {
                    if (ol == null) ol = go.AddComponent<UnityEngine.UI.Outline>();
                    ol.useGraphicAlpha = true;
                    if (World3DOutlineColor != null) ol.effectColor = World3DOutlineColor.Value;
                    float dx = World3DOutlineDistX != null ? World3DOutlineDistX.Value : 1.5f;
                    float dy = World3DOutlineDistY != null ? World3DOutlineDistY.Value : 1.5f;
                    ol.effectDistance = new Vector2(dx, dy);
                }
                else if (ol != null) UnityEngine.Object.Destroy(ol);
            }
            catch { }

            try
            {
                var go = text.gameObject;
                UnityEngine.UI.Shadow drop = null;
                var shadows = go.GetComponents<UnityEngine.UI.Shadow>();
                if (shadows != null)
                {
                    for (int i = 0; i < shadows.Length; i++)
                        if (!(shadows[i] is UnityEngine.UI.Outline)) { drop = shadows[i]; break; }
                }
                if (World3DShadowEnabled != null && World3DShadowEnabled.Value)
                {
                    if (drop == null) drop = go.AddComponent<UnityEngine.UI.Shadow>();
                    if (World3DShadowUseGraphicAlpha != null) drop.useGraphicAlpha = World3DShadowUseGraphicAlpha.Value;
                    if (World3DShadowColor != null) drop.effectColor = World3DShadowColor.Value;
                    float dx = World3DShadowDistX != null ? World3DShadowDistX.Value : 2f;
                    float dy = World3DShadowDistY != null ? World3DShadowDistY.Value : -2f;
                    drop.effectDistance = new Vector2(dx, dy);
                }
                else if (drop != null) UnityEngine.Object.Destroy(drop);
            }
            catch { }
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
                var dir = GetVoicesDir();
                if (!Directory.Exists(dir)) return false;

                var files = Directory.GetFiles(dir, "*.jsonc", SearchOption.TopDirectoryOnly);
                var picks = new List<string>();
                for (int i = 0; i < files.Length; i++)
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

                var jsonc = File.ReadAllText(file, Encoding.UTF8);
                var json = StripJsonComments(jsonc);
                var root = JObject.Parse(json);

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

        private static string WrapColorTag(string s, Color c)
        {
            string hex = ColorUtility.ToHtmlStringRGB(c);
            return "<color=#" + hex + ">" + s + "</color>";
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

        // 下面是若干 Put* 小工具（把 ConfigEntry 写进 JObject）

        private static void PutBool(JObject o, string key, BepInEx.Configuration.ConfigEntry<bool> e)
        { if (o != null && e != null) o[key] = e.Value; }

        private static void PutInt(JObject o, string key, BepInEx.Configuration.ConfigEntry<int> e)
        { if (o != null && e != null) o[key] = e.Value; }

        private static void PutFloat(JObject o, string key, BepInEx.Configuration.ConfigEntry<float> e)
        { if (o != null && e != null) o[key] = (double)e.Value; }

        private static void PutStr(JObject o, string key, BepInEx.Configuration.ConfigEntry<string> e)
        { if (o != null && e != null) o[key] = e.Value ?? ""; }

        private static void PutCsv(JObject o, string key, BepInEx.Configuration.ConfigEntry<string> e)
        {
            if (o == null || e == null) return;
            var csv = e.Value ?? "";
            var arr = new JArray();
            if (!string.IsNullOrEmpty(csv))
            {
                var parts = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    var s = parts[i].Trim();
                    if (!string.IsNullOrEmpty(s)) arr.Add(s);
                }
            }
            o[key] = arr;
        }

        private static void PutColor(JObject o, string key, BepInEx.Configuration.ConfigEntry<Color> e)
        {
            if (o == null || e == null) return;
            var c = e.Value;
            // 保存为 [r,g,b,a] (0~1)，你的加载器支持数组与 #hex 两种，这里用数组更直观
            var a = new JArray((double)c.r, (double)c.g, (double)c.b, (double)c.a);
            o[key] = a;
        }

        private static void RegisterPresetBindings()
        {
            s_SnapshotWriters.Clear();
            s_SnapshotReaders.Clear();

            // —— General —— 
            RegBool("EnableSubtitle", EnableSubtitle);
            RegBool("SubtitleShowRoleTag", SubtitleShowRoleTag);
            RegBool("SubtitleShowPmcName", SubtitleShowPmcName);
            RegBool("SubtitleShowScavName", SubtitleShowScavName);
            RegEnum("SubtitlePlayerSelfPronoun", SubtitlePlayerSelfPronoun);
            RegEnum("SubtitleTeammateSelfPronoun", SubtitleTeammateSelfPronoun);
            RegFloat("SubtitleMaxDistanceMeters", SubtitleMaxDistanceMeters);
            RegBool("SubtitleShowDistance", SubtitleShowDistance);
            RegFloat("SubtitleDisplayDelaySec", SubtitleDisplayDelaySec);
            RegBool("EnableMapBroadcastSubtitle", EnableMapBroadcastSubtitle);
            RegBool("SubtitleZombieEnabled", SubtitleZombieEnabled);
            RegInt("SubtitleZombieCooldownSec", SubtitleZombieCooldownSec);

            // —— 字幕：字体/对齐/换行 —— 
            RegStr("SubtitleFontBundleName", SubtitleFontBundleName);
            RegCsv("SubtitleFontFamilyCsv", SubtitleFontFamilyCsv);
            RegInt("SubtitleFontSize", SubtitleFontSize);
            RegBool("SubtitleFontBold", SubtitleFontBold);
            RegBool("SubtitleFontItalic", SubtitleFontItalic);
            RegEnum("SubtitleAlignment", SubtitleAlignment);
            RegBool("SubtitleWrap", SubtitleWrap);
            RegInt("SubtitleWrapLength", SubtitleWrapLength);

            // —— 字幕：描边/阴影 —— 
            RegBool("SubtitleOutlineEnabled", SubtitleOutlineEnabled);
            RegColor("SubtitleOutlineColor", SubtitleOutlineColor);
            RegFloat("SubtitleOutlineDistX", SubtitleOutlineDistX);
            RegFloat("SubtitleOutlineDistY", SubtitleOutlineDistY);

            RegBool("SubtitleShadowEnabled", SubtitleShadowEnabled);
            RegColor("SubtitleShadowColor", SubtitleShadowColor);
            RegFloat("SubtitleShadowDistX", SubtitleShadowDistX);
            RegFloat("SubtitleShadowDistY", SubtitleShadowDistY);
            RegBool("SubtitleShadowUseGraphicAlpha", SubtitleShadowUseGraphicAlpha);

            // —— 字幕：布局/背景 —— 
            RegEnum("SubtitleLayoutAnchor", SubtitleLayoutAnchor);
            RegFloat("SubtitleLayoutOffsetX", SubtitleLayoutOffsetX);
            RegFloat("SubtitleLayoutOffsetY", SubtitleLayoutOffsetY);
            RegBool("SubtitleLayoutSafeArea", SubtitleLayoutSafeArea);
            RegFloat("SubtitleLayoutMaxWidthPercent", SubtitleLayoutMaxWidthPercent);
            RegFloat("SubtitleLayoutLineSpacing", SubtitleLayoutLineSpacing);
            RegEnum("SubtitleLayoutOverrideAlign", SubtitleLayoutOverrideAlign);
            RegFloat("SubtitleLayoutStackOffsetPercent", SubtitleLayoutStackOffsetPercent);

            RegBool("SubtitleBgEnabled", SubtitleBgEnabled);
            RegStr("SubtitleBgFit", SubtitleBgFit);
            RegColor("SubtitleBgColor", SubtitleBgColor);
            RegFloat("SubtitleBgPaddingX", SubtitleBgPaddingX);
            RegFloat("SubtitleBgPaddingY", SubtitleBgPaddingY);
            RegFloat("SubtitleBgMarginY", SubtitleBgMarginY);
            RegStr("SubtitleBgSprite", SubtitleBgSprite);

            RegBool("SubtitleBgShadowEnabled", SubtitleBgShadowEnabled);
            RegColor("SubtitleBgShadowColor", SubtitleBgShadowColor);
            RegFloat("SubtitleBgShadowDistX", SubtitleBgShadowDistX);
            RegFloat("SubtitleBgShadowDistY", SubtitleBgShadowDistY);
            RegBool("SubtitleBgShadowUseGraphicAlpha", SubtitleBgShadowUseGraphicAlpha);

            // —— 弹幕：通用 + 字体/描边/阴影 —— 
            RegBool("EnableDanmaku", EnableDanmaku);
            RegInt("DanmakuLanes", DanmakuLanes);
            RegFloat("DanmakuSpeed", DanmakuSpeed);
            RegInt("DanmakuMinGapPx", DanmakuMinGapPx);
            RegFloat("DanmakuSpawnDelaySec", DanmakuSpawnDelaySec);
            RegInt("DanmakuFontSize", DanmakuFontSize);
            RegFloat("DanmakuTopOffsetPercent", DanmakuTopOffsetPercent);
            RegFloat("DanmakuAreaMaxPercent", DanmakuAreaMaxPercent);
            RegBool("DanmakuShowRoleTag", DanmakuShowRoleTag);
            RegBool("DanmakuShowPmcName", DanmakuShowPmcName);
            RegBool("DanmakuShowScavName", DanmakuShowScavName);
            RegEnum("DanmakuPlayerSelfPronoun", DanmakuPlayerSelfPronoun);
            RegEnum("DanmakuTeammateSelfPronoun", DanmakuTeammateSelfPronoun);
            RegFloat("DanmakuMaxDistanceMeters", DanmakuMaxDistanceMeters);
            RegBool("DanmakuShowDistance", DanmakuShowDistance);
            RegBool("EnableMapBroadcastDanmaku", EnableMapBroadcastDanmaku);
            RegBool("DanmakuZombieEnabled", DanmakuZombieEnabled);
            RegInt("DanmakuZombieCooldownSec", DanmakuZombieCooldownSec);

            RegStr("DanmakuFontBundleName", DanmakuFontBundleName);
            RegCsv("DanmakuFontFamilyCsv", DanmakuFontFamilyCsv);
            RegBool("DanmakuFontBold", DanmakuFontBold);
            RegBool("DanmakuFontItalic", DanmakuFontItalic);
            RegBool("DanmakuOutlineEnabled", DanmakuOutlineEnabled);
            RegColor("DanmakuOutlineColor", DanmakuOutlineColor);
            RegFloat("DanmakuOutlineDistX", DanmakuOutlineDistX);
            RegFloat("DanmakuOutlineDistY", DanmakuOutlineDistY);
            RegBool("DanmakuShadowEnabled", DanmakuShadowEnabled);
            RegColor("DanmakuShadowColor", DanmakuShadowColor);
            RegFloat("DanmakuShadowDistX", DanmakuShadowDistX);
            RegFloat("DanmakuShadowDistY", DanmakuShadowDistY);
            RegBool("DanmakuShadowUseGraphicAlpha", DanmakuShadowUseGraphicAlpha);

            // —— World3D —— 
            RegBool("EnableWorld3D", EnableWorld3D);
            RegBool("World3DShowRoleTag", World3DShowRoleTag);
            RegBool("World3DShowPmcName", World3DShowPmcName);
            RegBool("World3DShowScavName", World3DShowScavName);
            RegEnum("World3DPlayerSelfPronoun", World3DPlayerSelfPronoun);
            RegEnum("World3DTeammateSelfPronoun", World3DTeammateSelfPronoun);
            RegFloat("World3DMaxDistanceMeters", World3DMaxDistanceMeters);
            RegBool("World3DShowDistance", World3DShowDistance);
            RegFloat("World3DDisplayDelaySec", World3DDisplayDelaySec);
            RegFloat("World3DVerticalOffsetY", World3DVerticalOffsetY);
            RegBool("World3DFacePlayer", World3DFacePlayer);
            RegBool("World3DBGEnabled", World3DBGEnabled);
            RegColor("World3DBGColor", World3DBGColor);
            RegBool("World3DShowSelf", World3DShowSelf);
            RegBool("World3DZombieEnabled", World3DZombieEnabled);
            RegInt("World3DZombieCooldownSec", World3DZombieCooldownSec);

            RegStr("World3DFontBundleName", World3DFontBundleName);
            RegCsv("World3DFontFamilyCsv", World3DFontFamilyCsv);
            RegInt("World3DFontSize", World3DFontSize);
            RegBool("World3DFontBold", World3DFontBold);
            RegBool("World3DFontItalic", World3DFontItalic);
            RegEnum("World3DAlignment", World3DAlignment);
            RegBool("World3DWrap", World3DWrap);
            RegInt("World3DWrapLength", World3DWrapLength);
            RegFloat("World3DWorldScale", World3DWorldScale);
            RegFloat("World3DDynamicPixelsPerUnit", World3DDynamicPixelsPerUnit);
            RegFloat("World3DFaceUpdateIntervalSec", World3DFaceUpdateIntervalSec);
            RegInt("World3DStackMaxLines", World3DStackMaxLines);
            RegFloat("World3DStackOffsetY", World3DStackOffsetY);
            RegFloat("World3DFadeInSec", World3DFadeInSec);
            RegFloat("World3DFadeOutSec", World3DFadeOutSec);
            RegBool("World3DOutlineEnabled", World3DOutlineEnabled);
            RegColor("World3DOutlineColor", World3DOutlineColor);
            RegFloat("World3DOutlineDistX", World3DOutlineDistX);
            RegFloat("World3DOutlineDistY", World3DOutlineDistY);
            RegBool("World3DShadowEnabled", World3DShadowEnabled);
            RegColor("World3DShadowColor", World3DShadowColor);
            RegFloat("World3DShadowDistX", World3DShadowDistX);
            RegFloat("World3DShadowDistY", World3DShadowDistY);
            RegBool("World3DShadowUseGraphicAlpha", World3DShadowUseGraphicAlpha);

            // —— 角色/广播颜色 —— 
            RegColor("SubRole_Player", SubRole_Player);
            RegColor("SubRole_Teammate", SubRole_Teammate);
            RegColor("SubRole_PmcBear", SubRole_PmcBear);
            RegColor("SubRole_PmcUsec", SubRole_PmcUsec);
            RegColor("SubRole_Scav", SubRole_Scav);
            RegColor("SubRole_Raider", SubRole_Raider);
            RegColor("SubRole_Rogue", SubRole_Rogue);
            RegColor("SubRole_Cultist", SubRole_Cultist);
            RegColor("SubRole_BossFollower", SubRole_BossFollower);
            RegColor("SubRole_Zombie", SubRole_Zombie);
            RegColor("SubRole_Goons", SubRole_Goons);
            RegColor("SubRole_Bosses", SubRole_Bosses);
            RegColor("SubRole_LabAnnouncer", SubRole_LabAnnouncer);

            RegColor("SubText_Player", SubText_Player);
            RegColor("SubText_Teammate", SubText_Teammate);
            RegColor("SubText_PmcBear", SubText_PmcBear);
            RegColor("SubText_PmcUsec", SubText_PmcUsec);
            RegColor("SubText_Scav", SubText_Scav);
            RegColor("SubText_Raider", SubText_Raider);
            RegColor("SubText_Rogue", SubText_Rogue);
            RegColor("SubText_Cultist", SubText_Cultist);
            RegColor("SubText_BossFollower", SubText_BossFollower);
            RegColor("SubText_Zombie", SubText_Zombie);
            RegColor("SubText_Goons", SubText_Goons);
            RegColor("SubText_Bosses", SubText_Bosses);
            RegColor("SubText_LabAnnouncer", SubText_LabAnnouncer);

            RegColor("DmRole_Player", DmRole_Player);
            RegColor("DmRole_Teammate", DmRole_Teammate);
            RegColor("DmRole_PmcBear", DmRole_PmcBear);
            RegColor("DmRole_PmcUsec", DmRole_PmcUsec);
            RegColor("DmRole_Scav", DmRole_Scav);
            RegColor("DmRole_Raider", DmRole_Raider);
            RegColor("DmRole_Rogue", DmRole_Rogue);
            RegColor("DmRole_Cultist", DmRole_Cultist);
            RegColor("DmRole_BossFollower", DmRole_BossFollower);
            RegColor("DmRole_Zombie", DmRole_Zombie);
            RegColor("DmRole_Goons", DmRole_Goons);
            RegColor("DmRole_Bosses", DmRole_Bosses);
            RegColor("DmRole_LabAnnouncer", DmRole_LabAnnouncer);

            RegColor("DmText_Player", DmText_Player);
            RegColor("DmText_Teammate", DmText_Teammate);
            RegColor("DmText_PmcBear", DmText_PmcBear);
            RegColor("DmText_PmcUsec", DmText_PmcUsec);
            RegColor("DmText_Scav", DmText_Scav);
            RegColor("DmText_Raider", DmText_Raider);
            RegColor("DmText_Rogue", DmText_Rogue);
            RegColor("DmText_Cultist", DmText_Cultist);
            RegColor("DmText_BossFollower", DmText_BossFollower);
            RegColor("DmText_Zombie", DmText_Zombie);
            RegColor("DmText_Goons", DmText_Goons);
            RegColor("DmText_Bosses", DmText_Bosses);
            RegColor("DmText_LabAnnouncer", DmText_LabAnnouncer);

            RegColor("W3dRole_Player", W3dRole_Player);
            RegColor("W3dRole_Teammate", W3dRole_Teammate);
            RegColor("W3dRole_PmcBear", W3dRole_PmcBear);
            RegColor("W3dRole_PmcUsec", W3dRole_PmcUsec);
            RegColor("W3dRole_Scav", W3dRole_Scav);
            RegColor("W3dRole_Raider", W3dRole_Raider);
            RegColor("W3dRole_Rogue", W3dRole_Rogue);
            RegColor("W3dRole_Cultist", W3dRole_Cultist);
            RegColor("W3dRole_BossFollower", W3dRole_BossFollower);
            RegColor("W3dRole_Zombie", W3dRole_Zombie);
            RegColor("W3dRole_Goons", W3dRole_Goons);
            RegColor("W3dRole_Bosses", W3dRole_Bosses);
            RegColor("W3dRole_LabAnnouncer", W3dRole_LabAnnouncer);

            RegColor("W3dText_Player", W3dText_Player);
            RegColor("W3dText_Teammate", W3dText_Teammate);
            RegColor("W3dText_PmcBear", W3dText_PmcBear);
            RegColor("W3dText_PmcUsec", W3dText_PmcUsec);
            RegColor("W3dText_Scav", W3dText_Scav);
            RegColor("W3dText_Raider", W3dText_Raider);
            RegColor("W3dText_Rogue", W3dText_Rogue);
            RegColor("W3dText_Cultist", W3dText_Cultist);
            RegColor("W3dText_BossFollower", W3dText_BossFollower);
            RegColor("W3dText_Zombie", W3dText_Zombie);
            RegColor("W3dText_Goons", W3dText_Goons);
            RegColor("W3dText_Bosses", W3dText_Bosses);
            RegColor("W3dText_LabAnnouncer", W3dText_LabAnnouncer);

        }


        private static bool In(string keyLC, string[] arr)
        {
            for (int i = 0; i < arr.Length; i++) if (keyLC == arr[i]) return true;
            return false;
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

        // 角色名前缀颜色
        public static Color GetRoleColor(RoleKind kind, Channel ch)
        {
            try
            {
                switch (ch)
                {
                    case Channel.Subtitle:
                        switch (kind)
                        {
                            case RoleKind.Player: return SubRole_Player.Value;
                            case RoleKind.Teammate: return SubRole_Teammate.Value;
                            case RoleKind.PmcBear: return SubRole_PmcBear.Value;
                            case RoleKind.PmcUsec: return SubRole_PmcUsec.Value;
                            case RoleKind.Scav: return SubRole_Scav.Value;
                            case RoleKind.Raider: return SubRole_Raider.Value;
                            case RoleKind.Rogue: return SubRole_Rogue.Value;
                            case RoleKind.Cultist: return SubRole_Cultist.Value;
                            case RoleKind.BossFollower: return SubRole_BossFollower.Value;
                            case RoleKind.Zombie: return SubRole_Zombie.Value;
                            case RoleKind.Goons: return SubRole_Goons.Value;
                            case RoleKind.Bosses: return SubRole_Bosses.Value;
                        }
                        break;
                    case Channel.Danmaku:
                        switch (kind)
                        {
                            case RoleKind.Player: return DmRole_Player.Value;
                            case RoleKind.Teammate: return DmRole_Teammate.Value;
                            case RoleKind.PmcBear: return DmRole_PmcBear.Value;
                            case RoleKind.PmcUsec: return DmRole_PmcUsec.Value;
                            case RoleKind.Scav: return DmRole_Scav.Value;
                            case RoleKind.Raider: return DmRole_Raider.Value;
                            case RoleKind.Rogue: return DmRole_Rogue.Value;
                            case RoleKind.Cultist: return DmRole_Cultist.Value;
                            case RoleKind.BossFollower: return DmRole_BossFollower.Value;
                            case RoleKind.Zombie: return DmRole_Zombie.Value;
                            case RoleKind.Goons: return DmRole_Goons.Value;
                            case RoleKind.Bosses: return DmRole_Bosses.Value;
                        }
                        break;
                    case Channel.World3D:
                        switch (kind)
                        {
                            case RoleKind.Player: return W3dRole_Player.Value;
                            case RoleKind.Teammate: return W3dRole_Teammate.Value;
                            case RoleKind.PmcBear: return W3dRole_PmcBear.Value;
                            case RoleKind.PmcUsec: return W3dRole_PmcUsec.Value;
                            case RoleKind.Scav: return W3dRole_Scav.Value;
                            case RoleKind.Raider: return W3dRole_Raider.Value;
                            case RoleKind.Rogue: return W3dRole_Rogue.Value;
                            case RoleKind.Cultist: return W3dRole_Cultist.Value;
                            case RoleKind.BossFollower: return W3dRole_BossFollower.Value;
                            case RoleKind.Zombie: return W3dRole_Zombie.Value;
                            case RoleKind.Goons: return W3dRole_Goons.Value;
                            case RoleKind.Bosses: return W3dRole_Bosses.Value;
                        }
                        break;
                }
            }
            catch { }

            // 回退旧配置，再回退白色
            return Color.white; // 没命中就回退纯白
        }

        // 正文颜色（整行）
        public static Color GetTextColor(RoleKind kind, Channel ch)
        {
            try
            {
                switch (ch)
                {
                    case Channel.Subtitle:
                        switch (kind)
                        {
                            case RoleKind.Player: return SubText_Player.Value;
                            case RoleKind.Teammate: return SubText_Teammate.Value;
                            case RoleKind.PmcBear: return SubText_PmcBear.Value;
                            case RoleKind.PmcUsec: return SubText_PmcUsec.Value;
                            case RoleKind.Scav: return SubText_Scav.Value;
                            case RoleKind.Raider: return SubText_Raider.Value;
                            case RoleKind.Rogue: return SubText_Rogue.Value;
                            case RoleKind.Cultist: return SubText_Cultist.Value;
                            case RoleKind.BossFollower: return SubText_BossFollower.Value;
                            case RoleKind.Zombie: return SubText_Zombie.Value;
                            case RoleKind.Goons: return SubText_Goons.Value;
                            case RoleKind.Bosses: return SubText_Bosses.Value;
                        }
                        break;
                    case Channel.Danmaku:
                        switch (kind)
                        {
                            case RoleKind.Player: return DmText_Player.Value;
                            case RoleKind.Teammate: return DmText_Teammate.Value;
                            case RoleKind.PmcBear: return DmText_PmcBear.Value;
                            case RoleKind.PmcUsec: return DmText_PmcUsec.Value;
                            case RoleKind.Scav: return DmText_Scav.Value;
                            case RoleKind.Raider: return DmText_Raider.Value;
                            case RoleKind.Rogue: return DmText_Rogue.Value;
                            case RoleKind.Cultist: return DmText_Cultist.Value;
                            case RoleKind.BossFollower: return DmText_BossFollower.Value;
                            case RoleKind.Zombie: return DmText_Zombie.Value;
                            case RoleKind.Goons: return DmText_Goons.Value;
                            case RoleKind.Bosses: return DmText_Bosses.Value;
                        }
                        break;
                    case Channel.World3D:
                        switch (kind)
                        {
                            case RoleKind.Player: return W3dText_Player.Value;
                            case RoleKind.Teammate: return W3dText_Teammate.Value;
                            case RoleKind.PmcBear: return W3dText_PmcBear.Value;
                            case RoleKind.PmcUsec: return W3dText_PmcUsec.Value;
                            case RoleKind.Scav: return W3dText_Scav.Value;
                            case RoleKind.Raider: return W3dText_Raider.Value;
                            case RoleKind.Rogue: return W3dText_Rogue.Value;
                            case RoleKind.Cultist: return W3dText_Cultist.Value;
                            case RoleKind.BossFollower: return W3dText_BossFollower.Value;
                            case RoleKind.Zombie: return W3dText_Zombie.Value;
                            case RoleKind.Goons: return W3dText_Goons.Value;
                            case RoleKind.Bosses: return W3dText_Bosses.Value;
                        }
                        break;
                }
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
