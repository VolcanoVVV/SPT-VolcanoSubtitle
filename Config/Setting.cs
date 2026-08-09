using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using Subtitle.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Subtitle.Config
{
    internal partial class Settings
    {
        public enum TextAnchorOption
        {
            None = -1,
            UpperLeft = 0,
            UpperCenter = 1,
            UpperRight = 2,
            MiddleLeft = 3,
            MiddleCenter = 4,
            MiddleRight = 5,
            LowerLeft = 6,
            LowerCenter = 7,
            LowerRight = 8
        }

        public static ConfigFile Config;
        public static List<ConfigEntryBase> ConfigEntries = new List<ConfigEntryBase>();
        // 纯动作条目（按钮/折叠开关）：值无意义，分类/全局重置时跳过（Init 末尾统一登记）
        private static readonly HashSet<ConfigEntryBase> s_NonResettable = new HashSet<ConfigEntryBase>();
        private static readonly ManualLogSource s_Log = BepInEx.Logging.Logger.CreateLogSource("Subtitle.Settings");
        private static List<string> s_PresetNames = new List<string>();
        private static int s_SelectedPresetIndex = 0; // 仅用于 UI 的“待应用选择”
        private static string s_PresetsDir;
        private static bool s_PresetListLoaded = false;
        private static Dictionary<string, string> s_UserRoleMapExact;
        private static List<KeyValuePair<string, string>> s_UserRoleMapPrefix; // 前缀匹配（小写）
        private static List<string> s_AllAiTypeKeysCache;
        private static bool s_RoleTypeLoaded;

        public enum SelfPronounOption
        {
            略称,
            玩家名,
            声线名
        }

        // —— Sections —— //
        private const string GeneralSection = "1. 通用";

        private const string SubtitleGeneralSection = "2 字幕 - 通用";
        private const string SubtitleAdvancedSection = "2.1 字幕 - 进阶";

        private const string SubRoleColorSection = "2.2 字幕 - 角色颜色";
        private const string SubRoleTextColorSection = "2.3 字幕 - 角色文本颜色";

        private const string DanmakuGeneralSection = "3 弹幕 - 通用";
        private const string DanmakuAdvancedSection = "3.1 弹幕 - 进阶";

        private const string DmRoleColorSection = "3.2 弹幕 - 角色颜色";
        private const string DmRoleTextColorSection = "3.3 弹幕 - 角色文本颜色";

        private const string World3DGeneralSection = "4 3D气泡 - 通用";
        private const string World3DAdvancedSection = "4.1 3D气泡 - 进阶";

        private const string W3dRoleColorSection = "4.2 3D气泡 - 角色颜色";
        private const string W3dRoleTextColorSection = "4.3 3D气泡 - 角色文本颜色";

        private const string DebugSection = "99. 测试";

        // —— General —— //
        public static ConfigEntry<string> TextPresetName;
        public static ConfigEntry<string> PhraseFilterPanelButton;
        public static ConfigEntry<string> SettingsWindowButton;
        public static ConfigEntry<bool> ShowSubtitleOptions;
        public static ConfigEntry<bool> ShowDanmakuOptions;
        public static ConfigEntry<bool> ShowWorld3DOptions;
        public static ConfigEntry<bool> StreamerModeEnabled;
        public static ConfigEntry<StreamerMaskStyle> StreamerMaskStyle;
        public static ConfigEntry<KeyboardShortcut> SettingsWindowHotkey;
        public static ConfigEntry<string> UiLanguage;


        // —— Subtitle General —— //
        public static ConfigEntry<bool> EnableSubtitle;
        public static ConfigEntry<bool> SubtitleShowRoleTag;
        public static ConfigEntry<bool> SubtitleShowPmcName;
        public static ConfigEntry<bool> SubtitleShowScavName;
        public static ConfigEntry<SelfPronounOption> SubtitlePlayerSelfPronoun;
        public static ConfigEntry<SelfPronounOption> SubtitleTeammateSelfPronoun;
        public static ConfigEntry<float> SubtitleMaxDistanceMeters;
        public static ConfigEntry<bool> SubtitleShowDistance;
        public static ConfigEntry<float> SubtitleDisplayDelaySec;
        public static ConfigEntry<bool> EnableMapBroadcastSubtitle;
        public static ConfigEntry<bool> SubtitleZombieEnabled;
        public static ConfigEntry<int> SubtitleZombieCooldownSec;
        // —— Subtitle - Advanced ——//
        // 字体
        public static ConfigEntry<string> SubtitleFontBundleName;
        public static ConfigEntry<string> SubtitleFontFamilyCsv;   // 逗号分隔：SimHei, Microsoft YaHei, game:MainUIFontCN
        public static ConfigEntry<int> SubtitleFontSize;
        public static ConfigEntry<bool> SubtitleFontBold;
        public static ConfigEntry<bool> SubtitleFontItalic;

        // 文本对齐 & 换行
        public static ConfigEntry<TextAnchorOption> SubtitleAlignment;
        public static ConfigEntry<bool> SubtitleWrap;            // Wrap / Overflow
        public static ConfigEntry<int> SubtitleWrapLength;      // >0 强制断行（按可见字符数）
        public static ConfigEntry<int> SubtitleMaxLineChars;    // 单行可见字符数上限（0 不限制；宽度≈字符数×字号）
        public static ConfigEntry<bool> SubtitleMarqueeEnabled; // 不换行且超限时：固定宽窗口内横向滚动显示全文

        // 描边
        public static ConfigEntry<bool> SubtitleOutlineEnabled;
        public static ConfigEntry<Color> SubtitleOutlineColor;
        public static ConfigEntry<float> SubtitleOutlineDistX;
        public static ConfigEntry<float> SubtitleOutlineDistY;

        // 阴影
        public static ConfigEntry<bool> SubtitleShadowEnabled;
        public static ConfigEntry<Color> SubtitleShadowColor;
        public static ConfigEntry<float> SubtitleShadowDistX;
        public static ConfigEntry<float> SubtitleShadowDistY;
        public static ConfigEntry<bool> SubtitleShadowUseGraphicAlpha;

        // 布局（LayoutSpec）
        public static ConfigEntry<TextAnchorOption> SubtitleLayoutAnchor;
        public static ConfigEntry<float> SubtitleLayoutOffsetX;
        public static ConfigEntry<float> SubtitleLayoutOffsetY;
        public static ConfigEntry<bool> SubtitleLayoutSafeArea;
        public static ConfigEntry<float> SubtitleLayoutMaxWidthPercent;
        public static ConfigEntry<float> SubtitleLayoutLineSpacing;
        public static ConfigEntry<TextAnchorOption> SubtitleLayoutOverrideAlign;
        public static ConfigEntry<float> SubtitleLayoutStackOffsetPercent;

        // 背景（BackgroundSpec）
        public static ConfigEntry<bool> SubtitleBgEnabled;
        public static ConfigEntry<string> SubtitleBgFit;             // text | fullRow
        public static ConfigEntry<Color> SubtitleBgColor;
        public static ConfigEntry<float> SubtitleBgPaddingX;
        public static ConfigEntry<float> SubtitleBgPaddingY;
        public static ConfigEntry<float> SubtitleBgMarginY;
        public static ConfigEntry<string> SubtitleBgSprite;

        // 背景阴影
        public static ConfigEntry<bool> SubtitleBgShadowEnabled;
        public static ConfigEntry<Color> SubtitleBgShadowColor;
        public static ConfigEntry<float> SubtitleBgShadowDistX;
        public static ConfigEntry<float> SubtitleBgShadowDistY;
        public static ConfigEntry<bool> SubtitleBgShadowUseGraphicAlpha;

        // —— Danmaku —— //
        public static ConfigEntry<bool> EnableDanmaku;
        public static ConfigEntry<int> DanmakuLanes;
        public static ConfigEntry<float> DanmakuSpeed;
        public static ConfigEntry<int> DanmakuMinGapPx;
        public static ConfigEntry<float> DanmakuSpawnDelaySec;
        public static ConfigEntry<int> DanmakuFontSize; // 0 表示不覆盖
        public static ConfigEntry<float> DanmakuTopOffsetPercent;
        public static ConfigEntry<float> DanmakuAreaMaxPercent;
        public static ConfigEntry<bool> DanmakuShowRoleTag;
        public static ConfigEntry<bool> DanmakuShowPmcName;
        public static ConfigEntry<bool> DanmakuShowScavName;
        public static ConfigEntry<SelfPronounOption> DanmakuPlayerSelfPronoun;
        public static ConfigEntry<SelfPronounOption> DanmakuTeammateSelfPronoun;
        public static ConfigEntry<float> DanmakuMaxDistanceMeters;
        public static ConfigEntry<bool> DanmakuShowDistance;
        public static ConfigEntry<bool> EnableMapBroadcastDanmaku;

        public static ConfigEntry<bool> DanmakuZombieEnabled;
        public static ConfigEntry<int> DanmakuZombieCooldownSec;
        // —— Danmaku-Advanced —— //
        public static ConfigEntry<string> DanmakuFontBundleName;
        public static ConfigEntry<string> DanmakuFontFamilyCsv;
        public static ConfigEntry<bool> DanmakuFontBold;
        public static ConfigEntry<bool> DanmakuFontItalic;

        public static ConfigEntry<bool> DanmakuOutlineEnabled;
        public static ConfigEntry<Color> DanmakuOutlineColor;
        public static ConfigEntry<float> DanmakuOutlineDistX;
        public static ConfigEntry<float> DanmakuOutlineDistY;

        public static ConfigEntry<bool> DanmakuShadowEnabled;
        public static ConfigEntry<Color> DanmakuShadowColor;
        public static ConfigEntry<float> DanmakuShadowDistX;
        public static ConfigEntry<float> DanmakuShadowDistY;
        public static ConfigEntry<bool> DanmakuShadowUseGraphicAlpha;

        // —— World3D —— //
        public static ConfigEntry<bool> EnableWorld3D;
        public static ConfigEntry<bool> World3DShowRoleTag;
        public static ConfigEntry<bool> World3DShowPmcName;
        public static ConfigEntry<bool> World3DShowScavName;
        public static ConfigEntry<SelfPronounOption> World3DPlayerSelfPronoun;
        public static ConfigEntry<SelfPronounOption> World3DTeammateSelfPronoun;
        public static ConfigEntry<float> World3DMaxDistanceMeters;
        public static ConfigEntry<bool> World3DShowDistance;
        public static ConfigEntry<float> World3DDisplayDelaySec;
        public static ConfigEntry<float> World3DVerticalOffsetY;
        public static ConfigEntry<bool> World3DFacePlayer;
        public static ConfigEntry<bool> World3DBGEnabled;
        public static ConfigEntry<Color> World3DBGColor;
        public static ConfigEntry<bool> World3DShowSelf;
        public static ConfigEntry<bool> World3DZombieEnabled;
        public static ConfigEntry<int> World3DZombieCooldownSec;
        // —— World3D-Advanced —— //
        public static ConfigEntry<string> World3DFontBundleName;
        public static ConfigEntry<string> World3DFontFamilyCsv;
        public static ConfigEntry<int> World3DFontSize;
        public static ConfigEntry<bool> World3DFontBold;
        public static ConfigEntry<bool> World3DFontItalic;
        public static ConfigEntry<TextAnchorOption> World3DAlignment;
        public static ConfigEntry<bool> World3DWrap;
        public static ConfigEntry<int> World3DWrapLength;
        public static ConfigEntry<float> World3DWorldScale;
        public static ConfigEntry<float> World3DDynamicPixelsPerUnit;
        public static ConfigEntry<float> World3DFaceUpdateIntervalSec;
        public static ConfigEntry<int> World3DStackMaxLines;
        public static ConfigEntry<float> World3DStackOffsetY;
        public static ConfigEntry<float> World3DFadeInSec;
        public static ConfigEntry<float> World3DFadeOutSec;
        public static ConfigEntry<bool> World3DOutlineEnabled;
        public static ConfigEntry<Color> World3DOutlineColor;
        public static ConfigEntry<float> World3DOutlineDistX;
        public static ConfigEntry<float> World3DOutlineDistY;
        public static ConfigEntry<bool> World3DShadowEnabled;
        public static ConfigEntry<Color> World3DShadowColor;
        public static ConfigEntry<float> World3DShadowDistX;
        public static ConfigEntry<float> World3DShadowDistY;
        public static ConfigEntry<bool> World3DShadowUseGraphicAlpha;

        // ===== 颜色 · 角色名颜色（字幕） =====
        public static ConfigEntry<Color> SubRole_Player;
        public static ConfigEntry<Color> SubRole_Teammate;
        public static ConfigEntry<Color> SubRole_PmcBear;
        public static ConfigEntry<Color> SubRole_PmcUsec;
        public static ConfigEntry<Color> SubRole_Scav;
        public static ConfigEntry<Color> SubRole_Raider;
        public static ConfigEntry<Color> SubRole_Rogue;
        public static ConfigEntry<Color> SubRole_Cultist;
        public static ConfigEntry<Color> SubRole_BossFollower;
        public static ConfigEntry<Color> SubRole_Zombie;
        public static ConfigEntry<Color> SubRole_Goons;
        public static ConfigEntry<Color> SubRole_Bosses;
        public static ConfigEntry<Color> SubRole_LabAnnouncer;

        // ===== 颜色 · 正文颜色（字幕） =====
        public static ConfigEntry<Color> SubText_Player;
        public static ConfigEntry<Color> SubText_Teammate;
        public static ConfigEntry<Color> SubText_PmcBear;
        public static ConfigEntry<Color> SubText_PmcUsec;
        public static ConfigEntry<Color> SubText_Scav;
        public static ConfigEntry<Color> SubText_Raider;
        public static ConfigEntry<Color> SubText_Rogue;
        public static ConfigEntry<Color> SubText_Cultist;
        public static ConfigEntry<Color> SubText_BossFollower;
        public static ConfigEntry<Color> SubText_Zombie;
        public static ConfigEntry<Color> SubText_Goons;
        public static ConfigEntry<Color> SubText_Bosses;
        public static ConfigEntry<Color> SubText_LabAnnouncer;

        // ===== 颜色 · 角色名颜色（弹幕） =====
        public static ConfigEntry<Color> DmRole_Player;
        public static ConfigEntry<Color> DmRole_Teammate;
        public static ConfigEntry<Color> DmRole_PmcBear;
        public static ConfigEntry<Color> DmRole_PmcUsec;
        public static ConfigEntry<Color> DmRole_Scav;
        public static ConfigEntry<Color> DmRole_Raider;
        public static ConfigEntry<Color> DmRole_Rogue;
        public static ConfigEntry<Color> DmRole_Cultist;
        public static ConfigEntry<Color> DmRole_BossFollower;
        public static ConfigEntry<Color> DmRole_Zombie;
        public static ConfigEntry<Color> DmRole_Goons;
        public static ConfigEntry<Color> DmRole_Bosses;
        public static ConfigEntry<Color> DmRole_LabAnnouncer;

        // ===== 颜色 · 正文颜色（弹幕） =====
        public static ConfigEntry<Color> DmText_Player;
        public static ConfigEntry<Color> DmText_Teammate;
        public static ConfigEntry<Color> DmText_PmcBear;
        public static ConfigEntry<Color> DmText_PmcUsec;
        public static ConfigEntry<Color> DmText_Scav;
        public static ConfigEntry<Color> DmText_Raider;
        public static ConfigEntry<Color> DmText_Rogue;
        public static ConfigEntry<Color> DmText_Cultist;
        public static ConfigEntry<Color> DmText_BossFollower;
        public static ConfigEntry<Color> DmText_Zombie;
        public static ConfigEntry<Color> DmText_Goons;
        public static ConfigEntry<Color> DmText_Bosses;
        public static ConfigEntry<Color> DmText_LabAnnouncer;

        // ===== 颜色 · 角色名颜色（World3D） =====
        public static ConfigEntry<Color> W3dRole_Player;
        public static ConfigEntry<Color> W3dRole_Teammate;
        public static ConfigEntry<Color> W3dRole_PmcBear;
        public static ConfigEntry<Color> W3dRole_PmcUsec;
        public static ConfigEntry<Color> W3dRole_Scav;
        public static ConfigEntry<Color> W3dRole_Raider;
        public static ConfigEntry<Color> W3dRole_Rogue;
        public static ConfigEntry<Color> W3dRole_Cultist;
        public static ConfigEntry<Color> W3dRole_BossFollower;
        public static ConfigEntry<Color> W3dRole_Zombie;
        public static ConfigEntry<Color> W3dRole_Goons;
        public static ConfigEntry<Color> W3dRole_Bosses;
        public static ConfigEntry<Color> W3dRole_LabAnnouncer;

        // ===== 颜色 · 正文颜色（World3D） =====
        public static ConfigEntry<Color> W3dText_Player;
        public static ConfigEntry<Color> W3dText_Teammate;
        public static ConfigEntry<Color> W3dText_PmcBear;
        public static ConfigEntry<Color> W3dText_PmcUsec;
        public static ConfigEntry<Color> W3dText_Scav;
        public static ConfigEntry<Color> W3dText_Raider;
        public static ConfigEntry<Color> W3dText_Rogue;
        public static ConfigEntry<Color> W3dText_Cultist;
        public static ConfigEntry<Color> W3dText_BossFollower;
        public static ConfigEntry<Color> W3dText_Zombie;
        public static ConfigEntry<Color> W3dText_Goons;
        public static ConfigEntry<Color> W3dText_Bosses;
        public static ConfigEntry<Color> W3dText_LabAnnouncer;

        // —— Debug —— //
        private static ConfigEntry<string> _TestSubtitleButton;
        public static ConfigEntry<string> TestDanmakuButton;
        public static ConfigEntry<bool> EnableDebugTools;
        public static ConfigEntry<KeyboardShortcut> DebugPanelHotkey;
        public static ConfigEntry<bool> DanmakuDebugVerbose;
        public static BepInEx.Configuration.ConfigEntry<bool> MapBroadcastDebug;
        public static ConfigEntry<float> VoiceDedupWindowSec;
        // 设置窗口整体不透明度（纯 GUI 偏好，不进预设快照）：调低后可透过窗口看到测试字幕/弹幕
        public static ConfigEntry<float> SettingsWindowOpacity;

        public static void Init(ConfigFile config)
        {
            Config = config;

            var entries = new List<ConfigEntryBase>();

            // 防御：统一注册表在下方 Bind 时顺带填充，先清空以防重复初始化时累积
            s_SnapshotWriters.Clear();
            s_SnapshotReaders.Clear();
            s_SubtitleFoldTargets.Clear();
            s_DanmakuFoldTargets.Clear();
            s_World3DFoldTargets.Clear();

            // —— 运行期刷新动作：作为 Reg 的 refresh 参数传入（原 HookChanged 区块的五个分组） —— //
            Action refreshSubtitleLayout = TryApplySubtitleLayoutRuntime;
            Action refreshSubtitleStyle = TryRefreshSubtitleStyleRuntime;
            Action refreshDanmaku = TryApplyDanmakuRuntime;
            Action refreshDanmakuStyle = TryRefreshDanmakuStyleRuntime;
            Action refreshWorld3DStyle = TryRefreshWorld3DStyleRuntime;

            // 值变更挂接：批量应用预设期间（s_BatchApplying）只记录受影响子系统，批量结束后每个子系统统一刷新一次
            void HookChanged<T>(ConfigEntry<T> entry, Action onChanged)
            {
                if (entry == null || onChanged == null) return;
                entry.SettingChanged += (s, e) =>
                {
                    try
                    {
                        // 批量应用预设期间：只记录受影响的子系统，批量结束后统一各刷新一次
                        if (s_BatchApplying)
                        {
                            if (!s_BatchPendingRefreshes.Contains(onChanged))
                                s_BatchPendingRefreshes.Add(onChanged);
                            return;
                        }
                        onChanged();
                    }
                    catch { }
                };
            }

            // 统一注册：Bind 之后顺带登记 折叠组 / 预设快照 / 变更刷新，替代原先三套平行注册表
            // presetKey 为 null → 不进预设快照（按钮/折叠开关/调试项）；foldGroup 为 null → 不参与折叠；
            // refresh 为 null → 变更时不触发运行期刷新；csv = true → 字符串按 CSV 数组快照（字体候选列表）
            ConfigEntry<T> Reg<T>(string section, string key, T def, ConfigDescription desc,
                string presetKey = null, List<ConfigEntryBase> foldGroup = null, Action refresh = null, bool csv = false)
            {
                var e = Config.Bind(section, key, def, desc);
                entries.Add(e);
                if (foldGroup != null) AddFoldTarget(foldGroup, e);
                if (presetKey != null) RegSnapshot(presetKey, e, csv);
                if (refresh != null) HookChanged(e, refresh);
                return e;
            }

            // —— 1) General —— //
            // 让 EnableSubtitle 排在 TextPresetName 上方：先添加它（后续 RecalcOrder 会按添加顺序设置 Order）

            // 仅 Bind：预设选择器自身不进 折叠/预设快照/刷新 注册表
            entries.Add(TextPresetName = Config.Bind(
                GeneralSection,
                "文本样式预设",
                "default",
                new ConfigDescription(
                    "从 presets 文件夹读取所有 .jsonc预设文件。点击“应用”后，会将预设中所有包含选项一次性导入本配置。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "文本样式预设",
                        CustomDrawer = DrawPresetPicker,   // ★ 使用自绘控件
                        HideDefaultButton = true
                    })));

            // 若玩家手动改了 cfg 里的值，这里仅校验是否存在，不再自动应用
            TextPresetName.SettingChanged += (s, e) =>
            {
                if (string.IsNullOrEmpty(s_PresetsDir))
                    s_PresetsDir = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "presets");

                var name = TextPresetName.Value ?? "default";
                var path = Path.Combine(s_PresetsDir, name + ".jsonc");
                if (!File.Exists(path))
                {
                    s_Log.LogWarning($"[Settings] Preset '{name}' not found, keep selection but apply requires valid file.");
                }
            };

            // 扫描 presets 目录，构建可选项（与 DrawPresetPicker 共用 ScanPresets，避免两套扫描逻辑）
            ScanPresets(true);

            // 界面语言：纯环境项，不进预设快照（预设只管视觉样式）；切换后立即重载文本并整体重建设置窗口。
            // 注意：不再挂 AcceptableValueList —— GUI 用专用语言选择行（实时扫描 locales 目录）渲染，
            // 且 SettingControlFactory.ShouldSkip 会跳过本条，F12 下它只是普通字符串项。
            UiLanguage = Reg(
                GeneralSection,
                "界面 语言",
                I18n.DefaultLanguage,
                new ConfigDescription(
                    "图形化设置界面的显示语言（locales 下的语言目录名，如 ch）。\n切换后立即重载界面文本并重建设置窗口。",
                    null,
                    new ConfigurationManagerAttributes { }),
                null, null, delegate
                {
                    I18n.Reload(UiLanguage.Value);
                    SettingsUI.SettingsWindow.RebuildAll();
                });

            EnableSubtitle = Reg(
               GeneralSection,
               "字幕启用",
               true,
               new ConfigDescription(
                   "是否启用字幕功能。",
                   null,
                   new ConfigurationManagerAttributes
                   {
                   }),
               "EnableSubtitle");

            EnableDanmaku = Reg(
                GeneralSection,
                "弹幕启动",
                true,
                new ConfigDescription(
                    "启用弹幕显示。",
                    null,
                    new ConfigurationManagerAttributes
                    {

                    }),
                "EnableDanmaku");

            EnableWorld3D = Reg(
                GeneralSection,
                "3D气泡启用",
                true,
                new ConfigDescription(
                    "启用 3D 气泡显示。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "EnableWorld3D");

            // 主播模式：环境开关，不进预设快照（预设只管视觉样式）
            StreamerModeEnabled = Reg(
                GeneralSection,
                "主播模式启用",
                false,
                new ConfigDescription(
                    "启用后，字幕/弹幕/3D气泡与地图广播中的脏话会被打码。\n词表文件：locales/ch/StreamerWords.jsonc，可自由增删，重开战局后生效。",
                    null,
                    new ConfigurationManagerAttributes { }));

            StreamerMaskStyle = Reg(
                GeneralSection,
                "主播模式 打码样式",
                Utils.StreamerMaskStyle.Asterisks,
                new ConfigDescription(
                    "主播模式的打码样式：\n- Asterisks：等长星号（他妈的 → ***）\n- Blocks：等长方块（他妈的 → ■■■）\n- Grawlix：整词替换为 @#$^#",
                    null,
                    new ConfigurationManagerAttributes { Indent = 1 }));

            // 仅 Bind：图形化设置界面入口按钮，不进 折叠/预设快照/刷新 注册表
            entries.Add(SettingsWindowButton = Config.Bind(
                GeneralSection,
                "图形化设置界面",
                "",
                new ConfigDescription(
                    "打开图形化设置界面。所有设置项均在界面内调整，F12 仅保留本按钮与下方热键两个入口。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawSettingsWindowButton,
                        HideDefaultButton = true
                    })));

            // 图形化设置界面热键：环境开关，不进预设快照
            SettingsWindowHotkey = Reg(
                GeneralSection,
                "设置界面 打开热键",
                new KeyboardShortcut(KeyCode.F9),
                new ConfigDescription(
                    "打开/关闭 图形化设置界面 的热键（默认 F9）。也可点界面右上角“关闭”退出。",
                    null,
                    new ConfigurationManagerAttributes { }));

            // 仅 Bind：面板按钮/折叠开关/测试按钮/调试项 不进 折叠/预设快照/刷新 注册表
            entries.Add(PhraseFilterPanelButton = Config.Bind(
                GeneralSection,
                "台词显示控制面板",
                "",
                new ConfigDescription(
                    "打开台词显示控制面板，用于选择声线/触发器/NetId 的显示规则。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawPhraseFilterPanelButton,
                        HideDefaultButton = true
                    })));

            // 设置测试按钮（任意场景弹一条测试字幕）
            entries.Add(_TestSubtitleButton = Config.Bind(
                GeneralSection,
                "▶ 随机测试字幕",
                "", // 值无意义占位
                new ConfigDescription(
                    "点击右侧按钮随机发送一条测试字幕。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawTestSubtitleButton,
                        HideDefaultButton = true
                    })));

            entries.Add(ShowSubtitleOptions = Config.Bind(
                SubtitleGeneralSection,
                "展开/收缩 字幕 选项设置",
                true,
                new ConfigDescription(
                    "是否展开字幕设置项。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawFoldToggleButton,
                        HideDefaultButton = true,
                        HideSettingName = true
                    })));

            entries.Add(ShowDanmakuOptions = Config.Bind(
                DanmakuGeneralSection,
                "展开/收缩 弹幕 选项设置",
                true,
                new ConfigDescription(
                    "是否展开弹幕设置项。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawFoldToggleButton,
                        HideDefaultButton = true,
                        HideSettingName = true
                    })));

            entries.Add(ShowWorld3DOptions = Config.Bind(
                World3DGeneralSection,
                "展开/收缩 3D气泡 选项设置",
                true,
                new ConfigDescription(
                    "是否展开 3D 气泡设置项。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawFoldToggleButton,
                        HideDefaultButton = true,
                        HideSettingName = true
                    })));

            SubtitleShowRoleTag = Reg(
               SubtitleGeneralSection,
               "字幕 显示说话者",
               true,
               new ConfigDescription(
                    "是否在字幕中显示说话者（roletag）。关闭后仅显示台词文本（及距离）\n开启：显示“你/Scav/Tagilla：”。关闭：只显示台词（可选加距离）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    }),
               "SubtitleShowRoleTag", s_SubtitleFoldTargets);

            SubtitleShowPmcName = Reg(SubtitleGeneralSection, "字幕 显示PMC名字", false, new ConfigDescription("是否显示PMC游戏内的ID", null, new ConfigurationManagerAttributes { }), "SubtitleShowPmcName", s_SubtitleFoldTargets);
            SubtitleShowScavName = Reg(SubtitleGeneralSection, "字幕 显示Scav名字", false, new ConfigDescription("是否显示Scav游戏内的ID\n不推荐，因为Scav游戏名字太长可能会导致台词观感很差。", null, new ConfigurationManagerAttributes { }), "SubtitleShowScavName", s_SubtitleFoldTargets);

            SubtitlePlayerSelfPronoun = Reg(
                SubtitleGeneralSection,
                "字幕 玩家说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当玩家自己说话时字幕显示风格：\n- 略称：始终显示“你”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "SubtitlePlayerSelfPronoun", s_SubtitleFoldTargets);

            SubtitleTeammateSelfPronoun = Reg(
                SubtitleGeneralSection,
                "字幕 队友说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当友军（队友）说话时字幕显示风格：\n- 略称：始终显示“队友”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "SubtitleTeammateSelfPronoun", s_SubtitleFoldTargets);

            SubtitleMaxDistanceMeters = Reg(
               SubtitleGeneralSection,
               "字幕 最大语音接收距离（米）",
               30f,
               new ConfigDescription(
                   "当语音（其他玩家/AI）来源距离玩家超过该距离时，不显示字幕。\n10~150 米，默认 30 米",
                   new AcceptableValueRange<float>(10f, 150f),
                   new ConfigurationManagerAttributes
                   {
                   }),
               "SubtitleMaxDistanceMeters", s_SubtitleFoldTargets);

            SubtitleShowDistance = Reg(
                SubtitleGeneralSection,
                "字幕 显示距离",
                true,
                new ConfigDescription(
                    "是否显示距离。开启后，会在语音后面添加一个类似“ ·10m”的字样",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    }),
                "SubtitleShowDistance", s_SubtitleFoldTargets);

            SubtitleDisplayDelaySec = Reg(
                SubtitleGeneralSection,
                "字幕 台词显示延迟（秒）",
                0.5f,
                new ConfigDescription(
                    "字幕显示后，额外延迟消失的秒数，避免短语音瞬间出现又消失。",
                    new AcceptableValueRange<float>(0f, 3f),
                    new ConfigurationManagerAttributes { }),
                "SubtitleDisplayDelaySec", s_SubtitleFoldTargets);

            EnableMapBroadcastSubtitle = Reg(
                SubtitleGeneralSection,
                "字幕 启动实验室广播",
                true,
                new ConfigDescription(
                    "启用后会把 实验室公共播报 显示为字幕（即实验室拉闸/开关的全图播报）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    }),
                "EnableMapBroadcastSubtitle", s_SubtitleFoldTargets);

            SubtitleZombieEnabled = Reg(
                SubtitleGeneralSection,
                "字幕 丧尸显示台词（除 丧尸Tagilla）",
                true,
                new ConfigDescription(
                    "丧尸台词是否显示（不影响 丧尸Tagilla）。开启则正常显示丧尸台词，关闭则不显示所有普通丧尸的台词",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    }),
                "SubtitleZombieEnabled", s_SubtitleFoldTargets);

            SubtitleZombieCooldownSec = Reg(
                SubtitleGeneralSection,
                "字幕 丧尸台词间隔（秒）",
                10,
                new ConfigDescription(
                    "丧尸类台词的最小间隔（秒）。第一条丧尸台词出现后，在此设置时间区间内其它丧尸台词会被忽略。\n0 表示不限制。推荐5-10以上，否则会导致大量的台词刷屏。",
                    new AcceptableValueRange<int>(0, 60),
                    new ConfigurationManagerAttributes
                    {
                        Indent = 1
                    }),
                "SubtitleZombieCooldownSec", s_SubtitleFoldTargets);
            // —— 2.1) Subtitle-Advanced —— //
            SubtitleFontBundleName = Reg(
                SubtitleAdvancedSection, "字幕 字体资源包", "",
                new ConfigDescription(
                    "从 BepInEx\\plugins\\FontReplace\\Font 选择字体资源包（不覆盖则留空）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawFontBundlePicker,
                        HideDefaultButton = true
                    }),
                "SubtitleFontBundleName", s_SubtitleFoldTargets, refreshSubtitleStyle);
            SubtitleFontFamilyCsv = Reg(
                SubtitleAdvancedSection, "字幕 字体类型",
                "SimHei;Microsoft YaHei;Microsoft YaHei UI;DengXian;Noto Sans CJK SC",
                new ConfigDescription("字体候选，分号;隔开(需要大写分号)\n支持 game:FontName 走游戏内置字体。\n游戏将优先从左往右依次检测支持的字体类型，最后退回Arial.ttf",
                    null, new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    }),
                "SubtitleFontFamilyCsv", s_SubtitleFoldTargets, refreshSubtitleStyle, csv: true);

            SubtitleFontSize = Reg(
                SubtitleAdvancedSection, "字幕 字体尺寸（px）", 26,
                new ConfigDescription("字体尺寸大小（px）", new AcceptableValueRange<int>(12, 64),
                    new ConfigurationManagerAttributes { }),
                "SubtitleFontSize", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleFontBold = Reg(SubtitleAdvancedSection, "字幕 字体加粗", false,
                new ConfigDescription("字幕字体加粗。", null, new ConfigurationManagerAttributes { }),
                "SubtitleFontBold", s_SubtitleFoldTargets, refreshSubtitleStyle);
            SubtitleFontItalic = Reg(SubtitleAdvancedSection, "字幕 字体斜体", false,
                new ConfigDescription("字幕字体斜体。", null, new ConfigurationManagerAttributes { }),
                "SubtitleFontItalic", s_SubtitleFoldTargets, refreshSubtitleStyle);

            // 对齐 & 换行
            SubtitleAlignment = Reg(
                SubtitleAdvancedSection, "字幕 文本对齐", TextAnchorOption.MiddleCenter,
                new ConfigDescription("文本对齐（TextAnchor）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "SubtitleAlignment", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleWrap = Reg(
                SubtitleAdvancedSection, "字幕 自动换行", true,
                new ConfigDescription("是否开启自动换行，若开启则按照下方换行限制进行，禁用则不换行", null,
                    new ConfigurationManagerAttributes { }),
                "SubtitleWrap", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleWrapLength = Reg(
                SubtitleAdvancedSection, "字幕 自动换行长度阈值", 0,
                new ConfigDescription("超过 N 个可见字符后强制换行（0 关闭）。", null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "SubtitleWrapLength", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleMaxLineChars = Reg(
                SubtitleAdvancedSection, "字幕 台词长度上限", 0,
                new ConfigDescription("单行最多显示的可见字符数（0 不限制）。宽度按 字符数×字号 估算（中文每字约 1 个字号宽）。\n开启自动换行时：作为换行宽度上限，超出部分在框内换行；\n关闭自动换行时：超出后在固定宽窗口内横向滚动或截断（见下项）。", new AcceptableValueRange<int>(0, 200),
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "SubtitleMaxLineChars", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleMarqueeEnabled = Reg(
                SubtitleAdvancedSection, "字幕 超长滚动显示", true,
                new ConfigDescription("仅在 关闭自动换行 且 台词超过长度上限 时生效：\n开启则在固定宽窗口内横向滚动显示完整台词；关闭则截断并追加“…”。", null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "SubtitleMarqueeEnabled", s_SubtitleFoldTargets, refreshSubtitleStyle);

            // 描边
            SubtitleOutlineEnabled = Reg(
                SubtitleAdvancedSection, "字幕 字体描边", true,
                new ConfigDescription("启用描边。", null,
                    new ConfigurationManagerAttributes { }),
                "SubtitleOutlineEnabled", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleOutlineColor = Reg(
                SubtitleAdvancedSection, "字幕 字体描边颜色", new Color(0f, 0f, 0f, 0.95f),
                new ConfigDescription("描边颜色。", null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "SubtitleOutlineColor", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleOutlineDistX = Reg(
                SubtitleAdvancedSection, "字幕 字体描边位移（X轴）", 1.5f,
                new ConfigDescription("描边水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleOutlineDistX", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleOutlineDistY = Reg(
                SubtitleAdvancedSection, "字幕 字体描边位移（Y轴）", 1.5f,
                new ConfigDescription("描边垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleOutlineDistY", s_SubtitleFoldTargets, refreshSubtitleStyle);

            // 阴影
            SubtitleShadowEnabled = Reg(
                SubtitleAdvancedSection, "字幕 字体阴影", true,
                new ConfigDescription("启用阴影。", null,
                    new ConfigurationManagerAttributes { }),
                "SubtitleShadowEnabled", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleShadowColor = Reg(
                SubtitleAdvancedSection, "字幕 字体阴影颜色", new Color(0f, 0f, 0f, 0.6f),
                new ConfigDescription("阴影颜色。", null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "SubtitleShadowColor", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleShadowDistX = Reg(
                SubtitleAdvancedSection, "字幕 字体阴影位移（X轴）", 2f,
                new ConfigDescription("阴影水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleShadowDistX", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleShadowDistY = Reg(
                SubtitleAdvancedSection, "字幕 字体阴影位移（Y轴）", -2f,
                new ConfigDescription("阴影垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleShadowDistY", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleShadowUseGraphicAlpha = Reg(
                SubtitleAdvancedSection, "字幕 字体阴影叠乘文本透明度", true,
                new ConfigDescription("是否叠乘文本透明度。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleShadowUseGraphicAlpha", s_SubtitleFoldTargets, refreshSubtitleStyle);

            // 背景
            SubtitleBgEnabled = Reg(
                SubtitleAdvancedSection, "字幕 文本背景", true,
                new ConfigDescription("开启条形气泡背景。", null,
                    new ConfigurationManagerAttributes { }),
                "SubtitleBgEnabled", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleBgFit = Reg(
                SubtitleAdvancedSection, "字幕 文本背景贴合", "text",
                new ConfigDescription("贴合策略：text（贴文字）/ fullRow（固定宽度）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleBgFit", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleBgColor = Reg(
                SubtitleAdvancedSection, "字幕 文本背景颜色", new Color(0f, 0f, 0f, 0.35f),
                new ConfigDescription("背景色。", null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "SubtitleBgColor", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleBgPaddingX = Reg(
                SubtitleAdvancedSection, "字幕 文本背景内边距 X", 12f,
                new ConfigDescription("背景内边距 X（像素）", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleBgPaddingX", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleBgPaddingY = Reg(
                SubtitleAdvancedSection, "字幕 文本背景内边距 Y", 6f,
                new ConfigDescription("背景内边距 Y（像素）", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleBgPaddingY", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleBgMarginY = Reg(
                SubtitleAdvancedSection, "字幕 文本背景外边距 Y", 6f,
                new ConfigDescription("背景外边距 Y（像素）", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleBgMarginY", s_SubtitleFoldTargets, refreshSubtitleLayout);

            SubtitleBgSprite = Reg(
                SubtitleAdvancedSection, "字幕 文本背景九宫格名", "",
                new ConfigDescription("九宫格资源名（可选）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleBgSprite", s_SubtitleFoldTargets, refreshSubtitleStyle);

            // 背景阴影
            SubtitleBgShadowEnabled = Reg(
                SubtitleAdvancedSection, "字幕 背景阴影", false,
                new ConfigDescription("背景投影开关。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleBgShadowEnabled", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleBgShadowColor = Reg(
                SubtitleAdvancedSection, "字幕 背景阴影颜色", new Color(0f, 0f, 0f, 0.45f),
                new ConfigDescription("背景投影颜色。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleBgShadowColor", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleBgShadowDistX = Reg(
                SubtitleAdvancedSection, "字幕 背景阴影水平偏移 X", 2f,
                new ConfigDescription("背景阴影：水平偏移（px）", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleBgShadowDistX", s_SubtitleFoldTargets, refreshSubtitleStyle);

            // 注意：cfg 键与 DistX 相同是原有行为（两字段实际共享同一 entry），保持原样不“修复”
            SubtitleBgShadowDistY = Reg(
                SubtitleAdvancedSection, "字幕 背景阴影水平偏移 X", -2f,
                new ConfigDescription("背景阴影：水平偏移（px）", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleBgShadowDistY", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleBgShadowUseGraphicAlpha = Reg(
                SubtitleAdvancedSection, "字幕 背景阴影叠乘文字透明度", true,
                new ConfigDescription("背景阴影是否叠乘文字透明度", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "SubtitleBgShadowUseGraphicAlpha", s_SubtitleFoldTargets, refreshSubtitleStyle);

            // 布局
            SubtitleLayoutAnchor = Reg(
                SubtitleAdvancedSection, "字幕 布局锚点", TextAnchorOption.LowerCenter,
                new ConfigDescription("锚点（TextAnchor）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "SubtitleLayoutAnchor", s_SubtitleFoldTargets, refreshSubtitleLayout);

            SubtitleLayoutOffsetX = Reg(
                SubtitleAdvancedSection, "字幕 布局锚点位移（X轴）", 0f,
                new ConfigDescription("相对锚点水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "SubtitleLayoutOffsetX", s_SubtitleFoldTargets, refreshSubtitleLayout);

            SubtitleLayoutOffsetY = Reg(
                SubtitleAdvancedSection, "字幕 布局锚点位移（Y轴）", 0f,
                new ConfigDescription("相对锚点垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "SubtitleLayoutOffsetY", s_SubtitleFoldTargets, refreshSubtitleLayout);

            SubtitleLayoutSafeArea = Reg(
                SubtitleAdvancedSection, "字幕 布局安全区", true,
                new ConfigDescription("是否考虑安全区。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "SubtitleLayoutSafeArea", s_SubtitleFoldTargets, refreshSubtitleLayout);

            SubtitleLayoutMaxWidthPercent = Reg(
                SubtitleAdvancedSection, "字幕 布局最大宽度占比", 0.90f,
                new ConfigDescription("文本测量最大宽度占屏比例（0~1）。",
                    new AcceptableValueRange<float>(0.5f, 1.0f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "SubtitleLayoutMaxWidthPercent", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleLayoutLineSpacing = Reg(
                SubtitleAdvancedSection, "字幕 布局行距", 4.0f,
                new ConfigDescription("额外行距（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "SubtitleLayoutLineSpacing", s_SubtitleFoldTargets, refreshSubtitleLayout);

            SubtitleLayoutOverrideAlign = Reg(
                SubtitleAdvancedSection, "字幕 布局覆盖文本对齐", TextAnchorOption.None,
                new ConfigDescription("可选：强制 Text 对齐（None 表示不改）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "SubtitleLayoutOverrideAlign", s_SubtitleFoldTargets, refreshSubtitleStyle);

            SubtitleLayoutStackOffsetPercent = Reg(
                SubtitleAdvancedSection, "字幕 布局底部堆叠上移", 0.12f,
                new ConfigDescription("字幕堆叠面板距底部相对高度（0~0.5）。",
                    new AcceptableValueRange<float>(0f, 0.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "SubtitleLayoutStackOffsetPercent", s_SubtitleFoldTargets, refreshSubtitleLayout);

            DanmakuShowRoleTag = Reg(
                DanmakuGeneralSection,
                "弹幕 显示说话者",
                true,
                new ConfigDescription(
                    "是否在弹幕中显示说话者（例如“你/Scav/Tagilla：”）。关闭后仅显示台词文本。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    }),
                "DanmakuShowRoleTag", s_DanmakuFoldTargets);

            DanmakuShowPmcName = Reg(DanmakuGeneralSection, "弹幕 显示PMC名字", false, new ConfigDescription("是否显示PMC游戏内的ID", null, new ConfigurationManagerAttributes { }), "DanmakuShowPmcName", s_DanmakuFoldTargets);
            DanmakuShowScavName = Reg(DanmakuGeneralSection, "弹幕 显示Scav名字", false, new ConfigDescription("是否显示Scav游戏内的ID\n不推荐，因为Scav游戏名字太长可能会导致台词观感很差。", null, new ConfigurationManagerAttributes { }), "DanmakuShowScavName", s_DanmakuFoldTargets);

            DanmakuPlayerSelfPronoun = Reg(
                DanmakuGeneralSection,
                "弹幕 玩家说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当玩家自己说话时弹幕显示风格：\n- 略称：始终显示“你”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "DanmakuPlayerSelfPronoun", s_DanmakuFoldTargets);

            DanmakuTeammateSelfPronoun = Reg(
                DanmakuGeneralSection,
                "弹幕 队友说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当友军（队友）说话时弹幕显示风格：\n- 略称：始终显示“队友”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "DanmakuTeammateSelfPronoun", s_DanmakuFoldTargets);

            DanmakuMaxDistanceMeters = Reg(
                DanmakuGeneralSection,
                "弹幕 最大语音接收距离（米）",
                100f,
                new ConfigDescription(
                    "当语音来源距离玩家超过该距离时，不显示弹幕\n10~150 米，默认 100 米。",
                    new AcceptableValueRange<float>(10f, 150f),
                    new ConfigurationManagerAttributes
                    {
                    }),
                "DanmakuMaxDistanceMeters", s_DanmakuFoldTargets);

            DanmakuShowDistance = Reg(
                DanmakuGeneralSection,
                "弹幕 显示距离",
                true,
                new ConfigDescription(
                    "是否显示距离。开启后，会在语音后面添加一个类似“ ·10m”的字样",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    }),
                "DanmakuShowDistance", s_DanmakuFoldTargets);

            EnableMapBroadcastDanmaku = Reg(
                DanmakuGeneralSection,
                "弹幕 启动实验室广播",
                true,
                new ConfigDescription(
                    "启用后会把 实验室公共播报 显示为字幕（即实验室拉闸/开关的全图播报）。",
                    null,
                    new ConfigurationManagerAttributes
                    {

                    }),
                "EnableMapBroadcastDanmaku", s_DanmakuFoldTargets);

            DanmakuZombieEnabled = Reg(
                DanmakuGeneralSection,
                "弹幕 丧尸显示台词（除 丧尸Tagilla）",
                true,
                new ConfigDescription(
                    "丧尸台词是否显示（不影响 丧尸Tagilla）。开启则正常显示丧尸台词，关闭则不显示所有普通丧尸的台词",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    }),
                "DanmakuZombieEnabled", s_DanmakuFoldTargets);

            DanmakuZombieCooldownSec = Reg(
                DanmakuGeneralSection,
                "弹幕 丧尸台词间隔（秒）",
                5,
                new ConfigDescription(
                    "丧尸类台词的最小间隔（秒）。第一条丧尸台词出现后，在此设置时间区间内其它丧尸台词会被忽略。\n0 表示不限制。推荐5-10以上，否则会导致大量的台词刷屏。",
                    new AcceptableValueRange<int>(0, 60),
                    new ConfigurationManagerAttributes
                    {
                        Indent = 1
                    }),
                "DanmakuZombieCooldownSec", s_DanmakuFoldTargets);

            // —— Danmaku 字体 ——
            DanmakuFontBundleName = Reg(
                DanmakuAdvancedSection, "弹幕 字体资源包", "",
                new ConfigDescription(
                    "从 BepInEx\\plugins\\FontReplace\\Font 选择字体资源包（不覆盖则留空）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawFontBundlePicker,
                        HideDefaultButton = true
                    }),
                "DanmakuFontBundleName", s_DanmakuFoldTargets, refreshDanmakuStyle);
            DanmakuFontFamilyCsv = Reg(
                DanmakuAdvancedSection, "弹幕 字体类型",
                "SimHei;Microsoft YaHei;Microsoft YaHei UI;DengXian;Noto Sans CJK SC",
                new ConfigDescription("字体候选，逗号或分号分隔；支持 game:FontName 走游戏内置字体。\n游戏将优先从左往右依次检测支持的字体类型，最后退回Arial.ttf",
                    null, new ConfigurationManagerAttributes { IsAdvanced = true }),
                "DanmakuFontFamilyCsv", s_DanmakuFoldTargets, refreshDanmakuStyle, csv: true);

            DanmakuFontSize = Reg(
                DanmakuAdvancedSection, "弹幕 字体尺寸",
                24,
                new ConfigDescription("弹幕字体尺寸大小（px）。", new AcceptableValueRange<int>(12, 64),
                 new ConfigurationManagerAttributes { }),
                "DanmakuFontSize", s_DanmakuFoldTargets, refreshDanmakuStyle);

            DanmakuFontBold = Reg(
                DanmakuAdvancedSection, "弹幕 字体加粗", false,
                new ConfigDescription("弹幕字体加粗。", null,
                    new ConfigurationManagerAttributes { }),
                "DanmakuFontBold", s_DanmakuFoldTargets, refreshDanmakuStyle);

            DanmakuFontItalic = Reg(
                DanmakuAdvancedSection, "弹幕 字体斜体", false,
                new ConfigDescription("弹幕字体斜体。", null,
                    new ConfigurationManagerAttributes { }),
                "DanmakuFontItalic", s_DanmakuFoldTargets, refreshDanmakuStyle);

            // —— Danmaku 描边 ——
            DanmakuOutlineEnabled = Reg(
                DanmakuAdvancedSection, "弹幕 字体描边", true,
                new ConfigDescription("启用弹幕描边。", null,
                    new ConfigurationManagerAttributes { }),
                "DanmakuOutlineEnabled", s_DanmakuFoldTargets, refreshDanmakuStyle);

            DanmakuOutlineColor = Reg(
                DanmakuAdvancedSection, "弹幕 字体描边颜色", new Color(0f, 0f, 0f, 0.88f), // #000000E0 ≈ A=0.88
                new ConfigDescription("弹幕描边颜色。", null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "DanmakuOutlineColor", s_DanmakuFoldTargets, refreshDanmakuStyle);

            DanmakuOutlineDistX = Reg(
                DanmakuAdvancedSection, "弹幕 字体描边水平位移（X轴）", 1.2f,
                new ConfigDescription("描边水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "DanmakuOutlineDistX", s_DanmakuFoldTargets, refreshDanmakuStyle);

            DanmakuOutlineDistY = Reg(
                DanmakuAdvancedSection, "弹幕 字体描边水平位移（Y轴）", 1.2f,
                new ConfigDescription("描边垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "DanmakuOutlineDistY", s_DanmakuFoldTargets, refreshDanmakuStyle);

            // —— Danmaku 阴影 ——
            DanmakuShadowEnabled = Reg(
                DanmakuAdvancedSection, "弹幕 字体阴影", true,
                new ConfigDescription("启用弹幕阴影。", null,
                    new ConfigurationManagerAttributes { }),
                "DanmakuShadowEnabled", s_DanmakuFoldTargets, refreshDanmakuStyle);

            DanmakuShadowColor = Reg(
                DanmakuAdvancedSection, "弹幕 字体阴影颜色", new Color(0f, 0f, 0f, 0.55f),
                new ConfigDescription("弹幕阴影颜色。", null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "DanmakuShadowColor", s_DanmakuFoldTargets, refreshDanmakuStyle);

            DanmakuShadowDistX = Reg(
                DanmakuAdvancedSection, "弹幕 字体阴影水平位移（X轴）", 2f,
                new ConfigDescription("阴影水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "DanmakuShadowDistX", s_DanmakuFoldTargets, refreshDanmakuStyle);

            DanmakuShadowDistY = Reg(
                DanmakuAdvancedSection, "弹幕 字体阴影水平位移（Y轴）", -2f,
                new ConfigDescription("阴影垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "DanmakuShadowDistY", s_DanmakuFoldTargets, refreshDanmakuStyle);

            DanmakuShadowUseGraphicAlpha = Reg(
                DanmakuAdvancedSection, "弹幕 字体阴影叠乘文本透明度", true,
                new ConfigDescription("是否叠乘文本透明度。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "DanmakuShadowUseGraphicAlpha", s_DanmakuFoldTargets, refreshDanmakuStyle);

            // —— Danmaku 车道/速度/区间 ——
            DanmakuLanes = Reg(
                DanmakuAdvancedSection,
                "弹幕 车道数量",
                8,
                new ConfigDescription(
                    "弹幕车道数量。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    }),
                "DanmakuLanes", s_DanmakuFoldTargets, refreshDanmaku);

            DanmakuSpeed = Reg(
                DanmakuAdvancedSection,
                "弹幕 速度（px/s）",
                180f,
                new ConfigDescription(
                    "弹幕速度（像素/秒）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    }),
                "DanmakuSpeed", s_DanmakuFoldTargets, refreshDanmaku);

            DanmakuMinGapPx = Reg(
                DanmakuAdvancedSection,
                "弹幕 同车道最小间隔",
                40,
                new ConfigDescription(
                    "同车道最小间隔像素。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    }),
                "DanmakuMinGapPx", s_DanmakuFoldTargets, refreshDanmaku);

            DanmakuSpawnDelaySec = Reg(
                DanmakuAdvancedSection,
                "弹幕 新弹幕输出间隔（秒）",
                0.20f,
                new ConfigDescription(
                    "两条弹幕之间的最小发送间隔（秒）。建议 0.1~0.3。",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes
                    {
                        DispName = "新弹幕 输出间隔（秒）",
                        IsAdvanced = true
                    }),
                "DanmakuSpawnDelaySec", s_DanmakuFoldTargets, refreshDanmaku);

            DanmakuTopOffsetPercent = Reg(
                DanmakuAdvancedSection,
                "弹幕 顶部起始位置（相对）",
                0.10f,
                new ConfigDescription(
                    "弹幕距屏幕顶部的相对起始高度（0~0.5，默认 0.10）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    }),
                "DanmakuTopOffsetPercent", s_DanmakuFoldTargets, refreshDanmaku);

            DanmakuAreaMaxPercent = Reg(
                DanmakuAdvancedSection,
                "弹幕 最大垂直占比",
                0.35f,
                new ConfigDescription(
                    "弹幕允许占用的最大垂直高度（0~1，默认 0.35）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    }),
                "DanmakuAreaMaxPercent", s_DanmakuFoldTargets, refreshDanmaku);

            // —— 4) World3D —— //
            World3DShowRoleTag = Reg(
                World3DGeneralSection,
                "3D气泡 显示说话者",
                true,
                new ConfigDescription(
                    "是否在气泡中显示说话者（roletag）。关闭后仅显示台词文本（可选加距离）。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "World3DShowRoleTag", s_World3DFoldTargets);

            World3DShowPmcName = Reg(
                World3DGeneralSection,
                "3D气泡 显示PMC名字",
                false,
                new ConfigDescription("是否显示PMC游戏内的ID", null, new ConfigurationManagerAttributes { }),
                "World3DShowPmcName", s_World3DFoldTargets);

            World3DShowScavName = Reg(
                World3DGeneralSection,
                "3D气泡 显示Scav名字",
                false,
                new ConfigDescription("是否显示Scav游戏内的ID\n不推荐，因为Scav游戏名字太长可能会导致台词观感很差。", null, new ConfigurationManagerAttributes { }),
                "World3DShowScavName", s_World3DFoldTargets);

            World3DPlayerSelfPronoun = Reg(
                World3DGeneralSection,
                "3D气泡 玩家说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当玩家自己说话时3D气泡显示风格：\n- 略称：始终显示“你”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "World3DPlayerSelfPronoun", s_World3DFoldTargets);

            World3DTeammateSelfPronoun = Reg(
                World3DGeneralSection,
                "3D气泡 队友说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当友军（队友）说话时3D气泡显示风格：\n- 略称：始终显示“队友”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "World3DTeammateSelfPronoun", s_World3DFoldTargets);

            World3DMaxDistanceMeters = Reg(
                World3DGeneralSection,
                "3D气泡 最大语音接收距离（米）",
                30f,
                new ConfigDescription(
                    "当语音来源距离玩家超过该距离时，不显示气泡。\n10~150 米，默认 30 米",
                    new AcceptableValueRange<float>(10f, 150f),
                    new ConfigurationManagerAttributes { }),
                "World3DMaxDistanceMeters", s_World3DFoldTargets);

            World3DShowDistance = Reg(
                World3DGeneralSection,
                "3D气泡 显示距离",
                true,
                new ConfigDescription(
                    "是否显示距离。开启后，会在语音后面添加一个类似“ ·10m”的字样",
                    null,
                    new ConfigurationManagerAttributes { }),
                "World3DShowDistance", s_World3DFoldTargets);

            World3DDisplayDelaySec = Reg(
                World3DGeneralSection,
                "3D气泡 台词显示延迟（秒）",
                0.5f,
                new ConfigDescription(
                    "3D气泡显示后，额外延迟消失的秒数，避免短语音瞬间出现又消失。",
                    new AcceptableValueRange<float>(0f, 3f),
                    new ConfigurationManagerAttributes { }),
                "World3DDisplayDelaySec", s_World3DFoldTargets);

            World3DShowSelf = Reg(
                World3DGeneralSection,
                "3D气泡 显示自己",
                false,
                new ConfigDescription(
                    "是否显示玩家自己说话的气泡。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "World3DShowSelf", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DZombieEnabled = Reg(
                World3DGeneralSection,
                "3D气泡 丧尸显示台词（除 丧尸Tagilla）",
                true,
                new ConfigDescription(
                    "丧尸台词是否显示（不影响 丧尸Tagilla）。开启则正常显示丧尸台词，关闭则不显示所有普通丧尸的台词",
                    null,
                    new ConfigurationManagerAttributes { }),
                "World3DZombieEnabled", s_World3DFoldTargets);

            World3DZombieCooldownSec = Reg(
                World3DGeneralSection,
                "3D气泡 丧尸台词间隔（秒）",
                10,
                new ConfigDescription(
                    "丧尸类台词的最小间隔（秒）。第一条丧尸台词出现后，在此设置时间区间内其它丧尸台词会被忽略。\n0 表示不限制。推荐5-10以上，否则会导致大量的台词刷屏。",
                    new AcceptableValueRange<int>(0, 60),
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "World3DZombieCooldownSec", s_World3DFoldTargets);

            World3DVerticalOffsetY = Reg(
                World3DGeneralSection,
                "3D气泡 垂直偏移（米）",
                0.2f,
                new ConfigDescription(
                    "气泡整体向上/向下的偏移量（米）。正值向上，负值向下。",
                    new AcceptableValueRange<float>(-1.0f, 1.0f),
                    new ConfigurationManagerAttributes { }),
                "World3DVerticalOffsetY", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DFacePlayer = Reg(
                World3DGeneralSection,
                "3D气泡 朝向玩家",
                true,
                new ConfigDescription(
                    "是否让气泡始终朝向玩家视角。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "World3DFacePlayer", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DBGEnabled = Reg(
                World3DGeneralSection,
                "3D气泡 背景",
                true,
                new ConfigDescription(
                    "是否显示气泡背景。",
                    null,
                    new ConfigurationManagerAttributes { }),
                "World3DBGEnabled", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DBGColor = Reg(
                World3DGeneralSection,
                "3D气泡 背景颜色",
                new Color(0f, 0f, 0f, 0.65f),
                new ConfigDescription(
                    "气泡背景颜色（含透明度）。",
                    null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "World3DBGColor", s_World3DFoldTargets, refreshWorld3DStyle);

            // —— World3D 字体 ——
            World3DFontBundleName = Reg(
                World3DAdvancedSection, "3D气泡 字体资源包", "",
                new ConfigDescription(
                    "从 BepInEx\\plugins\\FontReplace\\Font 选择字体资源包（不覆盖则留空）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawFontBundlePicker,
                        HideDefaultButton = true
                    }),
                "World3DFontBundleName", s_World3DFoldTargets, refreshWorld3DStyle);
            World3DFontFamilyCsv = Reg(
                World3DAdvancedSection, "3D气泡 字体类型",
                "SimHei;Microsoft YaHei;Microsoft YaHei UI;DengXian;Noto Sans CJK SC",
                new ConfigDescription("字体候选，分号;隔开(需要大写分号)\n支持 game:FontName 走游戏内置字体。",
                    null, new ConfigurationManagerAttributes { IsAdvanced = true }),
                "World3DFontFamilyCsv", s_World3DFoldTargets, refreshWorld3DStyle, csv: true);

            World3DFontSize = Reg(
                World3DAdvancedSection, "3D气泡 字体尺寸（px）", 26,
                new ConfigDescription("字体尺寸大小（px）", new AcceptableValueRange<int>(12, 64),
                    new ConfigurationManagerAttributes { }),
                "World3DFontSize", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DFontBold = Reg(
                World3DAdvancedSection, "3D气泡 字体加粗", false,
                new ConfigDescription("3D气泡字体加粗。", null, new ConfigurationManagerAttributes { }),
                "World3DFontBold", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DFontItalic = Reg(
                World3DAdvancedSection, "3D气泡 字体斜体", false,
                new ConfigDescription("3D气泡字体斜体。", null, new ConfigurationManagerAttributes { }),
                "World3DFontItalic", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DAlignment = Reg(
                World3DAdvancedSection, "3D气泡 文本对齐", TextAnchorOption.MiddleCenter,
                new ConfigDescription("文本对齐（TextAnchor）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "World3DAlignment", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DWrap = Reg(
                World3DAdvancedSection, "3D气泡 自动换行", true,
                new ConfigDescription("是否开启自动换行，若开启则按照下方换行限制进行，禁用则不换行", null,
                    new ConfigurationManagerAttributes { }),
                "World3DWrap", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DWrapLength = Reg(
                World3DAdvancedSection, "3D气泡 自动换行长度阈值", 0,
                new ConfigDescription("超过 N 个可见字符后强制换行（0 关闭）。", null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "World3DWrapLength", s_World3DFoldTargets, refreshWorld3DStyle);

            // —— World3D 描边 ——
            World3DOutlineEnabled = Reg(
                World3DAdvancedSection, "3D气泡 字体描边", true,
                new ConfigDescription("启用描边。", null,
                    new ConfigurationManagerAttributes { }),
                "World3DOutlineEnabled", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DOutlineColor = Reg(
                World3DAdvancedSection, "3D气泡 字体描边颜色", new Color(0f, 0f, 0f, 0.95f),
                new ConfigDescription("描边颜色。", null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "World3DOutlineColor", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DOutlineDistX = Reg(
                World3DAdvancedSection, "3D气泡 字体描边位移（X轴）", 1.5f,
                new ConfigDescription("描边水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "World3DOutlineDistX", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DOutlineDistY = Reg(
                World3DAdvancedSection, "3D气泡 字体描边位移（Y轴）", 1.5f,
                new ConfigDescription("描边垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "World3DOutlineDistY", s_World3DFoldTargets, refreshWorld3DStyle);

            // —— World3D 阴影 ——
            World3DShadowEnabled = Reg(
                World3DAdvancedSection, "3D气泡 字体阴影", true,
                new ConfigDescription("启用阴影。", null,
                    new ConfigurationManagerAttributes { }),
                "World3DShadowEnabled", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DShadowColor = Reg(
                World3DAdvancedSection, "3D气泡 字体阴影颜色", new Color(0f, 0f, 0f, 0.6f),
                new ConfigDescription("阴影颜色。", null,
                    new ConfigurationManagerAttributes { Indent = 1 }),
                "World3DShadowColor", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DShadowDistX = Reg(
                World3DAdvancedSection, "3D气泡 字体阴影位移（X轴）", 2f,
                new ConfigDescription("阴影水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "World3DShadowDistX", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DShadowDistY = Reg(
                World3DAdvancedSection, "3D气泡 字体阴影位移（Y轴）", -2f,
                new ConfigDescription("阴影垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "World3DShadowDistY", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DShadowUseGraphicAlpha = Reg(
                World3DAdvancedSection, "3D气泡 字体阴影叠乘文本透明度", true,
                new ConfigDescription("阴影是否叠乘文字透明度", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "World3DShadowUseGraphicAlpha", s_World3DFoldTargets, refreshWorld3DStyle);

            // —— World3D 缩放/朝向/堆叠/淡入淡出 ——
            World3DWorldScale = Reg(
                World3DAdvancedSection, "3D气泡 世界缩放", 0.01f,
                new ConfigDescription("世界空间缩放系数；值越大越清晰也越大。默认 0.01。", new AcceptableValueRange<float>(0.002f, 0.05f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "World3DWorldScale", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DDynamicPixelsPerUnit = Reg(
                World3DAdvancedSection, "3D气泡 动态像素密度", 20f,
                new ConfigDescription("CanvasScaler.dynamicPixelsPerUnit；越大越清晰但更耗性能。", new AcceptableValueRange<float>(5f, 120f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "World3DDynamicPixelsPerUnit", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DFaceUpdateIntervalSec = Reg(
                World3DAdvancedSection, "3D气泡 朝向更新间隔（秒）", 0f,
                new ConfigDescription("0 表示每帧朝向玩家；>0 则按间隔更新，减少抖动/模糊。", new AcceptableValueRange<float>(0f, 0.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = true, Indent = 1 }),
                "World3DFaceUpdateIntervalSec", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DStackMaxLines = Reg(
                World3DAdvancedSection, "3D气泡 叠加最大行数", 3,
                new ConfigDescription("同一角色连续说话时可叠加的最大行数。", new AcceptableValueRange<int>(1, 6),
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "World3DStackMaxLines", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DStackOffsetY = Reg(
                World3DAdvancedSection, "3D气泡 叠加上移间距", 0.18f,
                new ConfigDescription("多行叠加时每行向上偏移的高度。", new AcceptableValueRange<float>(0.05f, 0.6f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "World3DStackOffsetY", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DFadeInSec = Reg(
                World3DAdvancedSection, "3D气泡 淡入时长（秒）", 0.15f,
                new ConfigDescription("3D气泡淡入耗时。", new AcceptableValueRange<float>(0f, 1.0f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "World3DFadeInSec", s_World3DFoldTargets, refreshWorld3DStyle);

            World3DFadeOutSec = Reg(
                World3DAdvancedSection, "3D气泡 淡出时长（秒）", 0.25f,
                new ConfigDescription("3D气泡淡出耗时。", new AcceptableValueRange<float>(0f, 1.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }),
                "World3DFadeOutSec", s_World3DFoldTargets, refreshWorld3DStyle);

            // 仅 Bind：测试按钮不进 折叠/预设快照/刷新 注册表
            entries.Add(TestDanmakuButton = Config.Bind(
                GeneralSection,
                "▶ 随机测试弹幕（3条）",
                "", // 值无意义，仅用于占位
                new ConfigDescription(
                    "点击右侧按钮随机发 3 条测试弹幕。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawTestDanmakuButton,
                        HideDefaultButton = true
                    })));

            // ——  Color —— //
            SubRole_Player = Reg(SubRoleColorSection, "玩家 角色颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_Player", s_SubtitleFoldTargets);
            SubRole_Teammate = Reg(SubRoleColorSection, "队友 角色颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_Teammate", s_SubtitleFoldTargets);
            SubRole_PmcBear = Reg(SubRoleColorSection, "Bear 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_PmcBear", s_SubtitleFoldTargets);
            SubRole_PmcUsec = Reg(SubRoleColorSection, "Usec 角色颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_PmcUsec", s_SubtitleFoldTargets);
            SubRole_Scav = Reg(SubRoleColorSection, "Scav 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_Scav", s_SubtitleFoldTargets);
            SubRole_Raider = Reg(SubRoleColorSection, "Raider 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_Raider", s_SubtitleFoldTargets);
            SubRole_Rogue = Reg(SubRoleColorSection, "Rogue 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_Rogue", s_SubtitleFoldTargets);
            SubRole_Cultist = Reg(SubRoleColorSection, "邪教徒 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_Cultist", s_SubtitleFoldTargets);
            SubRole_BossFollower = Reg(SubRoleColorSection, "Boss小弟 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_BossFollower", s_SubtitleFoldTargets);
            SubRole_Zombie = Reg(SubRoleColorSection, "丧尸 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_Zombie", s_SubtitleFoldTargets);
            SubRole_Goons = Reg(SubRoleColorSection, "三狗 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_Goons", s_SubtitleFoldTargets);
            SubRole_Bosses = Reg(SubRoleColorSection, "Boss 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_Bosses", s_SubtitleFoldTargets);
            SubRole_LabAnnouncer = Reg(SubRoleColorSection, "实验室广播 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "SubRole_LabAnnouncer", s_SubtitleFoldTargets);

            SubText_Player = Reg(SubRoleTextColorSection, "玩家 文本颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_Player", s_SubtitleFoldTargets);
            SubText_Teammate = Reg(SubRoleTextColorSection, "队友 文本颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_Teammate", s_SubtitleFoldTargets);
            SubText_PmcBear = Reg(SubRoleTextColorSection, "Bear 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_PmcBear", s_SubtitleFoldTargets);
            SubText_PmcUsec = Reg(SubRoleTextColorSection, "Usec 文本颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_PmcUsec", s_SubtitleFoldTargets);
            SubText_Scav = Reg(SubRoleTextColorSection, "Scav 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_Scav", s_SubtitleFoldTargets);
            SubText_Raider = Reg(SubRoleTextColorSection, "Raider 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_Raider", s_SubtitleFoldTargets);
            SubText_Rogue = Reg(SubRoleTextColorSection, "Rogue 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_Rogue", s_SubtitleFoldTargets);
            SubText_Cultist = Reg(SubRoleTextColorSection, "邪教徒 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_Cultist", s_SubtitleFoldTargets);
            SubText_BossFollower = Reg(SubRoleTextColorSection, "Boss小弟 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_BossFollower", s_SubtitleFoldTargets);
            SubText_Zombie = Reg(SubRoleTextColorSection, "丧尸 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_Zombie", s_SubtitleFoldTargets);
            SubText_Goons = Reg(SubRoleTextColorSection, "三狗 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_Goons", s_SubtitleFoldTargets);
            SubText_Bosses = Reg(SubRoleTextColorSection, "Boss 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_Bosses", s_SubtitleFoldTargets);
            SubText_LabAnnouncer = Reg(SubRoleTextColorSection, "实验室广播 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "SubText_LabAnnouncer", s_SubtitleFoldTargets);

            DmRole_Player = Reg(DmRoleColorSection, "玩家 角色颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_Player", s_DanmakuFoldTargets);
            DmRole_Teammate = Reg(DmRoleColorSection, "队友 角色颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_Teammate", s_DanmakuFoldTargets);
            DmRole_PmcBear = Reg(DmRoleColorSection, "Bear 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_PmcBear", s_DanmakuFoldTargets);
            DmRole_PmcUsec = Reg(DmRoleColorSection, "Usec 角色颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_PmcUsec", s_DanmakuFoldTargets);
            DmRole_Scav = Reg(DmRoleColorSection, "Scav 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_Scav", s_DanmakuFoldTargets);
            DmRole_Raider = Reg(DmRoleColorSection, "Raider 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_Raider", s_DanmakuFoldTargets);
            DmRole_Rogue = Reg(DmRoleColorSection, "Rogue 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_Rogue", s_DanmakuFoldTargets);
            DmRole_Cultist = Reg(DmRoleColorSection, "邪教徒 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_Cultist", s_DanmakuFoldTargets);
            DmRole_BossFollower = Reg(DmRoleColorSection, "Boss小弟 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_BossFollower", s_DanmakuFoldTargets);
            DmRole_Zombie = Reg(DmRoleColorSection, "丧尸 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_Zombie", s_DanmakuFoldTargets);
            DmRole_Goons = Reg(DmRoleColorSection, "三狗 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_Goons", s_DanmakuFoldTargets);
            DmRole_Bosses = Reg(DmRoleColorSection, "Boss 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_Bosses", s_DanmakuFoldTargets);
            DmRole_LabAnnouncer = Reg(DmRoleColorSection, "实验室广播 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "DmRole_LabAnnouncer", s_DanmakuFoldTargets);

            DmText_Player = Reg(DmRoleTextColorSection, "玩家 文本颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_Player", s_DanmakuFoldTargets);
            DmText_Teammate = Reg(DmRoleTextColorSection, "队友 文本颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_Teammate", s_DanmakuFoldTargets);
            DmText_PmcBear = Reg(DmRoleTextColorSection, "Bear 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_PmcBear", s_DanmakuFoldTargets);
            DmText_PmcUsec = Reg(DmRoleTextColorSection, "Usec 文本颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_PmcUsec", s_DanmakuFoldTargets);
            DmText_Scav = Reg(DmRoleTextColorSection, "Scav 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_Scav", s_DanmakuFoldTargets);
            DmText_Raider = Reg(DmRoleTextColorSection, "Raider 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_Raider", s_DanmakuFoldTargets);
            DmText_Rogue = Reg(DmRoleTextColorSection, "Rogue 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_Rogue", s_DanmakuFoldTargets);
            DmText_Cultist = Reg(DmRoleTextColorSection, "邪教徒 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_Cultist", s_DanmakuFoldTargets);
            DmText_BossFollower = Reg(DmRoleTextColorSection, "Boss小弟 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_BossFollower", s_DanmakuFoldTargets);
            DmText_Zombie = Reg(DmRoleTextColorSection, "丧尸 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_Zombie", s_DanmakuFoldTargets);
            DmText_Goons = Reg(DmRoleTextColorSection, "三狗 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_Goons", s_DanmakuFoldTargets);
            DmText_Bosses = Reg(DmRoleTextColorSection, "Boss 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_Bosses", s_DanmakuFoldTargets);
            DmText_LabAnnouncer = Reg(DmRoleTextColorSection, "实验室广播 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "DmText_LabAnnouncer", s_DanmakuFoldTargets);

            W3dRole_Player = Reg(W3dRoleColorSection, "玩家 角色颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_Player", s_World3DFoldTargets);
            W3dRole_Teammate = Reg(W3dRoleColorSection, "队友 角色颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_Teammate", s_World3DFoldTargets);
            W3dRole_PmcBear = Reg(W3dRoleColorSection, "Bear 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_PmcBear", s_World3DFoldTargets);
            W3dRole_PmcUsec = Reg(W3dRoleColorSection, "Usec 角色颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_PmcUsec", s_World3DFoldTargets);
            W3dRole_Scav = Reg(W3dRoleColorSection, "Scav 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_Scav", s_World3DFoldTargets);
            W3dRole_Raider = Reg(W3dRoleColorSection, "Raider 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_Raider", s_World3DFoldTargets);
            W3dRole_Rogue = Reg(W3dRoleColorSection, "Rogue 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_Rogue", s_World3DFoldTargets);
            W3dRole_Cultist = Reg(W3dRoleColorSection, "邪教徒 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_Cultist", s_World3DFoldTargets);
            W3dRole_BossFollower = Reg(W3dRoleColorSection, "Boss小弟 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_BossFollower", s_World3DFoldTargets);
            W3dRole_Zombie = Reg(W3dRoleColorSection, "丧尸 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_Zombie", s_World3DFoldTargets);
            W3dRole_Goons = Reg(W3dRoleColorSection, "三狗 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_Goons", s_World3DFoldTargets);
            W3dRole_Bosses = Reg(W3dRoleColorSection, "Boss 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_Bosses", s_World3DFoldTargets);
            W3dRole_LabAnnouncer = Reg(W3dRoleColorSection, "实验室广播 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { }), "W3dRole_LabAnnouncer", s_World3DFoldTargets);

            W3dText_Player = Reg(W3dRoleTextColorSection, "玩家 文本颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_Player", s_World3DFoldTargets);
            W3dText_Teammate = Reg(W3dRoleTextColorSection, "队友 文本颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_Teammate", s_World3DFoldTargets);
            W3dText_PmcBear = Reg(W3dRoleTextColorSection, "Bear 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_PmcBear", s_World3DFoldTargets);
            W3dText_PmcUsec = Reg(W3dRoleTextColorSection, "Usec 文本颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_PmcUsec", s_World3DFoldTargets);
            W3dText_Scav = Reg(W3dRoleTextColorSection, "Scav 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_Scav", s_World3DFoldTargets);
            W3dText_Raider = Reg(W3dRoleTextColorSection, "Raider 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_Raider", s_World3DFoldTargets);
            W3dText_Rogue = Reg(W3dRoleTextColorSection, "Rogue 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_Rogue", s_World3DFoldTargets);
            W3dText_Cultist = Reg(W3dRoleTextColorSection, "邪教徒 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_Cultist", s_World3DFoldTargets);
            W3dText_BossFollower = Reg(W3dRoleTextColorSection, "Boss小弟 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_BossFollower", s_World3DFoldTargets);
            W3dText_Zombie = Reg(W3dRoleTextColorSection, "丧尸 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_Zombie", s_World3DFoldTargets);
            W3dText_Goons = Reg(W3dRoleTextColorSection, "三狗 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_Goons", s_World3DFoldTargets);
            W3dText_Bosses = Reg(W3dRoleTextColorSection, "Boss 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_Bosses", s_World3DFoldTargets);
            W3dText_LabAnnouncer = Reg(W3dRoleTextColorSection, "实验室广播 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { }), "W3dText_LabAnnouncer", s_World3DFoldTargets);


            // —— 99) Debug —— //
            // 仅 Bind：调试项不进 折叠/预设快照/刷新 注册表
            entries.Add(EnableDebugTools = Config.Bind(
                DebugSection,
                "启动调试工具",
                false,
                new ConfigDescription(
                    "启用短句调试面板（仅开发/听写）。关闭则不创建面板，不影响正式游戏。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "启用调试工具",
                        Category = "99.调试",
                        Description = "启用短句调试面板（仅开发/听写）。",
            IsAdvanced = true
                    })));

            entries.Add(DebugPanelHotkey = Config.Bind(
                DebugSection,
                "短句调试面板 快捷键",
                new KeyboardShortcut(KeyCode.F8),
                new ConfigDescription(
                    "显示/隐藏 短句调试面板 的热键。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "调试面板 热键",
                        Category = "99.调试",
                        Description = "显示/隐藏 短句调试面板 的热键。",
            IsAdvanced = true
                    })));

            entries.Add(VoiceDedupWindowSec = Config.Bind(
                DebugSection,
                "语音去重窗口（秒）",
                0.40f,
                new ConfigDescription(
                    "同一说话者在窗口内的同类语音事件（同触发类型/同网络索引）只显示一次；0 表示关闭去重。",
                    new AcceptableValueRange<float>(0f, 1.0f),
                    new ConfigurationManagerAttributes
                    {
                        DispName = "语音去重窗口（秒）",
                        Category = "99.调试",
                        Description = "0~1.0，默认 0.40。窗口内重复触发只显示一次。设为 0 可关闭。",
                        IsAdvanced = true
                    })));

            entries.Add(DanmakuDebugVerbose = Config.Bind(
                DebugSection,
                "弹幕：详细调试日志",
                false,
                new ConfigDescription(
                    "弹幕详细调试日志（临时）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "弹幕：详细调试日志",
                        Category = "99.调试",
                        Description = "弹幕详细调试日志（临时）。",
            IsAdvanced = true
                    })));

            entries.Add(MapBroadcastDebug = Config.Bind(
                DebugSection,
                "地图广播：调试日志",
                false,
                new ConfigDescription(
                    "弹幕详细调试日志（临时）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "地图广播：调试日志",
                        Category = "99.调试",
                        Description = "地图广播：调试日志（打印匹配与拦截详情）",
            IsAdvanced = true
                    })));

            // 设置窗口不透明度：GUI 偏好（presetKey 为 null），拖动滑条即时生效 ——
            // 调低后可透过设置窗口看到下方飞行的测试字幕/弹幕（不改变 Canvas 排序，字幕仍在窗口下层）
            SettingsWindowOpacity = Reg(
                DebugSection,
                "设置界面 不透明度",
                1.0f,
                new ConfigDescription(
                    "图形化设置界面的整体不透明度（0.2~1.0）。\n调低后可透过窗口看到“随机测试字幕/弹幕”飞过的效果。",
                    new AcceptableValueRange<float>(0.2f, 1.0f),
                    new ConfigurationManagerAttributes
                    {
                        DispName = "设置界面 不透明度",
                        Category = "99.调试",
                        Description = "图形化设置界面的整体不透明度（0.2~1.0）。调低后可透过窗口看到测试字幕/弹幕。",
            IsAdvanced = true
                    }),
                null, null, delegate { SettingsUI.SettingsWindow.ApplyOpacity(); });

            // —— 最后：把 entries 赋回，并统一设置 Order —— //
            // （折叠组 / 预设快照 / 变更刷新 已在上面的 Reg 统一注册中顺带完成）
            ConfigEntries = entries ?? new List<ConfigEntryBase>();
            EnsureConfigurationManagerAttributes(ConfigEntries);
            EnsureConfigurationManagerAttributes(DanmakuFontSize);
            RecalcOrder();
            ApplySlimConfigurationManagerVisibility();

            // 纯动作条目登记：按钮（值仅占位）与已停用的折叠开关不参与“重置本板块/全部重置”。
            // 注意：TextPresetName/三个字体资源包名是值条目（默认值有效），仍可重置。
            s_NonResettable.Clear();
            if (SettingsWindowButton != null) s_NonResettable.Add(SettingsWindowButton);
            if (PhraseFilterPanelButton != null) s_NonResettable.Add(PhraseFilterPanelButton);
            if (_TestSubtitleButton != null) s_NonResettable.Add(_TestSubtitleButton);
            if (TestDanmakuButton != null) s_NonResettable.Add(TestDanmakuButton);
            if (ShowSubtitleOptions != null) s_NonResettable.Add(ShowSubtitleOptions);
            if (ShowDanmakuOptions != null) s_NonResettable.Add(ShowDanmakuOptions);
            if (ShowWorld3DOptions != null) s_NonResettable.Add(ShowWorld3DOptions);

            // 加载界面语言文件（ locales/<语言>/UI.jsonc ）；缺失/损坏时所有显示回落到上面的中文原文
            I18n.Init(UiLanguage.Value);
        }

        /// <summary>纯动作条目（按钮/折叠开关）没有有意义的值，重置时跳过；其余条目均可重置。</summary>
        public static bool IsResettable(ConfigEntryBase entry)
        {
            return entry != null && !s_NonResettable.Contains(entry);
        }

        /// <summary>把单个条目恢复为默认值：BoxedValue 赋值即触发 SettingChanged，运行期刷新沿用既有链路。</summary>
        public static void ResetEntryToDefault(ConfigEntryBase entry)
        {
            if (entry == null) return;
            entry.BoxedValue = entry.DefaultValue;
        }
    }

}
