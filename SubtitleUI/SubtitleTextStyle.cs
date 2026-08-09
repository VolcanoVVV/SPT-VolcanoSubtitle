using Newtonsoft.Json; // 需确保项目里已有\
using Newtonsoft.Json.Linq;
using Subtitle.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SubtitleSystem
{
    // 预设整体
    public sealed class SubtitleTextPreset
    {
        public Dictionary<string, TextStyle> Styles;
        public JObject Setting;

        // ★ 运行期显示名（不参与 JSON 读写）
        [JsonIgnore]
        public string Name { get; private set; }

        // 当前激活的预设
        public static SubtitleTextPreset Current;

        // —— 加载入口 —— //
        public static SubtitleTextPreset LoadFromFile(string presetPath)
        {
            if (!File.Exists(presetPath))
            {
                Debug.LogWarning("[SubtitleStyle] Preset file not found: " + presetPath);
                return null;
            }

            string jsonc = File.ReadAllText(presetPath);
            string json = Subtitle.Utils.JsoncUtils.StripJsonComments(jsonc);

            // 只解析一次：先拿 JObject，再映射到预设对象
            JObject root = null;
            SubtitleTextPreset preset = null;
            try
            {
                root = JObject.Parse(json);
                preset = root.ToObject<SubtitleTextPreset>();
            }
            catch (Exception e)
            {
                Debug.LogError("[SubtitleStyle] Parse preset failed: " + e);
                return null;
            }

            if (preset == null)
            {
                Debug.LogWarning("[SubtitleStyle] Empty preset: " + presetPath);
                return null;
            }

            try
            {
                preset.Name = Path.GetFileNameWithoutExtension(presetPath);
            }
            catch { preset.Name = "preset"; }

            // 规范化字典 key
            if (preset.Styles != null)
            {
                var norm = new Dictionary<string, TextStyle>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in preset.Styles)
                {
                    if (kv.Value != null) kv.Value.Normalize();
                    norm[kv.Key] = kv.Value;
                }
                // ★ 兼容：如有 default 且无 global，则复制一份为 global
                if (norm.ContainsKey("default") && !norm.ContainsKey("global"))
                {
                    norm["global"] = norm["default"];
                }
                preset.Styles = norm;
            }

            // ★ 解析 Setting（支持两种位置：根级 或 Styles 下的 Setting）
            try
            {
                JToken settingTok = null;

                if (root.TryGetValue("Setting", StringComparison.OrdinalIgnoreCase, out settingTok) ||
                    root.TryGetValue("Settings", StringComparison.OrdinalIgnoreCase, out settingTok))
                {
                    if (settingTok is JObject) preset.Setting = (JObject)settingTok;
                }
                else
                {
                    // 也兼容写在 Styles.Setting 里的用法
                    JToken stylesTok;
                    if (root.TryGetValue("Styles", StringComparison.OrdinalIgnoreCase, out stylesTok))
                    {
                        JToken inner;
                        var stylesObj = stylesTok as JObject;
                        if (stylesObj != null && stylesObj.TryGetValue("Setting", StringComparison.OrdinalIgnoreCase, out inner) && inner is JObject)
                        {
                            preset.Setting = (JObject)inner;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SubtitleStyle] Parse Setting section failed: " + e.Message);
            }

            return preset;
        }

        // 从文件夹扫描一个（或全部）
        public static Dictionary<string, string> ScanPresets(string dir)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(dir)) return map;
            var files = Directory.GetFiles(dir, "*.jsonc", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                var name = Path.GetFileNameWithoutExtension(files[i]);
                map[name] = files[i];
            }
            return map;
        }


    }



    // 单个样式定义
    public sealed class TextStyle
    {
        public FontSpec font;
        public OutlineSpec outline;
        public ShadowSpec shadow;
        public LayoutSpec layout;
        public BackgroundSpec background;



        // 规范化/缺省
        public void Normalize()
        {
            if (font == null) font = new FontSpec();
            if (outline == null) outline = new OutlineSpec();
            if (shadow == null) shadow = new ShadowSpec();
            if (layout == null) layout = new LayoutSpec();
            if (background == null) background = new BackgroundSpec();
        }

        public void ApplyTo(Text text)
        {
            if (text == null) return;

            // 1) 字体载入：file -> family[] -> Arial
            Font loaded = SubtitleFontLoader.ResolveFont(font);
            if (loaded != null && text.font != loaded)
                text.font = loaded;

            // 2) 基础样式
            if (font.size > 0) text.fontSize = font.size;
            text.fontStyle = (font.bold ? (font.italic ? FontStyle.BoldAndItalic : FontStyle.Bold)
                                        : (font.italic ? FontStyle.Italic : FontStyle.Normal));


            // 3) 描边
            var go = text.gameObject;
            Outline ol = go.GetComponent<Outline>();
            if (outline.enabled)
            {
                if (ol == null) ol = go.AddComponent<Outline>();
                ol.useGraphicAlpha = true;

                Color oc;
                if (ColorUtil.TryParseColor(outline.color, out oc))
                    ol.effectColor = oc;

                if (outline.distance != null && outline.distance.Length >= 2)
                    ol.effectDistance = new Vector2((float)outline.distance[0], (float)outline.distance[1]);
            }
            else
            {
                if (ol != null) UnityEngine.Object.Destroy(ol);
            }

            // 4) 投影（注意避开 Outline）
            Shadow drop = null;
            var shadows = go.GetComponents<Shadow>();
            if (shadows != null)
            {
                for (int i = 0; i < shadows.Length; i++)
                {
                    if (!(shadows[i] is Outline)) { drop = shadows[i]; break; }
                }
            }

            if (shadow.enabled)
            {
                if (drop == null) drop = go.AddComponent<Shadow>();
                drop.useGraphicAlpha = shadow.useGraphicAlpha;

                Color sc;
                if (ColorUtil.TryParseColor(shadow.color, out sc))
                    drop.effectColor = sc;

                if (shadow.distance != null && shadow.distance.Length >= 2)
                    drop.effectDistance = new Vector2((float)shadow.distance[0], (float)shadow.distance[1]);
            }
            else
            {
                if (drop != null) UnityEngine.Object.Destroy(drop);
            }

            // 5) 下划线/删除线：占位，不实现
            // TODO: 如果以后需要，可在 text 下生成一条 Image 作为线条并随文字尺寸调整
        }

        // ========== 新增：布局配置 ==========
public sealed class LayoutSpec
{
    // 把“这一行字幕”挂在哪个屏幕锚点：Upper/Middle/Lower × Left/Center/Right
    public string anchor = "LowerCenter";
    // 相对该锚点的像素偏移 [x, y]
    public double[] offset = new double[] { 0.0, 110.0 };
    // 考虑安全区域（底部手势条/刘海等）
    public bool safeArea = true;
    // 测量文字时的最大宽度占屏比（0~1），用于自动换行和“文本盒宽度”
    public double maxWidthPercent = 0.90;
    // 行距叠加（给多行字幕使用）
    public double lineSpacing = 0.0;
    // 可选：强制 Text 对齐（如 MiddleCenter；null/空 表示不改）
    public string overrideTextAlignment;
    // ★ 新增：底部堆叠面板离屏幕底部的相对高度（0~0.5）
    public double stackOffsetPercent = 0.12;
}
        // ========== 新增：背景配置 ==========
public sealed class BackgroundSpec
{
    public bool enabled = false;            // 开关
    // 贴合策略：text=紧贴文字（加 padding）；fullRow=定宽条（按 maxWidthPercent）
    public string fit = "text";
    public string color = "rgba(0,0,0,0.35)";
    public double[] padding = new double[] { 12.0, 6.0 };
    public double[] margin = new double[] { 0.0, 6.0 };
    public string sprite;                   // 可选：游戏内置九宫格资源名
    // 背景投影（可复用已有 ShadowSpec）
    public ShadowSpec shadow = new ShadowSpec { enabled = false };
}
}

    // ———— 子结构 ————
    public sealed class FontSpec
    { // 相对 presets/fonts/（仅用于猜测family，不做直读）
        public List<string> family;        // 系统字体候选
        public int size = 24;
        public bool bold;
        public bool italic;
    }

    public sealed class OutlineSpec
    {
        public bool enabled = true;
        public string color = "#000000F2";
        public double[] distance = new double[] { 1.5, 1.5 };
    }

    public sealed class ShadowSpec
    {
        public bool enabled = true;
        public string color = "#00000099";
        public double[] distance = new double[] { 2.0, -2.0 };
        public bool useGraphicAlpha = true;
    }

    // ———— 工具：字体加载 ————
    internal static class SubtitleFontLoader
    {
        private static string _fontsDir; // 你原来保留的字段（目前仅用于 file 名推断family）
        private static string _fontBundleDir; // FontReplace 的 Font 目录
        private static Dictionary<string, Font> _gameFontCache;
        private static Dictionary<string, Font> _bundleFontCache;
        private static Dictionary<string, TMP_FontAsset> _gameTmpFontCache;
        private static Dictionary<string, TMP_FontAsset> _bundleTmpFontCache;
        private static Dictionary<string, Font> _osFontCache;
        private static HashSet<string> _osFontMissCache;
        private static HashSet<string> _installedOSFontNames;
        private static HashSet<string> _bundleFontMissCache;
        private static HashSet<string> _bundleTmpFontMissCache;
        private const int DynamicFontBaseSize = 32;

        public static void SetFontsDir(string dir)
        {
            _fontsDir = dir;
        }

        public static void SetFontBundleDir(string dir)
        {
            _fontBundleDir = dir;
        }

        // 清空资源包字体缓存：局内替换/新增同名字体文件后，由设置界面的「刷新」按钮调用，
        // 强制下次解析重新从磁盘加载（定向失效，不影响游戏字体缓存）
        public static void InvalidateBundleFontCache()
        {
            if (_bundleFontCache != null) _bundleFontCache.Clear();
            if (_bundleTmpFontCache != null) _bundleTmpFontCache.Clear();
            if (_bundleFontMissCache != null) _bundleFontMissCache.Clear();
            if (_bundleTmpFontMissCache != null) _bundleTmpFontMissCache.Clear();
        }

        // 仅返回真实的 SDF 字体资产；无法解析时由调用方回退到旧 Text 渲染。
        public static TMP_FontAsset ResolveTMPFont(FontSpec spec)
        {
            if (spec == null) return null;
            List<string> bundleNames = CollectBundleFontCandidates(spec);
            List<string> gameNames = CollectGameFontCandidates(spec);

            TMP_FontAsset font = TryLoadBundleTMPFont(bundleNames);
            if (font == null) font = TryLoadGameTMPFont(gameNames);
            return font;
        }

        public static Font ResolveFont(FontSpec spec)
        {
            if (spec == null) return TryBuiltinArial();

            // 先准备候选
            List<string> bundleNames = CollectBundleFontCandidates(spec);
            List<string> gameNames = CollectGameFontCandidates(spec);
            List<string> osFamilies = CollectOSFontCandidates(spec);

            // 加载优先顺序：
            // 1) 若 source == "game"：先游戏字体 -> 再系统字体
            // 2) 若 source == "os"：先系统字体 -> 再游戏字体
            // 3) 其他/为空：先游戏字体（若给了）、再系统字体

            Font f = null;
            // 先试 FontReplace 资源包，再试游戏字体，再试系统 family
            f = TryLoadBundleFont(bundleNames, Math.Max(12, spec.size));
            if (f == null) f = TryLoadGameFont(gameNames, Math.Max(12, spec.size));
            if (f == null) f = TryLoadOSFont(osFamilies, Math.Max(12, spec.size));
            return f != null ? f : TryBuiltinArial();
        }

        // —— 收集候选：FontReplace 资源包名 —— //
        private static List<string> CollectBundleFontCandidates(FontSpec spec)
        {
            var list = new List<string>();
            if (spec.family == null) return list;
            for (int i = 0; i < spec.family.Count; i++)
            {
                var fam = spec.family[i];
                if (!string.IsNullOrEmpty(fam) && fam.StartsWith("bundle:", StringComparison.OrdinalIgnoreCase))
                {
                    var name = fam.Substring("bundle:".Length).Trim();
                    if (!string.IsNullOrEmpty(name)) list.Add(name);
                }
            }
            return list;
        }

        // —— 收集候选：游戏字体名 —— //
        private static List<string> CollectGameFontCandidates(FontSpec spec)
        {
            var list = new List<string>();


            // family 中形如 "game:FontName" 的也当作游戏字体名
            if (spec.family != null)
            {
                for (int i = 0; i < spec.family.Count; i++)
                {
                    var fam = spec.family[i];
                    if (!string.IsNullOrEmpty(fam) && fam.StartsWith("game:", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(fam.Substring("game:".Length));
                    }
                }
            }
            return list;
        }

        // —— 收集候选：系统字体 family —— //
        private static List<string> CollectOSFontCandidates(FontSpec spec)
        {
            var list = new List<string>();

            // 1) family 中非 "game:" 的当作系统family
            if (spec.family != null)
            {
                for (int i = 0; i < spec.family.Count; i++)
                {
                    var fam = spec.family[i];
                    if (!string.IsNullOrEmpty(fam) &&
                        !fam.StartsWith("game:", StringComparison.OrdinalIgnoreCase) &&
                        !fam.StartsWith("bundle:", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(fam.Trim());
                    }
                }
            }
            return list;
        }

        // —— 尝试加载：游戏字体 —— //
        private static Font TryLoadGameFont(List<string> names, int size)
        {
            if (names == null || names.Count == 0) return null;

            EnsureGameFontCache();
            for (int i = 0; i < names.Count; i++)
            {
                var key = names[i];
                if (string.IsNullOrEmpty(key)) continue;

                // _gameFontCache 使用 OrdinalIgnoreCase 比较器，直接查找即可
                Font f;
                if (_gameFontCache.TryGetValue(key, out f)) return f;
            }
            return null;
        }

        // —— 尝试加载：FontReplace 资源包字体 —— //
        private static Font TryLoadBundleFont(List<string> names, int size)
        {
            if (names == null || names.Count == 0) return null;
            if (string.IsNullOrEmpty(_fontBundleDir) || !Directory.Exists(_fontBundleDir)) return null;

            if (_bundleFontCache == null)
                _bundleFontCache = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
            if (_bundleFontMissCache == null)
                _bundleFontMissCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < names.Count; i++)
            {
                var key = names[i];
                if (string.IsNullOrEmpty(key)) continue;

                Font cached;
                if (_bundleFontCache.TryGetValue(key, out cached)) return cached;
                if (_bundleFontMissCache.Contains(key)) continue;

                string path = Path.Combine(_fontBundleDir, key);
                if (!File.Exists(path))
                {
                    // 兜底：若没扩展名，尝试匹配任意扩展
                    var files = Directory.GetFiles(_fontBundleDir, key + ".*", SearchOption.TopDirectoryOnly);
                    if (files.Length > 0)
                        path = files[0];
                }

                if (!File.Exists(path))
                {
                    _bundleFontMissCache.Add(key);
                    continue;
                }

                AssetBundle ab = null;
                try
                {
                    ab = AssetBundle.LoadFromFile(path);
                    if (ab == null)
                    {
                        _bundleFontMissCache.Add(key);
                        continue;
                    }

                    Font font = null;
                    var fonts = ab.LoadAllAssets<Font>();
                    if (fonts != null && fonts.Length > 0)
                    {
                        font = fonts[0];
                    }
                    else
                    {
                        var fontAssetName = Path.GetFileNameWithoutExtension(path);
                        TMP_FontAsset pick = null;
                        if (!string.IsNullOrEmpty(fontAssetName))
                            pick = ab.LoadAsset<TMP_FontAsset>(fontAssetName);
                        if (pick == null)
                        {
                            var tmpFonts = ab.LoadAllAssets<TMP_FontAsset>();
                            if (tmpFonts != null && tmpFonts.Length > 0)
                                pick = tmpFonts[0];
                        }
                        if (pick != null)
                            font = pick.sourceFontFile;
                        else
                            font = null;
                    }

                    if (font != null)
                    {
                        _bundleFontCache[key] = font;
                        return font;
                    }
                    _bundleFontMissCache.Add(key);
                }
                catch (Exception e)
                {
                    _bundleFontMissCache.Add(key);
                    Debug.LogWarning("[SubtitleStyle] Load FontReplace bundle failed: " + e.Message);
                }
                finally
                {
                    if (ab != null)
                    {
                        try { ab.Unload(false); }
                        catch { }
                    }
                }
            }

            return null;
        }

        private static TMP_FontAsset TryLoadBundleTMPFont(List<string> names)
        {
            if (names == null || names.Count == 0) return null;
            if (string.IsNullOrEmpty(_fontBundleDir) || !Directory.Exists(_fontBundleDir)) return null;

            if (_bundleTmpFontCache == null)
                _bundleTmpFontCache = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);
            if (_bundleTmpFontMissCache == null)
                _bundleTmpFontMissCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < names.Count; i++)
            {
                string key = names[i];
                if (string.IsNullOrEmpty(key)) continue;

                TMP_FontAsset cached;
                if (_bundleTmpFontCache.TryGetValue(key, out cached)) return cached;
                if (_bundleTmpFontMissCache.Contains(key)) continue;

                string path = Path.Combine(_fontBundleDir, key);
                if (!File.Exists(path))
                {
                    string[] files = Directory.GetFiles(_fontBundleDir, key + ".*", SearchOption.TopDirectoryOnly);
                    if (files.Length > 0) path = files[0];
                }
                if (!File.Exists(path))
                {
                    _bundleTmpFontMissCache.Add(key);
                    continue;
                }

                AssetBundle bundle = null;
                try
                {
                    bundle = AssetBundle.LoadFromFile(path);
                    if (bundle == null)
                    {
                        _bundleTmpFontMissCache.Add(key);
                        continue;
                    }

                    string assetName = Path.GetFileNameWithoutExtension(path);
                    TMP_FontAsset font = !string.IsNullOrEmpty(assetName)
                        ? bundle.LoadAsset<TMP_FontAsset>(assetName)
                        : null;
                    if (font == null)
                    {
                        TMP_FontAsset[] fonts = bundle.LoadAllAssets<TMP_FontAsset>();
                        if (fonts != null && fonts.Length > 0) font = fonts[0];
                    }
                    if (font != null)
                    {
                        _bundleTmpFontCache[key] = font;
                        return font;
                    }
                    _bundleTmpFontMissCache.Add(key);
                }
                catch (Exception e)
                {
                    _bundleTmpFontMissCache.Add(key);
                    Debug.LogWarning("[SubtitleStyle] Load TMP font bundle failed: " + e.Message);
                }
                finally
                {
                    if (bundle != null)
                    {
                        try { bundle.Unload(false); }
                        catch { }
                    }
                }
            }
            return null;
        }

        private static TMP_FontAsset TryLoadGameTMPFont(List<string> names)
        {
            if (names == null || names.Count == 0) return null;
            EnsureGameTMPFontCache();
            for (int i = 0; i < names.Count; i++)
            {
                string key = names[i];
                if (string.IsNullOrEmpty(key)) continue;
                TMP_FontAsset font;
                if (_gameTmpFontCache.TryGetValue(key, out font)) return font;
            }
            return null;
        }

        private static void EnsureGameTMPFontCache()
        {
            if (_gameTmpFontCache != null) return;
            _gameTmpFontCache = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);

            TMP_FontAsset[] all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (all == null) return;
            for (int i = 0; i < all.Length; i++)
            {
                TMP_FontAsset font = all[i];
                if (font == null || string.IsNullOrEmpty(font.name)) continue;
                if (!_gameTmpFontCache.ContainsKey(font.name))
                    _gameTmpFontCache.Add(font.name, font);
            }
        }

        // 构建一次游戏字体缓存（Resources 里已加载的 Font 资源）
        private static void EnsureGameFontCache()
        {
            if (_gameFontCache != null) return;
            _gameFontCache = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

            Font[] all = Resources.FindObjectsOfTypeAll<Font>();
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    var f = all[i];
                    if (f == null) continue;
                    if (!_gameFontCache.ContainsKey(f.name))
                        _gameFontCache.Add(f.name, f);
                }
            }
        }

        // 供设置 GUI 的字体选择器使用：确保游戏字体缓存已构建，返回全部可用游戏字体名（排序后的缓存键）。
        // 这些名字在 *FontFamilyCsv 里以 "game:名字" 的形式书写。
        internal static List<string> GetGameFontNames()
        {
            EnsureGameFontCache();
            var names = new List<string>(_gameFontCache.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // —— 尝试加载：系统字体 —— //
        private static Font TryLoadOSFont(List<string> families, int size)
        {
            if (families == null || families.Count == 0) return null;
            string requestKey = BuildOSFontCacheKey(families);
            if (string.IsNullOrEmpty(requestKey)) return null;

            if (_osFontCache == null)
                _osFontCache = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
            if (_osFontMissCache == null)
                _osFontMissCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_osFontMissCache.Contains(requestKey)) return null;

            string selectedFamily = FindInstalledOSFontFamily(families);
            if (string.IsNullOrEmpty(selectedFamily))
            {
                _osFontMissCache.Add(requestKey);
                return null;
            }

            Font cached;
            if (_osFontCache.TryGetValue(selectedFamily, out cached))
            {
                if (cached != null) return cached;
                _osFontCache.Remove(selectedFamily);
            }

            try
            {
                // 动态 Font 可按 Text.fontSize 请求不同字号，固定基础字号可让三个渠道共享同一原生字体资源。
                Font font = Font.CreateDynamicFontFromOSFont(selectedFamily, DynamicFontBaseSize);
                if (font != null)
                {
                    _osFontCache[selectedFamily] = font;
                    Debug.Log("[SubtitleStyle] Cached dynamic OS font: " + selectedFamily);
                    return font;
                }
                _osFontMissCache.Add(requestKey);
            }
            catch (Exception e)
            {
                _osFontMissCache.Add(requestKey);
                Debug.LogWarning("[SubtitleStyle] Load OS font failed: " + e.Message);
            }
            return null;
        }

        private static string FindInstalledOSFontFamily(List<string> families)
        {
            if (_installedOSFontNames == null)
            {
                _installedOSFontNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string[] installed = Font.GetOSInstalledFontNames();
                    if (installed != null)
                    {
                        for (int i = 0; i < installed.Length; i++)
                        {
                            string family = installed[i];
                            if (!string.IsNullOrEmpty(family)) _installedOSFontNames.Add(family.Trim());
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SubtitleStyle] Read installed OS fonts failed: " + e.Message);
                }
            }

            for (int i = 0; i < families.Count; i++)
            {
                string family = families[i];
                if (string.IsNullOrEmpty(family)) continue;
                family = family.Trim();
                if (_installedOSFontNames.Contains(family)) return family;
            }
            return null;
        }

        private static string BuildOSFontCacheKey(List<string> families)
        {
            if (families == null || families.Count == 0) return string.Empty;
            var normalized = new List<string>(families.Count);
            for (int i = 0; i < families.Count; i++)
            {
                string family = families[i];
                if (string.IsNullOrEmpty(family)) continue;
                family = family.Trim();
                if (family.Length > 0) normalized.Add(family.ToLowerInvariant());
            }
            return string.Join("\u001f", normalized.ToArray());
        }

        private static Font TryBuiltinArial()
        {
            // 走 UiWidgets 的共享缓存，避免重复 GetBuiltinResource
            try { return UiWidgets.DefaultFont; }
            catch { return null; }
        }
    }


    // ———— 工具：颜色解析 ————
    internal static class ColorUtil
    {
        // 预编译正则，避免每次解析颜色都重建
        private static readonly Regex RgbaRegex = new Regex(
            @"rgba?\s*\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*(\d*\.?\d+))?\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool TryParseColor(string s, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(s)) return false;

            // #RRGGBB or #RRGGBBAA
            if (s[0] == '#')
            {
                string hex = s.Substring(1);
                if (hex.Length == 6 || hex.Length == 8)
                {
                    byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                    byte a = (byte)255;
                    if (hex.Length == 8)
                        a = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
                    c = new Color32(r, g, b, a);
                    return true;
                }
                return false;
            }

            // rgba(r,g,b,a)
            var m = RgbaRegex.Match(s);
            if (m.Success)
            {
                int r = Clamp0_255(m.Groups[1].Value);
                int g = Clamp0_255(m.Groups[2].Value);
                int b = Clamp0_255(m.Groups[3].Value);
                float a = 1f;
                if (m.Groups[4].Success)
                {
                    float.TryParse(m.Groups[4].Value, out a);
                    if (a < 0f) a = 0f; if (a > 1f) a = 1f;
                }
                c = new Color32((byte)r, (byte)g, (byte)b, (byte)(a * 255f));
                return true;
            }

            return false;
        }

        private static int Clamp0_255(string s)
        {
            int v;
            if (!int.TryParse(s, out v)) v = 0;
            if (v < 0) v = 0; if (v > 255) v = 255;
            return v;
        }
    }

    // ———— 工具：枚举解析 ————
    internal static class EnumUtil
    {
        public static bool TryParseTextAnchor(string s, out TextAnchor a)
        {
            a = TextAnchor.UpperLeft;
            if (string.IsNullOrEmpty(s)) return false;
            try
            {
                a = (TextAnchor)Enum.Parse(typeof(TextAnchor), s, true);
                return true;
            }
            catch { return false; }
        }
    }
}
