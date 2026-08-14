using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.UI;
using Subtitle.Config;
using Subtitle.Patch;
using SubtitleSystem;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Subtitle;

namespace Subtitle
{
    [BepInPlugin("Volcano.Subtitle", "Volcano-Subtitle 火山家的实时字幕", "2.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log => Instance?.Logger;

        public GameObject SubtitleGO { get; private set; }
        public SubtitleManager SubtitleComponent { get; private set; }
        private GameObject _debugUiGo;
        private GameObject _standaloneCanvasGO;

        private static string PresetsDir
        {
            get { return Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "presets"); }
        }

        private void Awake()
        {
            Settings.Init(Config);
            Instance = this;
            DontDestroyOnLoad(this);

            // 图形化设置窗口：常驻创建（默认隐藏），热键轮询在下方 Plugin.Update 中
            SettingsUI.SettingsWindow.EnsureCreated();

            // 1) 告诉样式系统字体目录（先放着，当前仅用于 file 名推测）
            SubtitleSystem.SubtitleFontLoader.SetFontsDir(Path.Combine(PresetsDir, "fonts"));
            SubtitleSystem.SubtitleFontLoader.SetFontBundleDir(
                Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "FontReplace", "Font"));

            SubtitleSystem.SubtitleTextPreset.Current = null;

            // 刷新一次运行期层（此处 Instance 刚赋值为 this，直接调用即可）
            var mgr = GetOrCreateSubtitleManagerAnyScene();
            if (mgr != null)
            {
                mgr.ApplyDanmakuSettings();
                mgr.InitializeDanmakuLayer();
            }

            // 2) 预设不在这里自动加载；仅在“应用预设”时读取并覆盖当前设置

            if (Subtitle.Config.Settings.EnableDebugTools != null)
            {
                Subtitle.Config.Settings.EnableDebugTools.SettingChanged += (s, e) =>
                {
                    if (!Subtitle.Config.Settings.EnableDebugTools.Value && _debugUiGo != null)
                    {
                        Destroy(_debugUiGo);
                        _debugUiGo = null;
                    }
                    if (!Subtitle.Config.Settings.EnableDebugTools.Value)
                        Subtitle.DebugTools.DebugDiagnosticsPanel.CloseAndDestroy();
                };
            }

            EnablePatches();
        }

        private void EnablePatches()
        {
            var harmony = new HarmonyLib.Harmony("Volcano.Subtitle");

            // 先 PatchAll 整个程序集（包含嵌套类与独立 patch 类）
            try
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception e)
            {
                Log?.LogError("[Subtitle] PatchAll failed (continue): " + e);
            }

            new BattleUIScreenShowPatch().Enable();
            new GameWorldRegisterPlayerPatch().Enable();
            new GameWorldUnregisterPlayerPatch().Enable();

