using Newtonsoft.Json.Linq;
using Subtitle.Config;
using Subtitle.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Subtitle.DebugTools
{
    internal sealed class DebugVoiceEvent
    {
        public string Time;
        public string VoiceKey;
        public string Trigger;
        public string NetId;
        public string Speaker;
        public string AiType;
        public string Distance;
        public string Subtitle;
        public string Danmaku;
        public string World3D;
    }

    internal static class DebugDiagnostics
    {
        private const int Capacity = 200;
        private static readonly object s_Sync = new object();
        private static readonly List<DebugVoiceEvent> s_Events = new List<DebugVoiceEvent>();

        internal static void RecordVoice(string voiceKey, string trigger, string netId,
            string speaker, string aiType, float? distance,
            string subtitle, string danmaku, string world3D)
        {
            var item = new DebugVoiceEvent
            {
                Time = DateTime.Now.ToString("HH:mm:ss.fff"),
                VoiceKey = voiceKey ?? "?",
                Trigger = trigger ?? "?",
                NetId = netId ?? "?",
                Speaker = speaker ?? "?",
                AiType = aiType ?? "?",
                Distance = distance.HasValue ? distance.Value.ToString("0.0") + "m" : "-",
                Subtitle = subtitle ?? "-",
                Danmaku = danmaku ?? "-",
                World3D = world3D ?? "-"
            };
            lock (s_Sync)
            {
                s_Events.Insert(0, item);
                if (s_Events.Count > Capacity) s_Events.RemoveRange(Capacity, s_Events.Count - Capacity);
            }
            DebugDiagnosticsPanel.NotifyChanged();
        }

        internal static void RecordSystem(string message)
        {
            RecordVoice("SYSTEM", "-", "-", message, "-", null, "-", "-", "-");
        }

        internal static List<DebugVoiceEvent> Snapshot()
        {
            lock (s_Sync) return new List<DebugVoiceEvent>(s_Events);
        }

        internal static void Clear()
        {
            lock (s_Sync) s_Events.Clear();
            DebugDiagnosticsPanel.NotifyChanged();
        }

        internal static string BuildEventReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Volcano Subtitle - recent voice events");
            sb.AppendLine("Language: " + (I18n.CurrentLanguage ?? "?"));
            foreach (var e in Snapshot())
            {
                sb.AppendLine(string.Format("[{0}] {1} / {2} / #{3} | {4} ({5}) | {6}",
                    e.Time, e.VoiceKey, e.Trigger, e.NetId, e.Speaker, e.AiType, e.Distance));
                sb.AppendLine("  Subtitle: " + e.Subtitle);
                sb.AppendLine("  Danmaku:  " + e.Danmaku);
                sb.AppendLine("  World3D:  " + e.World3D);
            }
            return sb.ToString();
        }

        internal static string ScanCurrentLocale()
        {
            var sb = new StringBuilder();
            string language = I18n.CurrentLanguage ?? I18n.DefaultLanguage;
            string dir = PhraseFilterManager.LocalesDir;
            sb.AppendLine(string.Format(I18n.Text("Debug.Scan.Header", "当前语言：{0}"), language));
            sb.AppendLine(string.Format(I18n.Text("Debug.Scan.Directory", "目录：{0}"), dir ?? "?"));
            sb.AppendLine();

            int errors = 0;
            string[] objectFiles = { "UI.jsonc", "RoleType.jsonc", "LabBroadcast.jsonc", "Default_Voice.jsonc" };
            for (int i = 0; i < objectFiles.Length; i++)
            {
                string file = Path.Combine(dir ?? "", objectFiles[i]);
                try
                {
                    if (!File.Exists(file)) throw new FileNotFoundException(I18n.Text("Debug.Scan.Missing", "文件不存在"));
                    var root = JObject.Parse(JsoncUtils.StripJsonComments(File.ReadAllText(file, Encoding.UTF8)));
                    sb.AppendLine("[OK] " + objectFiles[i] + " (" + root.Count + ")");
                }
                catch (Exception e)
                {
                    errors++;
                    sb.AppendLine("[ERROR] " + objectFiles[i] + " - " + e.Message);
                }
            }

            string streamer = Path.Combine(dir ?? "", "StreamerWords.jsonc");
            try
            {
                if (!File.Exists(streamer)) throw new FileNotFoundException(I18n.Text("Debug.Scan.Missing", "文件不存在"));
                var words = JArray.Parse(JsoncUtils.StripJsonComments(File.ReadAllText(streamer, Encoding.UTF8)));
                sb.AppendLine("[OK] StreamerWords.jsonc (" + words.Count + ")");
            }
            catch (Exception e)
            {
                errors++;
                sb.AppendLine("[ERROR] StreamerWords.jsonc - " + e.Message);
            }

            int voiceFiles = 0;
            int voiceErrors = 0;
            int missingGeneral = 0;
            string voicesDir = Path.Combine(dir ?? "", "voices");
            if (!Directory.Exists(voicesDir))
            {
                sb.AppendLine("[INFO] voices/ - " + I18n.Text("Debug.Scan.NoVoiceFolder", "当前语言没有角色台词目录，将使用 Default_Voice。"));
            }
            else
            {
                foreach (string file in Directory.GetFiles(voicesDir, "*.jsonc", SearchOption.TopDirectoryOnly))
                {
                    voiceFiles++;
                    try
                    {
                        var root = JObject.Parse(JsoncUtils.StripJsonComments(File.ReadAllText(file, Encoding.UTF8)));
                        foreach (var property in root.Properties())
                        {
                            var map = property.Value as JObject;
                            if (map != null && map["General"] == null) missingGeneral++;
                        }
                    }
                    catch (Exception e)
                    {
                        voiceErrors++;
                        sb.AppendLine("[ERROR] voices/" + Path.GetFileName(file) + " - " + e.Message);
                    }
                }
            }
            errors += voiceErrors;
            sb.AppendLine();
            sb.AppendLine(string.Format(I18n.Text("Debug.Scan.VoiceSummary", "角色台词文件：{0}；解析错误：{1}；缺少 General 的触发器：{2}"),
                voiceFiles, voiceErrors, missingGeneral));
            sb.AppendLine(string.Format(I18n.Text("Debug.Scan.Result", "检查完成：{0} 个错误。"), errors));
            return sb.ToString();
        }
    }

    internal sealed class DebugDiagnosticsPanel : MonoBehaviour
    {
        private static DebugDiagnosticsPanel s_Instance;
        private GameObject _panel;
        private Text _body;
        private Button _events;
        private Button _resources;
        private bool _showResources;
        private bool _dirty = true;

        internal static void ToggleVisible()
        {
            if (s_Instance == null)
            {
                var go = new GameObject("Subtitle.DebugDiagnostics");
                go.hideFlags = HideFlags.DontSave;
                DontDestroyOnLoad(go);
                s_Instance = go.AddComponent<DebugDiagnosticsPanel>();
            }
            if (s_Instance._panel == null) s_Instance.BuildUI();
            bool show = !s_Instance._panel.activeSelf;
            s_Instance._panel.SetActive(show);
            if (show) { s_Instance._dirty = true; s_Instance.RefreshBody(); }
        }

        internal static void NotifyChanged()
        {
            if (s_Instance != null) s_Instance._dirty = true;
        }

        internal static void CloseAndDestroy()
        {
            if (s_Instance != null) Destroy(s_Instance.gameObject);
            s_Instance = null;
        }

        private void OnDestroy() { if (s_Instance == this) s_Instance = null; }
        private void Update() { if (_dirty && _panel != null && _panel.activeSelf) RefreshBody(); }

        private void BuildUI()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5100;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("Panel");
            _panel.transform.SetParent(canvasGo.transform, false);
            var root = _panel.AddComponent<RectTransform>();
            root.anchorMin = new Vector2(0.08f, 0.08f);
            root.anchorMax = new Vector2(0.92f, 0.92f);
            root.offsetMin = root.offsetMax = Vector2.zero;
            _panel.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.97f);

            var top = UiWidgets.CreateRect(root, "Top", new Vector2(0f, 0.91f), Vector2.one, new Vector2(8f, 6f), new Vector2(-8f, -6f));
            top.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
            var title = UiWidgets.CreateText(top, "Title", I18n.Text("Debug.Diagnostics.Title", "字幕诊断工具"), 18, TextAnchor.MiddleLeft, new Vector2(10f, 0f), new Vector2(-470f, 0f));
            _events = UiWidgets.CreateButton(top, "Events", I18n.Text("Debug.Diagnostics.Events", "实时事件"), new Vector2(0.55f, 0.1f), new Vector2(0.66f, 0.9f), new Color(0.22f, 0.4f, 0.22f, 1f), 13, false);
            _resources = UiWidgets.CreateButton(top, "Resources", I18n.Text("Debug.Diagnostics.Resources", "资源检查"), new Vector2(0.67f, 0.1f), new Vector2(0.78f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 13, false);
            var clear = UiWidgets.CreateButton(top, "Clear", I18n.Text("Debug.Diagnostics.Clear", "清空"), new Vector2(0.79f, 0.1f), new Vector2(0.85f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 12, false);
            var copy = UiWidgets.CreateButton(top, "Copy", I18n.Text("Debug.Diagnostics.Copy", "复制"), new Vector2(0.86f, 0.1f), new Vector2(0.91f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 12, false);
            var close = UiWidgets.CreateButton(top, "Close", I18n.Text("Close", "关闭"), new Vector2(0.92f, 0.1f), new Vector2(0.98f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 12, false);
            _events.onClick.AddListener(delegate { _showResources = false; _dirty = true; });
            _resources.onClick.AddListener(delegate { _showResources = true; _dirty = true; });
            clear.onClick.AddListener(delegate { if (!_showResources) DebugDiagnostics.Clear(); });
            copy.onClick.AddListener(delegate { GUIUtility.systemCopyBuffer = _showResources ? DebugDiagnostics.ScanCurrentLocale() : DebugDiagnostics.BuildEventReport(); });
            close.onClick.AddListener(delegate { _panel.SetActive(false); });

            var bodyArea = UiWidgets.CreateRect(root, "Body", Vector2.zero, new Vector2(1f, 0.91f), new Vector2(8f, 8f), new Vector2(-8f, -4f));
            ScrollRect scroll;
            RectTransform content;
            UiWidgets.MakeScrollWithContent(bodyArea, out scroll, out content, true);
            var bodyGo = new GameObject("Report");
            bodyGo.transform.SetParent(content, false);
            _body = bodyGo.AddComponent<Text>();
            _body.font = UiWidgets.DefaultFont;
            _body.fontSize = 13;
            _body.color = Color.white;
            _body.alignment = TextAnchor.UpperLeft;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            var le = bodyGo.AddComponent<LayoutElement>();
            le.minHeight = 600f;
            le.preferredHeight = 2000f;
            _panel.SetActive(false);
        }

        private void RefreshBody()
        {
            _dirty = false;
            if (_body == null) return;
            _body.text = _showResources ? DebugDiagnostics.ScanCurrentLocale() : DebugDiagnostics.BuildEventReport();
            var le = _body.GetComponent<LayoutElement>();
            if (le != null) le.preferredHeight = Mathf.Max(600f, (_body.text.Split('\n').Length + 2) * 19f);
            SetColor(_events, !_showResources);
            SetColor(_resources, _showResources);
        }

        private static void SetColor(Button button, bool selected)
        {
            var image = button != null ? button.GetComponent<Image>() : null;
            if (image != null) image.color = selected ? new Color(0.22f, 0.42f, 0.22f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);
        }
    }
}
