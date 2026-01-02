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
        public static ConfigEntry<bool> ShowSubtitleOptions;
        public static ConfigEntry<bool> ShowDanmakuOptions;
        public static ConfigEntry<bool> ShowWorld3DOptions;


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

        public static void Init(ConfigFile config)
        {
            Config = config;

            // 扫描 presets 目录，构建可选项

            var presetsDir = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "presets");
            var presetFiles = Directory.Exists(presetsDir) ? Directory.GetFiles(presetsDir, "*.jsonc", SearchOption.TopDirectoryOnly) : new string[0];
            var presetNames = new List<string>();
            for (int i = 0; i < presetFiles.Length; i++)
            {
                var name = Path.GetFileNameWithoutExtension(presetFiles[i]);
                if (!string.IsNullOrEmpty(name) && !presetNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    presetNames.Add(name);
            }
            if (!presetNames.Any(n => string.Equals(n, "default", StringComparison.OrdinalIgnoreCase)))
                presetNames.Insert(0, "default");

            var entries = new List<ConfigEntryBase>();

            // —— 1) General —— //
            // 让 EnableSubtitle 排在 TextPresetName 上方：先添加它（后续 RecalcOrder 会按添加顺序设置 Order）

            entries.Add(TextPresetName = Config.Bind(
                GeneralSection,
                "文本样式预设",
                "default",
                new ConfigDescription(
                    "从 presets 文件夹读取所有 .jsonc预设文件。点击“应用”后，会将预设中所有包含选项一次性导入本配置。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        DispName = "\n文本样式预设",
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

            entries.Add(EnableSubtitle = Config.Bind(
               GeneralSection,
               "字幕 启用",
               true,
               new ConfigDescription(
                   "是否启用字幕功能。",
                   null,
                   new ConfigurationManagerAttributes
                   {
                   })));

            entries.Add(EnableDanmaku = Config.Bind(
                GeneralSection,
                "弹幕 启动",
                true,
                new ConfigDescription(
                    "启用弹幕显示。",
                    null,
                    new ConfigurationManagerAttributes
                    {

                    })));

            entries.Add(EnableWorld3D = Config.Bind(
                GeneralSection,
                "3D气泡 启用",
                true,
                new ConfigDescription(
                    "启用 3D 气泡显示。",
                    null,
                    new ConfigurationManagerAttributes { })));

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

            entries.Add(SubtitleShowRoleTag = Config.Bind(
               SubtitleGeneralSection,
               "字幕 显示说话者",
               true,
               new ConfigDescription(
                    "是否在字幕中显示说话者（roletag）。关闭后仅显示台词文本（及距离）\n开启：显示“你/Scav/Tagilla：”。关闭：只显示台词（可选加距离）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    })));

            entries.Add(SubtitleShowPmcName = Config.Bind(SubtitleGeneralSection, "字幕 显示PMC名字", false, new ConfigDescription("是否显示PMC游戏内的ID", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubtitleShowScavName = Config.Bind(SubtitleGeneralSection, "字幕 显示Scav名字", false, new ConfigDescription("是否显示Scav游戏内的ID\n不推荐，因为Scav游戏名字太长可能会导致台词观感很差。", null, new ConfigurationManagerAttributes { })));

            entries.Add(SubtitlePlayerSelfPronoun = Config.Bind(
                SubtitleGeneralSection,
                "字幕 玩家说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当玩家自己说话时字幕显示风格：\n- 略称：始终显示“你”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(SubtitleTeammateSelfPronoun = Config.Bind(
                SubtitleGeneralSection,
                "字幕 队友说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当友军（队友）说话时字幕显示风格：\n- 略称：始终显示“队友”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(SubtitleMaxDistanceMeters = Config.Bind(
               SubtitleGeneralSection,
               "字幕 最大语音接收距离（米）",
               30f,
               new ConfigDescription(
                   "当语音（其他玩家/AI）来源距离玩家超过该距离时，不显示字幕。\n10~150 米，默认 30 米",
                   new AcceptableValueRange<float>(10f, 150f),
                   new ConfigurationManagerAttributes
                   {
                   })));

            entries.Add(SubtitleShowDistance = Config.Bind(
                SubtitleGeneralSection,
                "字幕 显示距离",
                true,
                new ConfigDescription(
                    "是否显示距离。开启后，会在语音后面添加一个类似“ ·10m”的字样",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    })));

            entries.Add(SubtitleDisplayDelaySec = Config.Bind(
                SubtitleGeneralSection,
                "字幕 台词显示延迟（秒）",
                0.5f,
                new ConfigDescription(
                    "字幕显示后，额外延迟消失的秒数，避免短语音瞬间出现又消失。",
                    new AcceptableValueRange<float>(0f, 3f),
                    new ConfigurationManagerAttributes { })));

            entries.Add(EnableMapBroadcastSubtitle = Config.Bind(
    SubtitleGeneralSection,
    "字幕 启动实验室广播",
    true,
    new ConfigDescription(
        "启用后会把 实验室公共播报 显示为字幕（即实验室拉闸/开关的全图播报）。",
        null,
        new ConfigurationManagerAttributes
        {
        })));

            entries.Add(SubtitleZombieEnabled = Config.Bind(
                SubtitleGeneralSection,
                "字幕 丧尸显示台词（除 丧尸Tagilla）",
                true,
                new ConfigDescription(
                    "丧尸台词是否显示（不影响 丧尸Tagilla）。开启则正常显示丧尸台词，关闭则不显示所有普通丧尸的台词",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    })));

            entries.Add(SubtitleZombieCooldownSec = Config.Bind(
                SubtitleGeneralSection,
                "字幕 丧尸台词间隔（秒）",
                10,
                new ConfigDescription(
                    "丧尸类台词的最小间隔（秒）。第一条丧尸台词出现后，在此设置时间区间内其它丧尸台词会被忽略。\n0 表示不限制。推荐5-10以上，否则会导致大量的台词刷屏。",
                    new AcceptableValueRange<int>(0, 60),
                    new ConfigurationManagerAttributes
                    {
                    })));
            // —— 2.1) Subtitle-Advanced —— //
            entries.Add(SubtitleFontBundleName = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体资源包", "",
                new ConfigDescription(
                    "从 BepInEx\\plugins\\FontReplace\\Font 选择字体资源包（不覆盖则留空）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawFontBundlePicker,
                        HideDefaultButton = true
                    })));
            entries.Add(SubtitleFontFamilyCsv = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体类型",
                "SimHei;Microsoft YaHei;Microsoft YaHei UI;DengXian;Noto Sans CJK SC",
                new ConfigDescription("字体候选，分号;隔开(需要大写分号)\n支持 game:FontName 走游戏内置字体。\n游戏将优先从左往右依次检测支持的字体类型，最后退回Arial.ttf",
                    null, new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    })));

            entries.Add(SubtitleFontSize = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体尺寸（px）", 26,
                new ConfigDescription("字体尺寸大小（px）", new AcceptableValueRange<int>(12, 64),
                    new ConfigurationManagerAttributes { })));

            entries.Add(SubtitleFontBold = Config.Bind(SubtitleAdvancedSection, "字幕 字体加粗", false,
                new ConfigDescription("字幕字体加粗。", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubtitleFontItalic = Config.Bind(SubtitleAdvancedSection, "字幕 字体斜体", false,
                new ConfigDescription("字幕字体斜体。", null, new ConfigurationManagerAttributes { })));

            // 对齐 & 换行
            entries.Add(SubtitleAlignment = Config.Bind(
                SubtitleAdvancedSection, "字幕 文本对齐", TextAnchorOption.MiddleCenter,
                new ConfigDescription("文本对齐（TextAnchor）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleWrap = Config.Bind(
                SubtitleAdvancedSection, "字幕 自动换行", true,
                new ConfigDescription("是否开启自动换行，若开启则按照下方换行限制进行，禁用则不换行", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(SubtitleWrapLength = Config.Bind(
                SubtitleAdvancedSection, "字幕 自动换行长度阈值", 0,
                new ConfigDescription("超过 N 个可见字符后强制换行（0 关闭）。", null,
                    new ConfigurationManagerAttributes { })));

            // 描边
            entries.Add(SubtitleOutlineEnabled = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体描边", true,
                new ConfigDescription("启用描边。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(SubtitleOutlineColor = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体描边颜色", new Color(0f, 0f, 0f, 0.95f),
                new ConfigDescription("描边颜色。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(SubtitleOutlineDistX = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体描边位移（X轴）", 1.5f,
                new ConfigDescription("描边水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleOutlineDistY = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体描边位移（Y轴）", 1.5f,
                new ConfigDescription("描边垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            // 阴影  
            entries.Add(SubtitleShadowEnabled = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体阴影", true,
                new ConfigDescription("启用阴影。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(SubtitleShadowColor = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体阴影颜色", new Color(0f, 0f, 0f, 0.6f),
                new ConfigDescription("阴影颜色。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(SubtitleShadowDistX = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体阴影位移（X轴）", 2f,
                new ConfigDescription("阴影水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleShadowDistY = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体阴影位移（Y轴）", -2f,
                new ConfigDescription("阴影垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleShadowUseGraphicAlpha = Config.Bind(
                SubtitleAdvancedSection, "字幕 字体阴影叠乘文本透明度", true,
                new ConfigDescription("是否叠乘文本透明度。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            // 布局
            entries.Add(SubtitleLayoutAnchor = Config.Bind(
                SubtitleAdvancedSection, "字幕 布局锚点", TextAnchorOption.LowerCenter,
                new ConfigDescription("锚点（TextAnchor）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleLayoutOffsetX = Config.Bind(
                SubtitleAdvancedSection, "字幕 布局锚点位移（X轴）", 0f,
                new ConfigDescription("相对锚点水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleLayoutOffsetY = Config.Bind(
                SubtitleAdvancedSection, "字幕 布局锚点位移（Y轴）", 0f,
                new ConfigDescription("相对锚点垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleLayoutSafeArea = Config.Bind(
                SubtitleAdvancedSection, "字幕 布局安全区", true,
                new ConfigDescription("是否考虑安全区。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleLayoutMaxWidthPercent = Config.Bind(
                SubtitleAdvancedSection, "字幕 布局最大宽度占比", 0.90f,
                new ConfigDescription("文本测量最大宽度占屏比例（0~1）。",
                    new AcceptableValueRange<float>(0.5f, 1.0f),
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleLayoutLineSpacing = Config.Bind(
                SubtitleAdvancedSection, "字幕 布局行距", 4.0f,
                new ConfigDescription("额外行距（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleLayoutOverrideAlign = Config.Bind(
                SubtitleAdvancedSection, "字幕 布局覆盖文本对齐", TextAnchorOption.None,
                new ConfigDescription("可选：强制 Text 对齐（None 表示不改）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleLayoutStackOffsetPercent = Config.Bind(
                SubtitleAdvancedSection, "字幕 布局底部堆叠上移", 0.12f,
                new ConfigDescription("字幕堆叠面板距底部相对高度（0~0.5）。",
                    new AcceptableValueRange<float>(0f, 0.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            // 背景
            entries.Add(SubtitleBgEnabled = Config.Bind(
                SubtitleAdvancedSection, "字幕 文本背景", true,
                new ConfigDescription("开启条形气泡背景。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(SubtitleBgFit = Config.Bind(
                SubtitleAdvancedSection, "字幕 文本背景贴合", "text",
                new ConfigDescription("贴合策略：text（贴文字）/ fullRow（固定宽度）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleBgColor = Config.Bind(
                SubtitleAdvancedSection, "字幕 文本背景颜色", new Color(0f, 0f, 0f, 0.35f),
                new ConfigDescription("背景色。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(SubtitleBgPaddingX = Config.Bind(
                SubtitleAdvancedSection, "字幕 文本背景内边距 X", 12f,
                new ConfigDescription("背景内边距 X（像素）", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleBgPaddingY = Config.Bind(
                SubtitleAdvancedSection, "字幕 文本背景内边距 Y", 6f,
                new ConfigDescription("背景内边距 Y（像素）", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleBgMarginY = Config.Bind(
                SubtitleAdvancedSection, "字幕 文本背景外边距 Y", 6f,
                new ConfigDescription("背景外边距 Y（像素）", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleBgSprite = Config.Bind(
                SubtitleAdvancedSection, "字幕 文本背景九宫格名", "",
                new ConfigDescription("九宫格资源名（可选）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            // 背景阴影
            entries.Add(SubtitleBgShadowEnabled = Config.Bind(
                SubtitleAdvancedSection, "字幕 背景阴影", false,
                new ConfigDescription("背景投影开关。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleBgShadowColor = Config.Bind(
                SubtitleAdvancedSection, "字幕 背景阴影颜色", new Color(0f, 0f, 0f, 0.45f),
                new ConfigDescription("背景投影颜色。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleBgShadowDistX = Config.Bind(
                SubtitleAdvancedSection, "字幕 背景阴影水平偏移 X", 2f,
                new ConfigDescription("背景阴影：水平偏移（px）", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleBgShadowDistY = Config.Bind(
                SubtitleAdvancedSection, "字幕 背景阴影水平偏移 X", -2f,
                new ConfigDescription("背景阴影：水平偏移（px）", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(SubtitleBgShadowUseGraphicAlpha = Config.Bind(
                SubtitleAdvancedSection, "字幕 背景阴影叠乘文字透明度", true,
                new ConfigDescription("背景阴影是否叠乘文字透明度", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(DanmakuLanes = Config.Bind(
                DanmakuAdvancedSection,
                "弹幕 车道数量",
                8,
                new ConfigDescription(
                    "弹幕车道数量。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    })));

            entries.Add(DanmakuSpeed = Config.Bind(
                DanmakuAdvancedSection,
                "弹幕 速度（px/s）",
                180f,
                new ConfigDescription(
                    "弹幕速度（像素/秒）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    })));

            entries.Add(DanmakuMinGapPx = Config.Bind(
                DanmakuAdvancedSection,
                "弹幕 同车道最小间隔",
                40,
                new ConfigDescription(
                    "同车道最小间隔像素。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    })));

            entries.Add(DanmakuSpawnDelaySec = Config.Bind(
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
                    })));

            entries.Add(DanmakuTopOffsetPercent = Config.Bind(
                DanmakuAdvancedSection,
                "弹幕 顶部起始位置（相对）",
                0.10f,
                new ConfigDescription(
                    "弹幕距屏幕顶部的相对起始高度（0~0.5，默认 0.10）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    })));

            entries.Add(DanmakuAreaMaxPercent = Config.Bind(
                DanmakuAdvancedSection,
                "弹幕 最大垂直占比",
                0.35f,
                new ConfigDescription(
                    "弹幕允许占用的最大垂直高度（0~1，默认 0.35）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        IsAdvanced = true
                    })));


            entries.Add(DanmakuShowRoleTag = Config.Bind(
                DanmakuGeneralSection,
                "弹幕 显示说话者",
                true,
                new ConfigDescription(
                    "是否在弹幕中显示说话者（例如“你/Scav/Tagilla：”）。关闭后仅显示台词文本。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    })));

            entries.Add(DanmakuShowPmcName = Config.Bind(DanmakuGeneralSection, "弹幕 显示PMC名字", false, new ConfigDescription("是否显示PMC游戏内的ID", null, new ConfigurationManagerAttributes { })));
            entries.Add(DanmakuShowScavName = Config.Bind(DanmakuGeneralSection, "弹幕 显示Scav名字", false, new ConfigDescription("是否显示Scav游戏内的ID\n不推荐，因为Scav游戏名字太长可能会导致台词观感很差。", null, new ConfigurationManagerAttributes { })));

            entries.Add(DanmakuPlayerSelfPronoun = Config.Bind(
                DanmakuGeneralSection,
                "弹幕 玩家说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当玩家自己说话时弹幕显示风格：\n- 略称：始终显示“你”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(DanmakuTeammateSelfPronoun = Config.Bind(
                DanmakuGeneralSection,
                "弹幕 队友说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当友军（队友）说话时弹幕显示风格：\n- 略称：始终显示“队友”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(DanmakuMaxDistanceMeters = Config.Bind(
                DanmakuGeneralSection,
                "弹幕 最大语音接收距离（米）",
                100f,
                new ConfigDescription(
                    "当语音来源距离玩家超过该距离时，不显示弹幕\n10~150 米，默认 100 米。",
                    new AcceptableValueRange<float>(10f, 150f),
                    new ConfigurationManagerAttributes
                    {
                    })));

            entries.Add(DanmakuShowDistance = Config.Bind(
                DanmakuGeneralSection,
                "弹幕 显示距离",
                true,
                new ConfigDescription(
                    "是否显示距离。开启后，会在语音后面添加一个类似“ ·10m”的字样",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    })));

            entries.Add(EnableMapBroadcastDanmaku = Config.Bind(
    DanmakuGeneralSection,
    "弹幕 启动实验室广播",
    true,
    new ConfigDescription(
        "启用后会把 实验室公共播报 显示为字幕（即实验室拉闸/开关的全图播报）。",
        null,
        new ConfigurationManagerAttributes
        {

        })));

            entries.Add(DanmakuZombieEnabled = Config.Bind(
                DanmakuGeneralSection,
                "弹幕 丧尸显示台词（除 丧尸Tagilla）",
                true,
                new ConfigDescription(
                    "丧尸台词是否显示（不影响 丧尸Tagilla）。开启则正常显示丧尸台词，关闭则不显示所有普通丧尸的台词",
                    null,
                    new ConfigurationManagerAttributes
                    {
                    })));

            entries.Add(DanmakuZombieCooldownSec = Config.Bind(
                DanmakuGeneralSection,
                "弹幕 丧尸台词间隔（秒）",
                5,
                new ConfigDescription(
                    "丧尸类台词的最小间隔（秒）。第一条丧尸台词出现后，在此设置时间区间内其它丧尸台词会被忽略。\n0 表示不限制。推荐5-10以上，否则会导致大量的台词刷屏。",
                    new AcceptableValueRange<int>(0, 60),
                    new ConfigurationManagerAttributes
                    {
                    })));

            // —— Danmaku 字体 —— 
            entries.Add(DanmakuFontBundleName = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体资源包", "",
                new ConfigDescription(
                    "从 BepInEx\\plugins\\FontReplace\\Font 选择字体资源包（不覆盖则留空）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawFontBundlePicker,
                        HideDefaultButton = true
                    })));
            entries.Add(DanmakuFontFamilyCsv = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体类型",
                "SimHei;Microsoft YaHei;Microsoft YaHei UI;DengXian;Noto Sans CJK SC",
                new ConfigDescription("字体候选，逗号或分号分隔；支持 game:FontName 走游戏内置字体。\n游戏将优先从左往右依次检测支持的字体类型，最后退回Arial.ttf",
                    null, new ConfigurationManagerAttributes { IsAdvanced = true })));
            
            entries.Add(DanmakuFontSize = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体尺寸",
                24,
                new ConfigDescription("弹幕字体尺寸大小（px）。",new AcceptableValueRange<int>(12, 64),
                 new ConfigurationManagerAttributes { })));

            entries.Add(DanmakuFontBold = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体加粗", false,
                new ConfigDescription("弹幕字体加粗。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(DanmakuFontItalic = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体斜体", false,
                new ConfigDescription("弹幕字体斜体。", null,
                    new ConfigurationManagerAttributes { })));

            // —— Danmaku 描边 —— 
            entries.Add(DanmakuOutlineEnabled = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体描边", true,
                new ConfigDescription("启用弹幕描边。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(DanmakuOutlineColor = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体描边颜色", new Color(0f, 0f, 0f, 0.88f), // #000000E0 ≈ A=0.88
                new ConfigDescription("弹幕描边颜色。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(DanmakuOutlineDistX = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体描边水平位移（X轴）", 1.2f,
                new ConfigDescription("描边水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(DanmakuOutlineDistY = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体描边水平位移（Y轴）", 1.2f,
                new ConfigDescription("描边垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            // —— Danmaku 阴影 —— 
            entries.Add(DanmakuShadowEnabled = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体阴影", true,
                new ConfigDescription("启用弹幕阴影。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(DanmakuShadowColor = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体阴影颜色", new Color(0f, 0f, 0f, 0.55f),
                new ConfigDescription("弹幕阴影颜色。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(DanmakuShadowDistX = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体阴影水平位移（X轴）", 2f,
                new ConfigDescription("阴影水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(DanmakuShadowDistY = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体阴影水平位移（Y轴）", -2f,
                new ConfigDescription("阴影垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(DanmakuShadowUseGraphicAlpha = Config.Bind(
                DanmakuAdvancedSection, "弹幕 字体阴影叠乘文本透明度", true,
                new ConfigDescription("是否叠乘文本透明度。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            // —— 4) World3D —— //
            entries.Add(World3DShowRoleTag = Config.Bind(
                World3DGeneralSection,
                "3D气泡 显示说话者",
                true,
                new ConfigDescription(
                    "是否在气泡中显示说话者（roletag）。关闭后仅显示台词文本（可选加距离）。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DShowPmcName = Config.Bind(
                World3DGeneralSection,
                "3D气泡 显示PMC名字",
                false,
                new ConfigDescription("是否显示PMC游戏内的ID", null, new ConfigurationManagerAttributes { })));

            entries.Add(World3DShowScavName = Config.Bind(
                World3DGeneralSection,
                "3D气泡 显示Scav名字",
                false,
                new ConfigDescription("是否显示Scav游戏内的ID\n不推荐，因为Scav游戏名字太长可能会导致台词观感很差。", null, new ConfigurationManagerAttributes { })));

            entries.Add(World3DPlayerSelfPronoun = Config.Bind(
                World3DGeneralSection,
                "3D气泡 玩家说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当玩家自己说话时3D气泡显示风格：\n- 略称：始终显示“你”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DTeammateSelfPronoun = Config.Bind(
                World3DGeneralSection,
                "3D气泡 队友说话代称",
                SelfPronounOption.玩家名,
                new ConfigDescription(
                    "当友军（队友）说话时3D气泡显示风格：\n- 略称：始终显示“队友”。\n- 玩家名：显示该玩家昵称。\n- 声线名：显示声线标签（如 Michael）。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DMaxDistanceMeters = Config.Bind(
                World3DGeneralSection,
                "3D气泡 最大语音接收距离（米）",
                30f,
                new ConfigDescription(
                    "当语音来源距离玩家超过该距离时，不显示气泡。\n10~150 米，默认 30 米",
                    new AcceptableValueRange<float>(10f, 150f),
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DShowDistance = Config.Bind(
                World3DGeneralSection,
                "3D气泡 显示距离",
                true,
                new ConfigDescription(
                    "是否显示距离。开启后，会在语音后面添加一个类似“ ·10m”的字样",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DDisplayDelaySec = Config.Bind(
                World3DGeneralSection,
                "3D气泡 台词显示延迟（秒）",
                0.5f,
                new ConfigDescription(
                    "3D气泡显示后，额外延迟消失的秒数，避免短语音瞬间出现又消失。",
                    new AcceptableValueRange<float>(0f, 3f),
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DVerticalOffsetY = Config.Bind(
                World3DGeneralSection,
                "3D气泡 垂直偏移（米）",
                0.2f,
                new ConfigDescription(
                    "气泡整体向上/向下的偏移量（米）。正值向上，负值向下。",
                    new AcceptableValueRange<float>(-1.0f, 1.0f),
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DFacePlayer = Config.Bind(
                World3DGeneralSection,
                "3D气泡 朝向玩家",
                true,
                new ConfigDescription(
                    "是否让气泡始终朝向玩家视角。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DBGEnabled = Config.Bind(
                World3DGeneralSection,
                "3D气泡 背景",
                true,
                new ConfigDescription(
                    "是否显示气泡背景。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DBGColor = Config.Bind(
                World3DGeneralSection,
                "3D气泡 背景颜色",
                new Color(0f, 0f, 0f, 0.65f),
                new ConfigDescription(
                    "气泡背景颜色（含透明度）。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DShowSelf = Config.Bind(
                World3DGeneralSection,
                "3D气泡 显示自己",
                true,
                new ConfigDescription(
                    "是否显示玩家自己说话的气泡。",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DZombieEnabled = Config.Bind(
                World3DGeneralSection,
                "3D气泡 丧尸显示台词（除 丧尸Tagilla）",
                true,
                new ConfigDescription(
                    "丧尸台词是否显示（不影响 丧尸Tagilla）。开启则正常显示丧尸台词，关闭则不显示所有普通丧尸的台词",
                    null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DZombieCooldownSec = Config.Bind(
                World3DGeneralSection,
                "3D气泡 丧尸台词间隔（秒）",
                10,
                new ConfigDescription(
                    "丧尸类台词的最小间隔（秒）。第一条丧尸台词出现后，在此设置时间区间内其它丧尸台词会被忽略。\n0 表示不限制。推荐5-10以上，否则会导致大量的台词刷屏。",
                    new AcceptableValueRange<int>(0, 60),
                    new ConfigurationManagerAttributes { })));

            // —— World3D 字体 —— 
            entries.Add(World3DFontBundleName = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体资源包", "",
                new ConfigDescription(
                    "从 BepInEx\\plugins\\FontReplace\\Font 选择字体资源包（不覆盖则留空）。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawFontBundlePicker,
                        HideDefaultButton = true
                    })));
            entries.Add(World3DFontFamilyCsv = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体类型",
                "SimHei;Microsoft YaHei;Microsoft YaHei UI;DengXian;Noto Sans CJK SC",
                new ConfigDescription("字体候选，分号;隔开(需要大写分号)\n支持 game:FontName 走游戏内置字体。",
                    null, new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DFontSize = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体尺寸（px）", 26,
                new ConfigDescription("字体尺寸大小（px）", new AcceptableValueRange<int>(12, 64),
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DFontBold = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体加粗", false,
                new ConfigDescription("3D气泡字体加粗。", null, new ConfigurationManagerAttributes { })));

            entries.Add(World3DFontItalic = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体斜体", false,
                new ConfigDescription("3D气泡字体斜体。", null, new ConfigurationManagerAttributes { })));

            entries.Add(World3DAlignment = Config.Bind(
                World3DAdvancedSection, "3D气泡 文本对齐", TextAnchorOption.MiddleCenter,
                new ConfigDescription("文本对齐（TextAnchor）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DWrap = Config.Bind(
                World3DAdvancedSection, "3D气泡 自动换行", true,
                new ConfigDescription("是否开启自动换行，若开启则按照下方换行限制进行，禁用则不换行", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DWrapLength = Config.Bind(
                World3DAdvancedSection, "3D气泡 自动换行长度阈值", 0,
                new ConfigDescription("超过 N 个可见字符后强制换行（0 关闭）。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DWorldScale = Config.Bind(
                World3DAdvancedSection, "3D气泡 世界缩放", 0.01f,
                new ConfigDescription("世界空间缩放系数；值越大越清晰也越大。默认 0.01。", new AcceptableValueRange<float>(0.002f, 0.05f),
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DDynamicPixelsPerUnit = Config.Bind(
                World3DAdvancedSection, "3D气泡 动态像素密度", 20f,
                new ConfigDescription("CanvasScaler.dynamicPixelsPerUnit；越大越清晰但更耗性能。", new AcceptableValueRange<float>(5f, 120f),
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DFaceUpdateIntervalSec = Config.Bind(
                World3DAdvancedSection, "3D气泡 朝向更新间隔（秒）", 0f,
                new ConfigDescription("0 表示每帧朝向玩家；>0 则按间隔更新，减少抖动/模糊。", new AcceptableValueRange<float>(0f, 0.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DStackMaxLines = Config.Bind(
                World3DAdvancedSection, "3D气泡 叠加最大行数", 3,
                new ConfigDescription("同一角色连续说话时可叠加的最大行数。", new AcceptableValueRange<int>(1, 6),
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DStackOffsetY = Config.Bind(
                World3DAdvancedSection, "3D气泡 叠加上移间距", 0.18f,
                new ConfigDescription("多行叠加时每行向上偏移的高度。", new AcceptableValueRange<float>(0.05f, 0.6f),
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DFadeInSec = Config.Bind(
                World3DAdvancedSection, "3D气泡 淡入时长（秒）", 0.15f,
                new ConfigDescription("3D气泡淡入耗时。", new AcceptableValueRange<float>(0f, 1.0f),
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DFadeOutSec = Config.Bind(
                World3DAdvancedSection, "3D气泡 淡出时长（秒）", 0.25f,
                new ConfigDescription("3D气泡淡出耗时。", new AcceptableValueRange<float>(0f, 1.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            // —— World3D 描边 —— 
            entries.Add(World3DOutlineEnabled = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体描边", true,
                new ConfigDescription("启用描边。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DOutlineColor = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体描边颜色", new Color(0f, 0f, 0f, 0.95f),
                new ConfigDescription("描边颜色。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DOutlineDistX = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体描边位移（X轴）", 1.5f,
                new ConfigDescription("描边水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DOutlineDistY = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体描边位移（Y轴）", 1.5f,
                new ConfigDescription("描边垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            // —— World3D 阴影 —— 
            entries.Add(World3DShadowEnabled = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体阴影", true,
                new ConfigDescription("启用阴影。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DShadowColor = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体阴影颜色", new Color(0f, 0f, 0f, 0.6f),
                new ConfigDescription("阴影颜色。", null,
                    new ConfigurationManagerAttributes { })));

            entries.Add(World3DShadowDistX = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体阴影位移（X轴）", 2f,
                new ConfigDescription("阴影水平偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DShadowDistY = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体阴影位移（Y轴）", -2f,
                new ConfigDescription("阴影垂直偏移（px）。", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

            entries.Add(World3DShadowUseGraphicAlpha = Config.Bind(
                World3DAdvancedSection, "3D气泡 字体阴影叠乘文本透明度", true,
                new ConfigDescription("阴影是否叠乘文字透明度", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true })));

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
            entries.Add(SubRole_Player = Config.Bind(SubRoleColorSection, "玩家 角色颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_Teammate = Config.Bind(SubRoleColorSection, "队友 角色颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_PmcBear = Config.Bind(SubRoleColorSection, "Bear 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_PmcUsec = Config.Bind(SubRoleColorSection, "Usec 角色颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_Scav = Config.Bind(SubRoleColorSection, "Scav 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_Raider = Config.Bind(SubRoleColorSection, "Raider 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_Rogue = Config.Bind(SubRoleColorSection, "Rogue 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_Cultist = Config.Bind(SubRoleColorSection, "邪教徒 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_BossFollower = Config.Bind(SubRoleColorSection, "Boss小弟 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_Zombie = Config.Bind(SubRoleColorSection, "丧尸 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_Goons = Config.Bind(SubRoleColorSection, "三狗 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_Bosses = Config.Bind(SubRoleColorSection, "Boss 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubRole_LabAnnouncer = Config.Bind(SubRoleColorSection, "实验室广播 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));

            entries.Add(SubText_Player = Config.Bind(SubRoleTextColorSection, "玩家 文本颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_Teammate = Config.Bind(SubRoleTextColorSection, "队友 文本颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_PmcBear = Config.Bind(SubRoleTextColorSection, "Bear 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_PmcUsec = Config.Bind(SubRoleTextColorSection, "Usec 文本颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_Scav = Config.Bind(SubRoleTextColorSection, "Scav 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_Raider = Config.Bind(SubRoleTextColorSection, "Raider 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_Rogue = Config.Bind(SubRoleTextColorSection, "Rogue 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_Cultist = Config.Bind(SubRoleTextColorSection, "邪教徒 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_BossFollower = Config.Bind(SubRoleTextColorSection, "Boss小弟 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_Zombie = Config.Bind(SubRoleTextColorSection, "丧尸 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_Goons = Config.Bind(SubRoleTextColorSection, "三狗 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_Bosses = Config.Bind(SubRoleTextColorSection, "Boss 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(SubText_LabAnnouncer = Config.Bind(SubRoleTextColorSection, "实验室广播 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("字幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));

            entries.Add(DmRole_Player = Config.Bind(DmRoleColorSection, "玩家 角色颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_Teammate = Config.Bind(DmRoleColorSection, "队友 角色颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_PmcBear = Config.Bind(DmRoleColorSection, "Bear 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_PmcUsec = Config.Bind(DmRoleColorSection, "Usec 角色颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_Scav = Config.Bind(DmRoleColorSection, "Scav 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_Raider = Config.Bind(DmRoleColorSection, "Raider 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_Rogue = Config.Bind(DmRoleColorSection, "Rogue 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_Cultist = Config.Bind(DmRoleColorSection, "邪教徒 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_BossFollower = Config.Bind(DmRoleColorSection, "Boss小弟 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_Zombie = Config.Bind(DmRoleColorSection, "丧尸 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_Goons = Config.Bind(DmRoleColorSection, "三狗 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_Bosses = Config.Bind(DmRoleColorSection, "Boss 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmRole_LabAnnouncer = Config.Bind(DmRoleColorSection, "实验室广播 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的颜色", null, new ConfigurationManagerAttributes { })));

            entries.Add(DmText_Player = Config.Bind(DmRoleTextColorSection, "玩家 文本颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_Teammate = Config.Bind(DmRoleTextColorSection, "队友 文本颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_PmcBear = Config.Bind(DmRoleTextColorSection, "Bear 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_PmcUsec = Config.Bind(DmRoleTextColorSection, "Usec 文本颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_Scav = Config.Bind(DmRoleTextColorSection, "Scav 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_Raider = Config.Bind(DmRoleTextColorSection, "Raider 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_Rogue = Config.Bind(DmRoleTextColorSection, "Rogue 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_Cultist = Config.Bind(DmRoleTextColorSection, "邪教徒 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_BossFollower = Config.Bind(DmRoleTextColorSection, "Boss小弟 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_Zombie = Config.Bind(DmRoleTextColorSection, "丧尸 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_Goons = Config.Bind(DmRoleTextColorSection, "三狗 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_Bosses = Config.Bind(DmRoleTextColorSection, "Boss 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(DmText_LabAnnouncer = Config.Bind(DmRoleTextColorSection, "实验室广播 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("弹幕-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));

            entries.Add(W3dRole_Player = Config.Bind(W3dRoleColorSection, "玩家 角色颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_Teammate = Config.Bind(W3dRoleColorSection, "队友 角色颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_PmcBear = Config.Bind(W3dRoleColorSection, "Bear 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_PmcUsec = Config.Bind(W3dRoleColorSection, "Usec 角色颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_Scav = Config.Bind(W3dRoleColorSection, "Scav 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_Raider = Config.Bind(W3dRoleColorSection, "Raider 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_Rogue = Config.Bind(W3dRoleColorSection, "Rogue 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_Cultist = Config.Bind(W3dRoleColorSection, "邪教徒 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_BossFollower = Config.Bind(W3dRoleColorSection, "Boss小弟 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_Zombie = Config.Bind(W3dRoleColorSection, "丧尸 角色颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_Goons = Config.Bind(W3dRoleColorSection, "三狗 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_Bosses = Config.Bind(W3dRoleColorSection, "Boss 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dRole_LabAnnouncer = Config.Bind(W3dRoleColorSection, "实验室广播 角色颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的颜色", null, new ConfigurationManagerAttributes { })));

            entries.Add(W3dText_Player = Config.Bind(W3dRoleTextColorSection, "玩家 文本颜色", new Color(1f, 1f, 1f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_Teammate = Config.Bind(W3dRoleTextColorSection, "队友 文本颜色", new Color(0.15f, 0.35f, 0.95f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_PmcBear = Config.Bind(W3dRoleTextColorSection, "Bear 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_PmcUsec = Config.Bind(W3dRoleTextColorSection, "Usec 文本颜色", new Color(1f, 1f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_Scav = Config.Bind(W3dRoleTextColorSection, "Scav 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_Raider = Config.Bind(W3dRoleTextColorSection, "Raider 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_Rogue = Config.Bind(W3dRoleTextColorSection, "Rogue 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_Cultist = Config.Bind(W3dRoleTextColorSection, "邪教徒 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_BossFollower = Config.Bind(W3dRoleTextColorSection, "Boss小弟 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_Zombie = Config.Bind(W3dRoleTextColorSection, "丧尸 文本颜色", new Color(1f, 0.45f, 0.007f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_Goons = Config.Bind(W3dRoleTextColorSection, "三狗 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_Bosses = Config.Bind(W3dRoleTextColorSection, "Boss 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));
            entries.Add(W3dText_LabAnnouncer = Config.Bind(W3dRoleTextColorSection, "实验室广播 文本颜色", new Color(1f, 0f, 0f, 1f), new ConfigDescription("3D气泡-说话角色的 台词文本 颜色", null, new ConfigurationManagerAttributes { })));


            // —— 99) Debug —— //
            entries.Add(EnableDebugTools = Config.Bind(
                DebugSection,
                "Enable Debug Tools",
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
                "Debug Panel Hotkey",
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

            entries.Add(DanmakuDebugVerbose = Config.Bind(
                DebugSection,
                "Danmaku Debug Verbose",
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
                "Map Broadcast Debug",
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

            entries.Add(VoiceDedupWindowSec = Config.Bind(
    DebugSection,
    "Voice De-dup Window (sec)",
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

            // —— 运行期刷新（布局/弹幕） —— //
            void HookChanged<T>(ConfigEntry<T> entry, Action onChanged)
            {
                if (entry == null || onChanged == null) return;
                entry.SettingChanged += (s, e) => { try { onChanged(); } catch { } };
            }

            Action refreshSubtitleLayout = TryApplySubtitleLayoutRuntime;
            HookChanged(SubtitleLayoutAnchor, refreshSubtitleLayout);
            HookChanged(SubtitleLayoutOffsetX, refreshSubtitleLayout);
            HookChanged(SubtitleLayoutOffsetY, refreshSubtitleLayout);
            HookChanged(SubtitleLayoutSafeArea, refreshSubtitleLayout);
            HookChanged(SubtitleLayoutLineSpacing, refreshSubtitleLayout);
            HookChanged(SubtitleLayoutStackOffsetPercent, refreshSubtitleLayout);
            HookChanged(SubtitleBgMarginY, refreshSubtitleLayout);

            Action refreshSubtitleStyle = TryRefreshSubtitleStyleRuntime;
            HookChanged(SubtitleFontBundleName, refreshSubtitleStyle);
            HookChanged(SubtitleFontFamilyCsv, refreshSubtitleStyle);
            HookChanged(SubtitleFontSize, refreshSubtitleStyle);
            HookChanged(SubtitleFontBold, refreshSubtitleStyle);
            HookChanged(SubtitleFontItalic, refreshSubtitleStyle);
            HookChanged(SubtitleAlignment, refreshSubtitleStyle);
            HookChanged(SubtitleLayoutOverrideAlign, refreshSubtitleStyle);
            HookChanged(SubtitleLayoutMaxWidthPercent, refreshSubtitleStyle);
            HookChanged(SubtitleWrap, refreshSubtitleStyle);
            HookChanged(SubtitleWrapLength, refreshSubtitleStyle);
            HookChanged(SubtitleOutlineEnabled, refreshSubtitleStyle);
            HookChanged(SubtitleOutlineColor, refreshSubtitleStyle);
            HookChanged(SubtitleOutlineDistX, refreshSubtitleStyle);
            HookChanged(SubtitleOutlineDistY, refreshSubtitleStyle);
            HookChanged(SubtitleShadowEnabled, refreshSubtitleStyle);
            HookChanged(SubtitleShadowColor, refreshSubtitleStyle);
            HookChanged(SubtitleShadowDistX, refreshSubtitleStyle);
            HookChanged(SubtitleShadowDistY, refreshSubtitleStyle);
            HookChanged(SubtitleShadowUseGraphicAlpha, refreshSubtitleStyle);
            HookChanged(SubtitleBgEnabled, refreshSubtitleStyle);
            HookChanged(SubtitleBgFit, refreshSubtitleStyle);
            HookChanged(SubtitleBgColor, refreshSubtitleStyle);
            HookChanged(SubtitleBgPaddingX, refreshSubtitleStyle);
            HookChanged(SubtitleBgPaddingY, refreshSubtitleStyle);
            HookChanged(SubtitleBgSprite, refreshSubtitleStyle);
            HookChanged(SubtitleBgShadowEnabled, refreshSubtitleStyle);
            HookChanged(SubtitleBgShadowColor, refreshSubtitleStyle);
            HookChanged(SubtitleBgShadowDistX, refreshSubtitleStyle);
            HookChanged(SubtitleBgShadowDistY, refreshSubtitleStyle);
            HookChanged(SubtitleBgShadowUseGraphicAlpha, refreshSubtitleStyle);

            Action refreshDanmaku = TryApplyDanmakuRuntime;
            HookChanged(DanmakuLanes, refreshDanmaku);
            HookChanged(DanmakuSpeed, refreshDanmaku);
            HookChanged(DanmakuMinGapPx, refreshDanmaku);
            HookChanged(DanmakuSpawnDelaySec, refreshDanmaku);
            HookChanged(DanmakuTopOffsetPercent, refreshDanmaku);
            HookChanged(DanmakuAreaMaxPercent, refreshDanmaku);

            Action refreshDanmakuStyle = TryRefreshDanmakuStyleRuntime;
            HookChanged(DanmakuFontBundleName, refreshDanmakuStyle);
            HookChanged(DanmakuFontFamilyCsv, refreshDanmakuStyle);
            HookChanged(DanmakuFontSize, refreshDanmakuStyle);
            HookChanged(DanmakuFontBold, refreshDanmakuStyle);
            HookChanged(DanmakuFontItalic, refreshDanmakuStyle);
            HookChanged(DanmakuOutlineEnabled, refreshDanmakuStyle);
            HookChanged(DanmakuOutlineColor, refreshDanmakuStyle);
            HookChanged(DanmakuOutlineDistX, refreshDanmakuStyle);
            HookChanged(DanmakuOutlineDistY, refreshDanmakuStyle);
            HookChanged(DanmakuShadowEnabled, refreshDanmakuStyle);
            HookChanged(DanmakuShadowColor, refreshDanmakuStyle);
            HookChanged(DanmakuShadowDistX, refreshDanmakuStyle);
            HookChanged(DanmakuShadowDistY, refreshDanmakuStyle);
            HookChanged(DanmakuShadowUseGraphicAlpha, refreshDanmakuStyle);

            Action refreshWorld3DStyle = TryRefreshWorld3DStyleRuntime;
            HookChanged(World3DFontBundleName, refreshWorld3DStyle);
            HookChanged(World3DFontFamilyCsv, refreshWorld3DStyle);
            HookChanged(World3DFontSize, refreshWorld3DStyle);
            HookChanged(World3DFontBold, refreshWorld3DStyle);
            HookChanged(World3DFontItalic, refreshWorld3DStyle);
            HookChanged(World3DAlignment, refreshWorld3DStyle);
            HookChanged(World3DWrap, refreshWorld3DStyle);
            HookChanged(World3DWrapLength, refreshWorld3DStyle);
            HookChanged(World3DWorldScale, refreshWorld3DStyle);
            HookChanged(World3DDynamicPixelsPerUnit, refreshWorld3DStyle);
            HookChanged(World3DFaceUpdateIntervalSec, refreshWorld3DStyle);
            HookChanged(World3DStackMaxLines, refreshWorld3DStyle);
            HookChanged(World3DStackOffsetY, refreshWorld3DStyle);
            HookChanged(World3DFadeInSec, refreshWorld3DStyle);
            HookChanged(World3DFadeOutSec, refreshWorld3DStyle);
            HookChanged(World3DOutlineEnabled, refreshWorld3DStyle);
            HookChanged(World3DOutlineColor, refreshWorld3DStyle);
            HookChanged(World3DOutlineDistX, refreshWorld3DStyle);
            HookChanged(World3DOutlineDistY, refreshWorld3DStyle);
            HookChanged(World3DShadowEnabled, refreshWorld3DStyle);
            HookChanged(World3DShadowColor, refreshWorld3DStyle);
            HookChanged(World3DShadowDistX, refreshWorld3DStyle);
            HookChanged(World3DShadowDistY, refreshWorld3DStyle);
            HookChanged(World3DShadowUseGraphicAlpha, refreshWorld3DStyle);
            HookChanged(World3DVerticalOffsetY, refreshWorld3DStyle);
            HookChanged(World3DFacePlayer, refreshWorld3DStyle);
            HookChanged(World3DBGEnabled, refreshWorld3DStyle);
            HookChanged(World3DBGColor, refreshWorld3DStyle);
            HookChanged(World3DShowSelf, refreshWorld3DStyle);

            // —— 最后：把 entries 赋回，并统一设置 Order —— //
            ConfigEntries = entries ?? new List<ConfigEntryBase>();
            EnsureConfigurationManagerAttributes(ConfigEntries);
            EnsureConfigurationManagerAttributes(DanmakuFontSize);
            RecalcOrder();
            RegisterPresetBindings();
            RegisterFoldBindings();
        }
    }

}