            // ====== LabRadioPatch 启动：预热广播文本映射 ======
            try
            {
                Subtitle.LabRadioPatch.Bootstrap();
            }
            catch (Exception e)
            {
                Log?.LogWarning("[LabBroadcast] bootstrap failed: " + e);
            }
        }

        internal void TryAttachToBattleUIScreen(EftBattleUIScreen screen)
        {
            if (screen.GetComponentInChildren<SubtitleManager>() != null)
            {
                Log.LogDebug("SubtitleManager already attached to BattleUI.");
                return;
            }

            DestroySubtitle();

            SubtitleGO = SubtitleManager.TryAttachToBattleUIScreen(screen);
            SubtitleComponent = SubtitleGO.GetComponent<SubtitleManager>();

            if (SubtitleComponent != null)
            {
                SubtitleComponent.SetVisible(true);
                Log.LogDebug("SubtitleManager successfully attached to BattleUI.");
            }
            else
            {
                Log.LogError("Failed to attach SubtitleManager to BattleUI.");
            }
        }

        internal bool OpenVoiceDebugPanel()
        {
            if (Singleton<GameWorld>.Instance == null) return false;
            if (_debugUiGo == null)
            {
                _debugUiGo = new GameObject("Subtitle.DebugUI");
                _debugUiGo.hideFlags = HideFlags.DontSave;
                DontDestroyOnLoad(_debugUiGo);
                _debugUiGo.AddComponent<Subtitle.DebugTools.DebugPhrasePanel>();
            }
            var panel = _debugUiGo.GetComponent<Subtitle.DebugTools.DebugPhrasePanel>();
            if (panel == null) return false;
            panel.Show();
            return true;
        }

        internal void DestroySubtitle()
        {
            if (SubtitleGO != null)
            {
                Destroy(SubtitleGO);
                SubtitleGO = null;
                SubtitleComponent = null;
                Log.LogDebug("SubtitleManager destroyed.");
            }
        }

        void Update()
        {
            // 设置界面热键：轮询放在这里（与调试面板同一条已验证可靠的路径）；SettingsWindow 自身不再轮询，避免双触发
            if (Subtitle.Config.Settings.SettingsWindowHotkey != null &&
                Subtitle.Config.Settings.SettingsWindowHotkey.Value.IsDown())
            {
                Log?.LogInfo("[SettingsUI] 设置界面热键触发，切换窗口显隐。");
                SettingsUI.SettingsWindow.ToggleVisible();
            }

            // 只在 Debug 开启且有 GameWorld（藏身处/离线局）时运行
            bool shouldEnable =
                Subtitle.Config.Settings.EnableDebugTools != null &&
                Subtitle.Config.Settings.EnableDebugTools.Value &&
                Singleton<GameWorld>.Instance != null;

            if (shouldEnable)
            {
                if (_debugUiGo == null)
                {
                    _debugUiGo = new GameObject("Subtitle.DebugUI");
                    _debugUiGo.hideFlags = HideFlags.DontSave;
                    DontDestroyOnLoad(_debugUiGo);
                    _debugUiGo.AddComponent<Subtitle.DebugTools.DebugPhrasePanel>();
                }

                if (Subtitle.Config.Settings.DebugPanelHotkey != null &&
                    Subtitle.Config.Settings.DebugPanelHotkey.Value.IsDown())
                {
                    var panel = _debugUiGo.GetComponent<Subtitle.DebugTools.DebugPhrasePanel>();
                    if (panel != null) panel.ToggleVisible();
                }
            }
            else
            {
                if (_debugUiGo != null)
                {
                    Destroy(_debugUiGo);
                    _debugUiGo = null;
                }
            }
        }

        public SubtitleSystem.SubtitleManager GetOrCreateSubtitleManagerAnyScene()
        {
            // 1) 已存在
            if (SubtitleSystem.SubtitleManager.Instance != null)
                return SubtitleSystem.SubtitleManager.Instance;

            // 2) 战斗UI存在 → 挂在战斗UI下
            var ui = UnityEngine.Object.FindObjectOfType<EFT.UI.EftBattleUIScreen>();
            if (ui != null)
            {
                var go = SubtitleSystem.SubtitleManager.TryAttachToBattleUIScreen(ui);
                return SubtitleSystem.SubtitleManager.Instance;
            }

            // 3) 创建独立 Canvas（仅用于测试）
            if (_standaloneCanvasGO == null)
            {
                _standaloneCanvasGO = new GameObject("SubtitleStandaloneCanvas",
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var cvs = _standaloneCanvasGO.GetComponent<Canvas>();
                cvs.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = _standaloneCanvasGO.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
            }

            // 在 Canvas 下新建一个 Panel + SubtitleManager
            var panel = new GameObject("SubtitleRoot", typeof(RectTransform));
            panel.transform.SetParent(_standaloneCanvasGO.transform, false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            var mgr = panel.AddComponent<SubtitleSystem.SubtitleManager>();
            mgr.InitializeDanmakuLayer();
            Subtitle.Plugin.Log?.LogInfo("[Danmaku] TestCanvas root ready.");
            return mgr;
        }
    }
}
