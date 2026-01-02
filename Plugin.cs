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
    [BepInPlugin("Volcano.Subtitle", "Volcano-Subtitle 火山家的实时字幕", "1.7.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log => Instance?.Logger;

        public GameObject SubtitleGO { get; private set; }
        public SubtitleManager SubtitleComponent { get; private set; }
        private GameObject _debugUiGo;
        private GameObject _standaloneCanvasGO;

        private GameObject _testCanvasRoot;         // 我们自己的 Overlay Canvas
        private SubtitleManager _testManager;       // 测试专用的 SubtitleManager

        private static string PresetsDir
        {
            get { return Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "subtitle", "presets"); }
        }

        private void Awake()
        {
            Settings.Init(Config);
            Instance = this;
            DontDestroyOnLoad(this);

            // 1) 告诉样式系统字体目录（先放着，当前仅用于 file 名推测）
            SubtitleSystem.SubtitleFontLoader.SetFontsDir(Path.Combine(PresetsDir, "fonts"));
            SubtitleSystem.SubtitleFontLoader.SetFontBundleDir(
                Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "FontReplace", "Font"));

            SubtitleSystem.SubtitleTextPreset.Current = null;

            // 刷新一次运行期层
            var mgr = Subtitle.Plugin.Instance != null
                ? Subtitle.Plugin.Instance.GetOrCreateSubtitleManagerAnyScene()
                : null;
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

            // --- 新增：仅当 Fika 存在时才尝试打探针（不会影响单机） ---
            try
            {
                SubtitlePatch.FikaManualPatch.TryPatchFikaIfPresent(harmony);
            }
            catch (Exception e)
            {
                Log?.LogWarning("[Subtitle] Fika manual patch failed: " + e);
            }

            new BattleUIScreenShowPatch().Enable();
            new GameWorldRegisterPlayerPatch().Enable();
            new GameWorldUnregisterPlayerPatch().Enable();

            harmony.PatchAll(typeof(SubtitlePatch));
            // ====== LabRadioPatch 启动自检 ======
            try
            {
                // 1) 反射确认目标方法存在
                var miQ = HarmonyLib.AccessTools.Method(typeof(global::QueuePlayer), "Play", new System.Type[] { typeof(UnityEngine.AudioClip) });
                if (miQ != null) Log?.LogInfo("[LabBroadcast] Target OK: QueuePlayer.Play(AudioClip)");
                else Log?.LogWarning("[LabBroadcast] Target NOT found: QueuePlayer.Play(AudioClip)");

                // 2) 主动调用 LabRadioPatch.Bootstrap()（打印“init bootstrap.”并预热映射）
                Subtitle.LabRadioPatch.Bootstrap();
            }
            catch (System.Exception e)
            {
                Log?.LogWarning("[LabBroadcast] bootstrap failed: " + e);
            }
            try
            {
                var mi = HarmonyLib.AccessTools.Method(
                    typeof(EFT.GlobalEvents.AudioEvents.BroadcastItemChangedEvent),
                    "Invoke",
                    new System.Type[] {
            typeof(CommonAssets.Scripts.Audio.RadioSystem.ERadioStation),
            typeof(CommonAssets.Scripts.Audio.RadioSystem.BroadcastItemData),
            typeof(float)
                    }
                );
                if (mi != null) Log?.LogInfo("[LabBroadcast] Hook target located: " + mi.DeclaringType + "." + mi.Name);
                else Log?.LogWarning("[LabBroadcast] Hook target NOT found.");
            }
            catch (Exception e)
            {
                Log?.LogWarning("[LabBroadcast] Hook target locate failed: " + e);
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

        private void EnsureTestCanvasAndManager()
        {
            if (_testCanvasRoot == null)
            {
                _testCanvasRoot = new GameObject("Subtitle.TestCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                DontDestroyOnLoad(_testCanvasRoot);

                var canvas = _testCanvasRoot.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                var scaler = _testCanvasRoot.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
            }

            if (_testManager == null)
            {
                var go = new GameObject("Subtitle.TestRoot", typeof(RectTransform));
                go.transform.SetParent(_testCanvasRoot.transform, false);

                _testManager = go.AddComponent<SubtitleManager>();
                _testManager.SetVisible(true);

                // —— 把内部生成的 "SubtitlePanel" 稍微调个位置/宽度，尽量确保可见 —— //
                var panelTr = go.transform.Find("SubtitleStackPanel") as RectTransform;
                if (panelTr != null)
                {
                    // 让垂直布局占满宽度，避免文字被挤成“竖排”
                    var vlg = panelTr.GetComponent<VerticalLayoutGroup>();
                    if (vlg != null) vlg.childForceExpandWidth = true;
                }
            }
        }

        private static readonly ManualLogSource s_Log =
        BepInEx.Logging.Logger.CreateLogSource("Subtitle.Debug");

        private static void LoadPresetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "default";

            var path = Path.Combine(PresetsDir, name + ".jsonc");
            if (!File.Exists(path))
            {
                // 兜底 default.jsonc
                path = Path.Combine(PresetsDir, "default.jsonc");
            }

            var preset = SubtitleSystem.SubtitleTextPreset.LoadFromFile(path);
            if (preset == null && !name.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                // 再尝试一次 default，防御性处理
                var fallback = Path.Combine(PresetsDir, "default.jsonc");
                preset = SubtitleSystem.SubtitleTextPreset.LoadFromFile(fallback);
            }

            SubtitleSystem.SubtitleTextPreset.Current = preset;
            if (preset == null)
            {
                s_Log.LogWarning("[SubtitleStyle] failed to load preset; styles will not apply until a valid preset is selected.");
            }
            else
            {
                s_Log.LogInfo("[SubtitleStyle] loaded preset: " + (string.IsNullOrEmpty(preset.Name) ? name : preset.Name));
            }
        }
    }
}
