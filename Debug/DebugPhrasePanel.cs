using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Comfort.Common;
using EFT;
using BepInEx.Logging;
using Subtitle.Config;
using Subtitle.Utils;
#if GAME_4_1
using SpeakerClass = EFT.BaseSpeaker;
#else
using SpeakerClass = PhraseSpeakerClass;
#endif

namespace Subtitle.DebugTools
{
    public class DebugPhrasePanel : MonoBehaviour
    {
        private static DebugPhrasePanel s_Instance;
        private static readonly ManualLogSource s_Log =
        BepInEx.Logging.Logger.CreateLogSource("Subtitle.PhraseDebug");

        // UI 根
        private Canvas _canvas;
        private RectTransform _root;
        private GameObject _panelBg;
        private Button _currentSpeakerBtn;

        // 左侧：VoiceKey 列表
        private ScrollRect _voiceScroll;
        private RectTransform _voiceContent;
        private Button _voiceBtnTpl;

        // 右侧：Trigger/NetId 列表
        private ScrollRect _clipScroll;
        private RectTransform _clipContent;
        private Button _clipBtnTpl;

        // 顶部：标题/关闭/刷新
        private Text _title;
        private Button _btnExport;
        private Button _btnStop;
        private Button _btnClose;
        private Button _btnRefresh;
        private Text _hint;

        // 播放（2D）
        private AudioSource _src2D;
        private AudioSource _exportSource;
        private AudioPcmCapture _exportCapture;

        // 当前所选 voiceKey
        private string _currentVoiceKey;
        private SpeakerClass _currentSpeaker;
        private Coroutine _exportRoutine;
        private bool _exportCancelRequested;

        void Awake()
        {
            // ★ 若未开启调试，直接自毁，省内存与 UI 构建
            if (Subtitle.Config.Settings.EnableDebugTools == null ||
                !Subtitle.Config.Settings.EnableDebugTools.Value)
            {
                Destroy(this.gameObject);
                return;
            }

            s_Instance = this;
            BuildUI();
            Hide();
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        public static void RefreshLocalization()
        {
            if (s_Instance == null) return;
            s_Instance.RefreshLocalizationInstance();
            if (s_Instance._panelBg != null && s_Instance._panelBg.activeSelf)
                s_Instance.RefreshVoiceKeys();
        }

        public void ToggleVisible()
        {
            // ★ 若未开启调试，直接无效
            if (Subtitle.Config.Settings.EnableDebugTools == null ||
                !Subtitle.Config.Settings.EnableDebugTools.Value)
                return;

            if (_panelBg != null) _panelBg.SetActive(!_panelBg.activeSelf);
            if (_panelBg != null && _panelBg.activeSelf)
            {
                RefreshLocalizationInstance();
                RefreshVoiceKeys();
            }
        }

        private void Hide()
        {
            if (_panelBg != null) _panelBg.SetActive(false);
        }

        // ===== UI 构建 =====

        private void BuildUI()
        {
            // Canvas
            var goCanvas = new GameObject("PhraseCanvas");
            goCanvas.transform.SetParent(this.transform, false);
            _canvas = goCanvas.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000;
            goCanvas.AddComponent<CanvasScaler>();
            goCanvas.AddComponent<GraphicRaycaster>();

            // 半透明背景
            _panelBg = new GameObject("PanelBg");
            _panelBg.transform.SetParent(goCanvas.transform, false);
            var bgRT = _panelBg.AddComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0f);
            bgRT.anchorMax = new Vector2(1f, 1f);
            bgRT.offsetMin = new Vector2(0f, 0f);
            bgRT.offsetMax = new Vector2(0f, 0f);
            var imgBg = _panelBg.AddComponent<Image>();
            imgBg.color = new Color(0f, 0f, 0f, 0.4f);

            // 主面板
            var panel = new GameObject("Panel");
            panel.transform.SetParent(_panelBg.transform, false);
            _root = panel.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.1f, 0.1f);
            _root.anchorMax = new Vector2(0.9f, 0.9f);
            _root.offsetMin = new Vector2(0f, 0f);
            _root.offsetMax = new Vector2(0f, 0f);
            var imgPanel = panel.AddComponent<Image>();
            imgPanel.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            // 顶部栏
            var top = UiWidgets.CreateRect(panel.transform, "TopBar", new Vector2(0f, 0.9f), new Vector2(1f, 1f), new Vector2(8f, 8f), new Vector2(-8f, -8f));
            var topImg = top.gameObject.AddComponent<Image>();
            topImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);

            _title = UiWidgets.CreateText(top, "Title", I18n.Text("Debug.Title", "Phrase Debug (2D)"), 18, TextAnchor.MiddleLeft, new Vector2(8f, 4f), new Vector2(-8f, -4f));
            var titleRT = _title.rectTransform;
            titleRT.anchorMin = new Vector2(0f, 0f);
            titleRT.anchorMax = new Vector2(0.45f, 1f);
            titleRT.offsetMin = new Vector2(10f, 0f);
            titleRT.offsetMax = new Vector2(-10f, 0f);

            _btnExport = UiWidgets.CreateButton(top, "ExportVoice", I18n.Text("Debug.ExportVoice", "导出声线"), new Vector2(0.46f, 0.1f), new Vector2(0.60f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 14, false);
            _btnExport.interactable = false;
            _btnExport.onClick.AddListener(new UnityEngine.Events.UnityAction(ExportSelectedVoice));

            _btnStop = UiWidgets.CreateButton(top, "Stop", I18n.Text("Debug.StopPlayback", "停止播放"), new Vector2(0.61f, 0.1f), new Vector2(0.72f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 13, false);
            _btnStop.onClick.AddListener(delegate { if (_src2D != null) _src2D.Stop(); });

            _btnRefresh = UiWidgets.CreateButton(top, "Refresh", I18n.Text("BtnRefresh", "刷新"), new Vector2(0.73f, 0.1f), new Vector2(0.84f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 14, false);
            _btnRefresh.onClick.AddListener(new UnityEngine.Events.UnityAction(RefreshVoiceKeys));

            _btnClose = UiWidgets.CreateButton(top, "Close", I18n.Text("Close", "关闭"), new Vector2(0.85f, 0.1f), new Vector2(0.97f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 14, false);
            _btnClose.onClick.AddListener(new UnityEngine.Events.UnityAction(Hide));

            // 底部提示
            var bottom = UiWidgets.CreateRect(panel.transform, "BottomBar", new Vector2(0f, 0f), new Vector2(1f, 0.08f), new Vector2(8f, 8f), new Vector2(-8f, -8f));
            var bottomImg = bottom.gameObject.AddComponent<Image>();
            bottomImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            _hint = UiWidgets.CreateText(bottom, "Hint", I18n.Text("Debug.Hint", "选择一个 VoiceKey（跨声线） → 右侧选择 trigger/netId → 点击播放"), 14, TextAnchor.MiddleLeft, new Vector2(8f, 4f), new Vector2(-8f, -4f));
            _hint.rectTransform.offsetMin = new Vector2(10f, 0f);

            // 左：VoiceKey 列表
            var left = UiWidgets.CreateRect(panel.transform, "Left", new Vector2(0f, 0.08f), new Vector2(0.35f, 0.9f), new Vector2(8f, 8f), new Vector2(-8f, -8f));
            var leftImg = left.gameObject.AddComponent<Image>();
            leftImg.color = new Color(0.12f, 0.12f, 0.12f, 1f);

            UiWidgets.MakeScrollWithContent(left, out _voiceScroll, out _voiceContent, false);
            _voiceBtnTpl = UiWidgets.CreateFlatButtonTemplate(this.transform, "VoiceBtnTpl", 28f, new Color(0.2f, 0.2f, 0.2f, 1f), true,
                new Color(0.3f, 0.3f, 0.3f, 1f), new Color(0.1f, 0.1f, 0.1f, 1f), 14, "Button", new Vector2(8f, 4f), new Vector2(-8f, -4f), false);

            // 右：Trigger/NetId 列表
            var right = UiWidgets.CreateRect(panel.transform, "Right", new Vector2(0.35f, 0.08f), new Vector2(1f, 0.9f), new Vector2(8f, 8f), new Vector2(-8f, -8f));
            var rightImg = right.gameObject.AddComponent<Image>();
            rightImg.color = new Color(0.12f, 0.12f, 0.12f, 1f);

            UiWidgets.MakeScrollWithContent(right, out _clipScroll, out _clipContent, false);
            _clipBtnTpl = UiWidgets.CreateFlatButtonTemplate(this.transform, "ClipBtnTpl", 28f, new Color(0.2f, 0.2f, 0.2f, 1f), true,
                new Color(0.3f, 0.3f, 0.3f, 1f), new Color(0.1f, 0.1f, 0.1f, 1f), 14, "Button", new Vector2(8f, 4f), new Vector2(-8f, -4f), false);

            // 播放器（2D）
            _src2D = this.gameObject.AddComponent<AudioSource>();
            _src2D.spatialBlend = 0f; // 强制 2D
            _src2D.playOnAwake = false;
            _src2D.loop = false;
            _src2D.volume = 1f;

            // 流式 AudioClip 无法调用 GetData；单独建立静音的实时捕获链作为导出兜底。
            // AudioPcmCapture 在音频线程中复制 PCM 后将输出清零，因此不会把几百条
            // 导出语音实际播放给用户听。
            var exportAudio = new GameObject("VoiceExportAudioCapture");
            exportAudio.transform.SetParent(this.transform, false);
            _exportSource = exportAudio.AddComponent<AudioSource>();
            _exportSource.spatialBlend = 0f;
            _exportSource.playOnAwake = false;
            _exportSource.loop = false;
            _exportSource.volume = 1f;
            _exportSource.ignoreListenerPause = true;
            _exportSource.bypassEffects = true;
            _exportSource.bypassListenerEffects = true;
            _exportSource.bypassReverbZones = true;
            _exportCapture = exportAudio.AddComponent<AudioPcmCapture>();
        }

        public void Show()
        {
            if (_panelBg == null) return;
            _panelBg.SetActive(true);
            RefreshLocalizationInstance();
            RefreshVoiceKeys();
        }

        private void RefreshLocalizationInstance()
        {
            if (_title != null)
                _title.text = string.IsNullOrEmpty(_currentVoiceKey)
                    ? I18n.Text("Debug.Title", "Phrase Debug (2D)")
                    : I18n.Text("Debug.Title", "Phrase Debug (2D)") + "  -  " + _currentVoiceKey;
            SetButtonText(_btnRefresh, I18n.Text("BtnRefresh", "刷新"));
            SetButtonText(_btnExport, _exportRoutine != null
                ? I18n.Text("Debug.CancelExport", "取消导出")
                : I18n.Text("Debug.ExportVoice", "导出声线"));
            SetButtonText(_btnStop, I18n.Text("Debug.StopPlayback", "停止播放"));
            SetButtonText(_btnClose, I18n.Text("Close", "关闭"));
            if (_hint != null)
                _hint.text = I18n.Text("Debug.Hint", "选择一个 VoiceKey（跨声线） → 右侧选择 trigger/netId → 点击播放");
        }

        private static void SetButtonText(Button button, string text)
        {
            if (button == null) return;
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.text = text;
        }

        // ===== 数据刷新 =====

        private void RefreshVoiceKeys()
        {
            UiWidgets.ClearChildren(_voiceContent);
            UiWidgets.ClearChildren(_clipContent);
            _currentVoiceKey = null;
            _currentSpeaker = null;
            if (_btnExport != null) _btnExport.interactable = true; // 导出期间按钮改作“取消”

            var ps = FindPhraseSounds();
            if (ps != null && ps.Voices != null && ps.Voices.Length > 0)
            {
                // 有 PhraseSounds → 按 VoiceKey 列表
                int count = 0;
                for (int i = 0; i < ps.Voices.Length; i++)
                {
                    var v = ps.Voices[i];
                    if (v == null) continue;
                    string name = GetVoiceName(v);
                    if (string.IsNullOrEmpty(name)) continue;
                    count++;

                    var btn = UiWidgets.InstantiateButton(_voiceBtnTpl, _voiceContent, name, new Vector2(8f, 2f), new Vector2(-8f, -2f), false, 28f);
                    var capturedName = name;
                    btn.onClick.AddListener(delegate { OnSelectVoiceKey(capturedName); });
                }
                if (count == 0) UiWidgets.AddInfoRow(_voiceContent, I18n.Text("Debug.EmptyVoices", "PhraseSounds.Voices 为空。"), false, new Color(0.9f, 0.9f, 0.9f, 1f));
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_voiceContent);
                return;
            }

            // —— 没有 PhraseSounds：退回“Speaker 模式” —— //
            AddHeaderRow(_voiceContent, I18n.Text("Debug.SceneSpeakers", "当前场景说话者（Speaker）"));

            int sc = 0;
            SpeakerClass first = null;

            foreach (var sp in GetAllSpeakers())
            {
                var ip = GetSpeakerOwner(sp);
                // ★ 优先：直接从 Speaker 拿 voiceKey（更稳）
                string vk = GetSpeakerVoiceKey(sp);
                if (string.IsNullOrEmpty(vk)) vk = GetVoiceKey(ip);

                string displayName = GetDisplayName(ip);
                string label;
                if (ip != null)
                {
                    label = string.IsNullOrEmpty(vk) ? displayName : displayName + "  [" + vk + "]";
                }
                else
                {
                    int speakerId = GetSpeakerId(sp);
                    string fallback = speakerId == int.MinValue ? "Speaker" : "Speaker #" + speakerId;
                    label = string.IsNullOrEmpty(vk) ? fallback : fallback + "  [" + vk + "]";
                }

                var btn = UiWidgets.InstantiateButton(_voiceBtnTpl, _voiceContent, label, new Vector2(8f, 2f), new Vector2(-8f, -2f), false, 28f);
                var capturedSpeaker = sp;
                var capturedVoiceKey = vk;
                var capturedLabel = label;
                btn.onClick.AddListener(delegate {
                    if (_currentSpeakerBtn != null) _currentSpeakerBtn.targetGraphic.color = new Color(0.2f, 0.2f, 0.2f, 1f);
                    _currentSpeakerBtn = btn;
                    _currentSpeakerBtn.targetGraphic.color = new Color(0.35f, 0.35f, 0.35f, 1f);

                    OnSelectSpeaker(capturedSpeaker, capturedVoiceKey, capturedLabel);
                });

                if (first == null) first = sp;
                sc++;
            }

            if (sc == 0)
            {
                UiWidgets.AddInfoRow(_voiceContent, I18n.Text("Debug.NoSpeakers", "未找到 PhraseSounds；且当前没有可用的 Speaker。请进入藏身处/离线局。"), false, new Color(0.9f, 0.9f, 0.9f, 1f));
            }
            else
            {
                UiWidgets.AddInfoRow(_voiceContent, string.Format(I18n.Text("Debug.SpeakerCount", "发现 Speaker 数量: {0}"), sc), false, new Color(0.9f, 0.9f, 0.9f, 1f));
                if (first != null) RefreshClipsForSpeaker(first);
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_voiceContent);
            }
        }

        // 尝试把任意对象解包成 IPlayer（有的类包了一层 Player/Owner/PlayerOwner）
        private static IPlayer TryUnwrapPlayer(object obj)
        {
            if (obj == null) return null;
            var ip = obj as IPlayer;
            if (ip != null) return ip;

            const System.Reflection.BindingFlags BF =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            // 常见桥接属性/字段
            string[] names = { "Player", "player", "_player", "Owner", "owner", "PlayerOwner", "playerOwner" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    var pi = obj.GetType().GetProperty(names[i], BF);
                    if (pi != null && pi.CanRead)
                    {
                        var v = pi.GetValue(obj, null);
                        ip = v as IPlayer;
                        if (ip != null) return ip;
                    }
                    var fi = obj.GetType().GetField(names[i], BF);
                    if (fi != null)
                    {
                        var v = fi.GetValue(obj);
                        ip = v as IPlayer;
                        if (ip != null) return ip;
                    }
                }
                catch { }
            }

            // 广义兜底：扫描所有成员，遇到 IPlayer 就返回
            var mems = obj.GetType().GetMembers(BF);
            for (int i = 0; i < mems.Length; i++)
            {
                object v = null;
                var pi = mems[i] as System.Reflection.PropertyInfo;
                if (pi != null && pi.CanRead) { try { v = pi.GetValue(obj, null); } catch { } }
                else
                {
                    var fi = mems[i] as System.Reflection.FieldInfo;
                    if (fi != null) { try { v = fi.GetValue(obj); } catch { } }
                }
                ip = v as IPlayer;
                if (ip != null) return ip;
            }
            return null;
        }

        private static IPlayer GetSpeakerOwner(SpeakerClass sp)
        {
            if (sp == null) return null;

            // 正式字幕在玩家注册时维护的 Speaker → IPlayer 索引优先级最高。
            var indexed = SpeakerIndex.TryGetBySpeaker(sp);
            if (indexed != null) return indexed;

            // 部分版本会在 Speaker 上直接保留 Player/Owner。
            object v =
                HarmonyLib.Traverse.Create(sp).Field("_player")?.GetValue() ??
                HarmonyLib.Traverse.Create(sp).Property("Player")?.GetValue() ??
                HarmonyLib.Traverse.Create(sp).Field("player")?.GetValue();

            var ip = TryUnwrapPlayer(v);
            if (ip != null) return ip;

            // BaseSpeaker 通常只保留 Id，不一定反向持有玩家；从 GameWorld 玩家集合比较 Speaker。
            var gw = Comfort.Common.Singleton<GameWorld>.Instance;
            if (gw != null)
            {
                try
                {
                    var players = gw.AllAlivePlayersList;
                    if (players != null)
                    {
                        int targetId = GetSpeakerId(sp);
                        for (int i = 0; i < players.Count; i++)
                        {
                            IPlayer player = players[i];
                            if (player == null) continue;
                            SpeakerIndex.IndexPlayer(player);

                            object playerSpeaker = GetPlayerSpeakerObject(player);
                            if (object.ReferenceEquals(playerSpeaker, sp)) return player;
                            if (targetId != int.MinValue && GetSpeakerId(playerSpeaker) == targetId) return player;
                        }
                    }
                }
                catch { }

                try
                {
                    IPlayer mainPlayer = gw.MainPlayer;
                    if (mainPlayer != null)
                    {
                        SpeakerIndex.IndexPlayer(mainPlayer);
                        object mainSpeaker = GetPlayerSpeakerObject(mainPlayer);
                        if (object.ReferenceEquals(mainSpeaker, sp)) return mainPlayer;
                        int targetId = GetSpeakerId(sp);
                        if (targetId != int.MinValue && GetSpeakerId(mainSpeaker) == targetId) return mainPlayer;
                    }
                }
                catch { }
            }

            // 最后保留旧版全成员扫描兜底。
            return TryUnwrapPlayer(sp);
        }

        private static string GetSpeakerVoiceKey(SpeakerClass sp)
        {
            if (sp == null) return null;

            try
            {
                string direct = sp.PlayerVoice;
                if (!string.IsNullOrEmpty(direct)) return direct;
            }
            catch { }

            string[] names = { "PlayerVoice", "Voice", "VoiceKey", "voice", "voiceKey" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    object value = HarmonyLib.Traverse.Create(sp).Property(names[i])?.GetValue() ??
                                   HarmonyLib.Traverse.Create(sp).Field(names[i])?.GetValue();
                    if (value != null && !string.IsNullOrEmpty(value.ToString())) return value.ToString();
                }
                catch { }
            }

            return GetVoiceKey(GetSpeakerOwner(sp));
        }

        private static string GetDisplayName(IPlayer p)
        {
            if (p == null) return "Speaker";
            string nickname = null;
            string role = null;
            try
            {
                if (p.Profile != null)
                {
                    nickname = p.Profile.Nickname;
                    if (p.Profile.Info != null)
                        role = p.Profile.Info.Settings.Role.ToString();
                }
            }
            catch { }

            try
            {
                if (!p.IsAI)
                    return string.IsNullOrEmpty(nickname) ? "Player" : nickname;

                string roleLabel = string.IsNullOrEmpty(role) ? "AI" : Settings.GetRoleLabel(role, role);
                if (!string.IsNullOrEmpty(nickname)) return nickname + " · " + roleLabel;
                return roleLabel;
            }
            catch { }

            if (!string.IsNullOrEmpty(nickname)) return nickname;
            return "Player";
        }

        private static object GetPlayerSpeakerObject(IPlayer player)
        {
            if (player == null) return null;

            var concrete = player as Player;
            if (concrete != null)
            {
                try { return concrete.Speaker; } catch { }
            }

            try
            {
                return HarmonyLib.Traverse.Create(player).Property("Speaker")?.GetValue() ??
                       HarmonyLib.Traverse.Create(player).Property("PhraseSpeaker")?.GetValue() ??
                       HarmonyLib.Traverse.Create(player).Field("Speaker")?.GetValue() ??
                       HarmonyLib.Traverse.Create(player).Field("_speaker")?.GetValue() ??
                       HarmonyLib.Traverse.Create(player).Field("_phraseSpeaker")?.GetValue();
            }
            catch { return null; }
        }

        private static int GetSpeakerId(object speaker)
        {
            if (speaker == null) return int.MinValue;
            var typed = speaker as SpeakerClass;
            if (typed != null)
            {
                try { return typed.Id; } catch { }
            }

            try
            {
                object value = HarmonyLib.Traverse.Create(speaker).Property("Id")?.GetValue() ??
                               HarmonyLib.Traverse.Create(speaker).Field("Id")?.GetValue() ??
                               HarmonyLib.Traverse.Create(speaker).Field("_id")?.GetValue();
                if (value != null) return Convert.ToInt32(value);
            }
            catch { }
            return int.MinValue;
        }

        private void RefreshClipsForSpeaker(SpeakerClass speaker)
        {
            UiWidgets.ClearChildren(_clipContent);
            if (speaker == null)
            {
                UiWidgets.AddInfoRow(_clipContent, I18n.Text("Debug.InvalidSpeaker", "无效的 Speaker。"), false, new Color(0.9f, 0.9f, 0.9f, 1f));
                return;
            }

            var map = GetTriggerBankMap(speaker);
            if (map == null)
            {
                UiWidgets.AddInfoRow(_clipContent, I18n.Text("Debug.NoSpeakerMap", "未能读取该 Speaker 的短句映射。"), false, new Color(0.9f, 0.9f, 0.9f, 1f));
                return;
            }

            foreach (System.Collections.DictionaryEntry de in map)
            {
                if (de.Key == null || de.Value == null) continue;
                string trigger = de.Key.ToString();
                var bank = de.Value;

                // 组容器
                var group = new GameObject("Group_" + trigger);
                group.transform.SetParent(_clipContent, false);
                var groupRT = group.AddComponent<RectTransform>();
                var groupLayout = group.AddComponent<VerticalLayoutGroup>();
                groupLayout.childForceExpandHeight = false;
                groupLayout.childControlHeight = true;
                groupLayout.spacing = 2f;

                // Header（可折叠）
                var headerBtn = UiWidgets.InstantiateButton(_clipBtnTpl, groupRT, "▶ " + trigger, new Vector2(8f, 2f), new Vector2(-8f, -2f), false, 28f);
                headerBtn.onClick.AddListener(delegate {
                    // 切换子项显隐
                    bool anyActive = false;
                    for (int i = 1; i < groupRT.childCount; i++)
                        if (groupRT.GetChild(i).gameObject.activeSelf) { anyActive = true; break; }
                    bool newActive = !anyActive;
                    for (int i = 1; i < groupRT.childCount; i++)
                        groupRT.GetChild(i).gameObject.SetActive(newActive);

                    // 切换图标
                    var lbl = headerBtn.GetComponentInChildren<Text>();
                    if (lbl != null)
                    {
                        string body = trigger;
                        if (newActive) lbl.text = "▼ " + body; else lbl.text = "▶ " + body;
                    }
                });

                // Clips
                var clips = ExtractTaggedClips(bank); // 用强化版，能抓到所有含 NetId 的集合
                int count = 0;

                if (clips != null)
                {
                    foreach (var tagged in clips)
                    {
                        if (tagged == null) continue;

                        var ac = GetAudioClipFromTagged(tagged);

                        // 先拿 AudioClip，再算时长兜底
                        int? netId = GetNetIdRobust(tagged);
                        float? lenSec = GetLengthSecRobust(tagged, ac);

                        string nidStr = netId.HasValue ? netId.Value.ToString() : "?";
                        string lenStr = lenSec.HasValue ? lenSec.Value.ToString("F2") : "?";

                        var row = UiWidgets.InstantiateButton(_clipBtnTpl, groupRT,
                            (ac != null ? "▶ " : "□ ") + " #" + nidStr + "   (" + lenStr + "s)", new Vector2(28f, 2f), new Vector2(-8f, -2f), false, 28f);

                        // 没有 AudioClip 的行置灰 & 不可点
                        if (ac == null)
                        {
                            row.interactable = false;
                            var g = row.targetGraphic as Graphic;
                            if (g != null) g.color = new Color(0.12f, 0.12f, 0.12f, 1f);
                        }
                        else
                        {
                            var clipToPlay = ac;
row.onClick.AddListener(delegate {
    if (_src2D != null && clipToPlay != null)
    {
        _src2D.PlayOneShot(clipToPlay);

        // 组合更详细的日志
        var owner = GetSpeakerOwner(speaker);
        string voiceKey = GetSpeakerVoiceKey(speaker);
        if (string.IsNullOrEmpty(voiceKey)) voiceKey = GetVoiceKey(owner);
        if (string.IsNullOrEmpty(voiceKey)) voiceKey = "?";

        string aiType = GetAITypeOrPlayer(owner);
        string nameForLog = GetDisplayName(owner);

        // 尝试拿 bank 名（拿不到就用 trigger）
        string bankName = trigger;
        try
        {
            var bn = HarmonyLib.Traverse.Create(bank).Property("name")?.GetValue();
            if (bn != null) bankName = bn.ToString();
        }
        catch { }

        // 输出到 BepInEx 控制台
        try
        {
            s_Log.LogInfo(
                "[PhraseDbg] voiceKey=" + voiceKey +
                " trigger=" + trigger +
                " netId=" + nidStr +
                " len=" + lenStr + "s " +
                " bank=" + bankName +
                " aiType=" + aiType +
                " name=" + nameForLog
            );
        }
        catch (System.Exception e)
        {
            s_Log.LogWarning("[PhraseDbg] log failed: " + e);
        }
    }
});
                        }
                        count++;
                    }
                }

                if (count == 0)
                {
                    var empty = UiWidgets.InstantiateButton(_clipBtnTpl, groupRT, I18n.Text("Debug.NoClipsForTrigger", "（此 Trigger 下未找到剪辑）"), new Vector2(8f, 2f), new Vector2(-8f, -2f), false, 28f);
                    empty.interactable = false;
                    var g = empty.targetGraphic as Graphic;
                    if (g != null) g.color = new Color(0.12f, 0.12f, 0.12f, 1f);
                }

                // 初始状态：收起（只显示 header）
                for (int i = 1; i < groupRT.childCount; i++) groupRT.GetChild(i).gameObject.SetActive(false);
            }
        }

        private void OnSelectVoiceKey(string voiceKey)
        {
            _currentVoiceKey = voiceKey;
            _currentSpeaker = null;
            _title.text = I18n.Text("Debug.Title", "Phrase Debug (2D)") + "  -  " + voiceKey;
            if (_btnExport != null && _exportRoutine == null) _btnExport.interactable = true;
            RefreshClipsForVoice(voiceKey);
        }

        private void OnSelectSpeaker(SpeakerClass speaker, string voiceKey, string label)
        {
            _currentSpeaker = speaker;
            _currentVoiceKey = voiceKey;
            if (string.IsNullOrEmpty(_currentVoiceKey))
            {
                int speakerId = GetSpeakerId(speaker);
                _currentVoiceKey = speakerId == int.MinValue ? "Speaker" : "Speaker_" + speakerId;
            }

            _title.text = I18n.Text("Debug.Title", "Phrase Debug (2D)") + "  -  " + label;
            if (_btnExport != null && _exportRoutine == null) _btnExport.interactable = speaker != null;
            RefreshClipsForSpeaker(speaker);
        }

        private sealed class VoiceExportItem
        {
            public AudioClip Clip;
            public string Trigger;
            public int NetId;
            public string FilePath;
        }

        private sealed class AudioPcmCapture : MonoBehaviour
        {
            private readonly object _sync = new object();
            private List<float> _samples = new List<float>();
            private bool _recording;
            private int _channels;

            public void Begin(int expectedSampleCount)
            {
                lock (_sync)
                {
                    _samples = new List<float>(Math.Max(1024, expectedSampleCount));
                    _channels = 0;
                    _recording = true;
                }
            }

            public float[] End(out int channels)
            {
                lock (_sync)
                {
                    _recording = false;
                    channels = _channels;
                    return _samples.ToArray();
                }
            }

            // Unity 在音频线程调用；这里只进行加锁复制，不访问其他 Unity 对象。
            private void OnAudioFilterRead(float[] data, int channels)
            {
                if (data == null || data.Length == 0) return;
                lock (_sync)
                {
                    if (!_recording) return;
                    if (_channels == 0) _channels = channels;
                    _samples.AddRange(data);

                    // 捕获用 AudioSource 必须实际进入 DSP 管线，但不应被用户听见。
                    Array.Clear(data, 0, data.Length);
                }
            }
        }

        private void ExportSelectedVoice()
        {
            if (_exportRoutine != null)
            {
                _exportCancelRequested = true;
                if (_hint != null) _hint.text = I18n.Text("Debug.ExportCancelPending", "正在取消导出…");
                return;
            }
            if (string.IsNullOrEmpty(_currentVoiceKey)) return;
            _exportCancelRequested = false;
            _exportRoutine = StartCoroutine(ExportVoiceCoroutine(_currentVoiceKey, _currentSpeaker));
            SetButtonText(_btnExport, I18n.Text("Debug.CancelExport", "取消导出"));
        }

        private IEnumerator ExportVoiceCoroutine(string voiceKey, SpeakerClass speaker)
        {
            // 导出期间这个按钮本身就是“取消导出”，必须保持可点击。
            if (_btnExport != null) _btnExport.interactable = true;

            // 确保 StartCoroutine 先把句柄写入 _exportRoutine；否则极短/空声线可能
            // 在首次 yield 前同步结束，随后又被赋回一个非空的已完成句柄。
            yield return null;

            int exported = 0;
            int skipped = 0;
            int failed = 0;
            bool canceled = false;
            string outputDir = null;
            List<VoiceExportItem> items = null;

            try
            {
                outputDir = GetVoiceExportDirectory(voiceKey);
                Directory.CreateDirectory(outputDir);
                items = speaker != null
                    ? CollectSpeakerExportItems(speaker, voiceKey, outputDir, ref skipped, ref failed)
                    : CollectVoiceExportItems(voiceKey, outputDir, ref skipped, ref failed);
            }
            catch (Exception e)
            {
                failed++;
                s_Log.LogError("[VoiceExport] 无法准备导出：" + e);
            }

            int total = items != null ? items.Count : 0;
            for (int i = 0; i < total; i++)
            {
                if (_exportCancelRequested) { canceled = true; break; }
                var item = items[i];
                if (item == null || item.Clip == null)
                {
                    failed++;
                    continue;
                }

                if (File.Exists(item.FilePath))
                {
                    skipped++;
                    continue;
                }

                // 大部分短句已由 PhraseSounds 加载；未加载时请求加载并分帧等待。
                try
                {
                    if (item.Clip.loadState == AudioDataLoadState.Unloaded)
                        item.Clip.LoadAudioData();
                }
                catch { }

                int waitFrames = 0;
                while (item.Clip != null &&
                       item.Clip.loadState == AudioDataLoadState.Loading &&
                       waitFrames < 600 && !_exportCancelRequested)
                {
                    waitFrames++;
                    yield return null;
                }

                if (_exportCancelRequested) { canceled = true; break; }
                string directError;
                if (TryWritePcm16Wav(item.Clip, item.FilePath, out directError))
                {
                    exported++;
                }
                else
                {
                    // Streaming / CompressedInMemory 等资源可正常播放，但禁止 GetData。
                    // 让专用 AudioSource 播放一次，并从 OnAudioFilterRead 捕获解码后的 PCM。
                    if (_hint != null)
                    {
                        _hint.text = string.Format(
                            I18n.Text("Debug.ExportCapture", "正在实时捕获流式音频 {0}/{1}：{2} / {3} / #{4}"),
                            i + 1, total, voiceKey, item.Trigger, item.NetId);
                    }

                    bool captureSucceeded = false;
                    string captureError = null;
                    yield return CaptureClipToWavCoroutine(item, delegate (bool ok, string err)
                    {
                        captureSucceeded = ok;
                        captureError = err;
                    });

                    if (_exportCancelRequested)
                    {
                        canceled = true;
                        break;
                    }

                    if (captureSucceeded)
                    {
                        exported++;
                        s_Log.LogInfo(
                            "[VoiceExport] 已通过实时捕获导出 " + voiceKey + "/" +
                            item.Trigger + "/" + item.NetId + " clip=" + item.Clip.name);
                    }
                    else
                    {
                        failed++;
                        s_Log.LogWarning(
                            "[VoiceExport] 导出失败 " + voiceKey + "/" + item.Trigger + "/" + item.NetId +
                            "：GetData=" + directError + "；实时捕获=" + captureError);
                    }
                }

                if (_hint != null)
                {
                    _hint.text = string.Format(
                        I18n.Text("Debug.ExportProgress", "正在导出 {0}：{1}/{2}（已导出 {3}，跳过 {4}，失败 {5}）"),
                        voiceKey, i + 1, total, exported, skipped, failed);
                }

                // 分散磁盘写入，避免数百条短句在同一帧完成而冻结界面。
                if ((i & 3) == 3) yield return null;
            }

            string summary = string.Format(
                I18n.Text("Debug.ExportDone", "导出完成：{0}；已导出 {1}，跳过 {2}，失败 {3}\n目录：{4}"),
                voiceKey, exported, skipped, failed, outputDir ?? "?");
            if (canceled)
                summary = I18n.Text("Debug.ExportCanceled", "导出已取消。\n") + summary;
            if (_hint != null) _hint.text = summary;
            s_Log.LogInfo("[VoiceExport] " + summary.Replace('\n', ' '));

            try
            {
                if (!string.IsNullOrEmpty(outputDir))
                {
                    File.WriteAllText(Path.Combine(outputDir, "VoiceExportReport.txt"),
                        "VoiceKey: " + voiceKey + Environment.NewLine +
                        "Exported: " + exported + Environment.NewLine +
                        "Skipped: " + skipped + Environment.NewLine +
                        "Failed: " + failed + Environment.NewLine +
                        "Canceled: " + (canceled ? "yes" : "no") + Environment.NewLine,
                        System.Text.Encoding.UTF8);
                }
            }
            catch (Exception e) { s_Log.LogWarning("[VoiceExport] report write failed: " + e.Message); }

            _exportRoutine = null;
            _exportCancelRequested = false;
            SetButtonText(_btnExport, I18n.Text("Debug.ExportVoice", "导出声线"));
            if (_btnExport != null && !string.IsNullOrEmpty(_currentVoiceKey))
                _btnExport.interactable = true;
        }

        private IEnumerator CaptureClipToWavCoroutine(
            VoiceExportItem item,
            Action<bool, string> completed)
        {
            if (item == null || item.Clip == null || _exportSource == null || _exportCapture == null)
            {
                completed(false, "实时捕获组件不可用。");
                yield break;
            }

            if (File.Exists(item.FilePath))
            {
                completed(true, null);
                yield break;
            }

            int outputRate = AudioSettings.outputSampleRate;
            int estimatedChannels = Math.Max(1, item.Clip.channels);
            int expectedSamples = Mathf.CeilToInt(
                Math.Max(0.1f, item.Clip.length) * Math.Max(1, outputRate) * estimatedChannels);

            _exportSource.Stop();
            _exportSource.clip = item.Clip;
            _exportCapture.Begin(expectedSamples);
            _exportSource.Play();

            float timeoutAt = Time.realtimeSinceStartup + Math.Max(5f, item.Clip.length + 5f);
            // 至少等待一帧，让 Play 请求进入音频线程。
            yield return null;
            while (_exportSource != null && _exportSource.isPlaying && Time.realtimeSinceStartup < timeoutAt)
            {
                if (_exportCancelRequested)
                {
                    _exportSource.Stop();
                    break;
                }
                yield return null;
            }

            bool timedOut = _exportSource != null && _exportSource.isPlaying;
            if (_exportSource != null) _exportSource.Stop();

            // 让最后一个 DSP buffer 有机会完成 OnAudioFilterRead。
            yield return null;
            int capturedChannels;
            float[] captured = _exportCapture.End(out capturedChannels);
            _exportSource.clip = null;

            // 用户主动取消时丢弃本次不完整捕获，避免留下一个看似有效的残缺 WAV。
            if (_exportCancelRequested)
            {
                completed(false, "export canceled");
                yield break;
            }

            if (timedOut)
            {
                completed(false, "播放超时。捕获样本=" + (captured != null ? captured.Length : 0));
                yield break;
            }
            if (captured == null || captured.Length == 0 || capturedChannels <= 0)
            {
                completed(false, "音频回调没有捕获到 PCM 数据。");
                yield break;
            }

            string error;
            bool written = TryWritePcm16Wav(
                captured, capturedChannels, outputRate, item.FilePath, out error);
            completed(written, error);
        }

        private static List<VoiceExportItem> CollectVoiceExportItems(
            string voiceKey,
            string outputDir,
            ref int skipped,
            ref int failed)
        {
            var result = new List<VoiceExportItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ps = FindPhraseSounds();
            if (ps == null) throw new InvalidOperationException("找不到 PhraseSounds。");

            TagBank[] banks = null;
            try { banks = ps.GetVoice(voiceKey, EPlayerSide.Usec); } catch { }
            if (banks == null || banks.Length == 0)
                throw new InvalidOperationException("该 VoiceKey 没有可用的 TagBank。");

            string safeVoiceKey = SanitizePathPart(voiceKey, "Voice");
            for (int i = 0; i < banks.Length; i++)
            {
                var bank = banks[i];
                if (bank == null) continue;

                string trigger = GetTriggerName(bank);
                if (string.IsNullOrEmpty(trigger)) trigger = "UnknownTrigger";
                string safeTrigger = SanitizePathPart(trigger, "UnknownTrigger");
                var clips = GetClipsFromBank(bank);
                if (clips == null) continue;

                foreach (var tagged in clips)
                {
                    if (tagged == null) continue;
                    var clip = GetAudioClipFromTagged(tagged);
                    int? netId = GetNetIdRobust(tagged);
                    if (clip == null || !netId.HasValue)
                    {
                        failed++;
                        s_Log.LogWarning(
                            "[VoiceExport] 无法收集 voiceKey=" + voiceKey +
                            " trigger=" + trigger +
                            " netId=" + (netId.HasValue ? netId.Value.ToString() : "?") +
                            " reason=" + (clip == null ? "AudioClip引用为空" : "NetId缺失") +
                            " taggedType=" + tagged.GetType().FullName);
                        continue;
                    }

                    string fileName = safeVoiceKey + "_" + safeTrigger + "_" + netId.Value + ".wav";
                    string filePath = Path.Combine(outputDir, fileName);
                    if (!seenPaths.Add(filePath) || File.Exists(filePath))
                    {
                        skipped++;
                        continue;
                    }

                    result.Add(new VoiceExportItem
                    {
                        Clip = clip,
                        Trigger = trigger,
                        NetId = netId.Value,
                        FilePath = filePath
                    });
                }
            }
            return result;
        }

        private static List<VoiceExportItem> CollectSpeakerExportItems(
            SpeakerClass speaker,
            string voiceKey,
            string outputDir,
            ref int skipped,
            ref int failed)
        {
            var result = new List<VoiceExportItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var map = GetTriggerBankMap(speaker);
            if (map == null) throw new InvalidOperationException("未能读取该 Speaker 的短句映射。");

            string safeVoiceKey = SanitizePathPart(voiceKey, "Voice");
            foreach (DictionaryEntry entry in map)
            {
                if (entry.Key == null || entry.Value == null) continue;
                string trigger = entry.Key.ToString();
                if (string.IsNullOrEmpty(trigger)) trigger = "UnknownTrigger";
                string safeTrigger = SanitizePathPart(trigger, "UnknownTrigger");
                var clips = ExtractTaggedClips(entry.Value);
                if (clips == null) continue;

                foreach (var tagged in clips)
                {
                    if (tagged == null) continue;
                    var clip = GetAudioClipFromTagged(tagged);
                    int? netId = GetNetIdRobust(tagged);
                    if (clip == null || !netId.HasValue)
                    {
                        failed++;
                        s_Log.LogWarning(
                            "[VoiceExport] 无法收集 voiceKey=" + voiceKey +
                            " trigger=" + trigger +
                            " netId=" + (netId.HasValue ? netId.Value.ToString() : "?") +
                            " reason=" + (clip == null ? "AudioClip引用为空" : "NetId缺失") +
                            " taggedType=" + tagged.GetType().FullName);
                        continue;
                    }

                    string fileName = safeVoiceKey + "_" + safeTrigger + "_" + netId.Value + ".wav";
                    string filePath = Path.Combine(outputDir, fileName);
                    if (!seenPaths.Add(filePath) || File.Exists(filePath))
                    {
                        skipped++;
                        continue;
                    }

                    result.Add(new VoiceExportItem
                    {
                        Clip = clip,
                        Trigger = trigger,
                        NetId = netId.Value,
                        FilePath = filePath
                    });
                }
            }
            return result;
        }

        private static string GetVoiceExportDirectory(string voiceKey)
        {
            string pluginDir = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "BepInEx", "plugins", "subtitle"));
            return Path.Combine(pluginDir, "VoiceExports", SanitizePathPart(voiceKey, "Voice"));
        }

        private static string SanitizePathPart(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            }
            string safe = new string(chars).Trim().TrimEnd('.');
            return string.IsNullOrEmpty(safe) ? fallback : safe;
        }

        private static bool TryWritePcm16Wav(AudioClip clip, string destinationPath, out string error)
        {
            error = null;
            if (clip == null)
            {
                error = "AudioClip 为空。";
                return false;
            }
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                error = "音频数据未加载，状态=" + clip.loadState;
                return false;
            }

            string tempPath = destinationPath + ".tmp";
            try
            {
                if (clip.loadType == AudioClipLoadType.Streaming)
                    throw new InvalidOperationException("AudioClip 使用 Streaming 加载，GetData 不可用。");

                int channels = clip.channels;
                int sampleRate = clip.frequency;
                int sampleCount = checked(clip.samples * channels);
                if (channels <= 0 || sampleRate <= 0 || sampleCount <= 0)
                    throw new InvalidDataException("无效的音频参数。");

                var samples = new float[sampleCount];
                if (!clip.GetData(samples, 0))
                    throw new InvalidOperationException("AudioClip.GetData 返回失败；该资源可能采用不可读取的流式/压缩加载方式。");

                return TryWritePcm16Wav(samples, channels, sampleRate, destinationPath, out error);
            }
            catch (Exception e)
            {
                error = e.Message;
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }
        }

        private static bool TryWritePcm16Wav(
            float[] samples,
            int channels,
            int sampleRate,
            string destinationPath,
            out string error)
        {
            error = null;
            string tempPath = destinationPath + ".tmp";
            try
            {
                if (samples == null || samples.Length == 0)
                    throw new InvalidDataException("PCM 样本为空。");
                if (channels <= 0 || sampleRate <= 0)
                    throw new InvalidDataException("无效的 PCM 参数。");
                if (File.Exists(destinationPath)) return true;

                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream))
                {
                    int dataLength = checked(samples.Length * 2);
                    writer.Write(new byte[] { 0x52, 0x49, 0x46, 0x46 }); // RIFF
                    writer.Write(36 + dataLength);
                    writer.Write(new byte[] { 0x57, 0x41, 0x56, 0x45 }); // WAVE
                    writer.Write(new byte[] { 0x66, 0x6D, 0x74, 0x20 }); // fmt
                    writer.Write(16);
                    writer.Write((short)1);
                    writer.Write((short)channels);
                    writer.Write(sampleRate);
                    writer.Write(sampleRate * channels * 2);
                    writer.Write((short)(channels * 2));
                    writer.Write((short)16);
                    writer.Write(new byte[] { 0x64, 0x61, 0x74, 0x61 }); // data
                    writer.Write(dataLength);

                    for (int i = 0; i < samples.Length; i++)
                    {
                        float value = samples[i];
                        if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
                        value = Mathf.Clamp(value, -1f, 1f);
                        writer.Write((short)Mathf.RoundToInt(value * 32767f));
                    }
                }

                if (File.Exists(destinationPath))
                {
                    File.Delete(tempPath);
                    return true;
                }
                File.Move(tempPath, destinationPath);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }
        }

        private void RefreshClipsForVoice(string voiceKey)
        {
            UiWidgets.ClearChildren(_clipContent);

            var ps = FindPhraseSounds();
            if (ps == null)
            {
                UiWidgets.AddInfoRow(_clipContent, I18n.Text("Debug.NoPhraseSounds", "找不到 PhraseSounds（跨声线资源）。"), false, new Color(0.9f, 0.9f, 0.9f, 1f));
                return;
            }

            // 用阵营推默认回退（Usec 作为默认）
            TagBank[] banks = null;
            try { banks = ps.GetVoice(voiceKey, EPlayerSide.Usec); } catch { }
            if (banks == null || banks.Length == 0)
            {
                UiWidgets.AddInfoRow(_clipContent, I18n.Text("Debug.NoTagBank", "该 VoiceKey 没有可用的 TagBank。"), false, new Color(0.9f, 0.9f, 0.9f, 1f));
                return;
            }

            // 按 Trigger 分组展示
            for (int i = 0; i < banks.Length; i++)
            {
                var bank = banks[i];
                if (bank == null) continue;

                string trigger = GetTriggerName(bank);
                if (string.IsNullOrEmpty(trigger)) trigger = "(UnknownTrigger)";

                // 分组标题
                AddHeaderRow(_clipContent, "▶ " + trigger);

                var clips = GetClipsFromBank(bank);
                if (clips == null) continue;

                foreach (var tagged in clips)
                {
                    if (tagged == null) continue;

                    // netId / length
                    object nidObj = null;
                    object lenObj = null;
                    try { nidObj = Traverse.Create(tagged).Property("NetId")?.GetValue(); } catch { }
                    try { if (lenObj == null) lenObj = Traverse.Create(tagged).Property("Length")?.GetValue(); } catch { }

                    string nid = nidObj != null ? nidObj.ToString() : "?";
                    string len = lenObj != null ? lenObj.ToString() : "?";

                    // AudioClip
                    var ac = GetAudioClipFromTagged(tagged);
                    if (ac == null)
                    {
                        UiWidgets.AddInfoRow(_clipContent, string.Format(I18n.Text("Debug.NoAudioClip", "  (无 AudioClip) #{0}"), nid), false, new Color(0.9f, 0.9f, 0.9f, 1f));
                        continue;
                    }

                    var text = trigger + "   #" + nid + "   (" + len + "s)";
                    var btn = UiWidgets.InstantiateButton(_clipBtnTpl, _clipContent, text, new Vector2(8f, 2f), new Vector2(-8f, -2f), false, 28f);

                    var clipToPlay = ac; // 闭包捕获
                    btn.onClick.AddListener(delegate {
                        if (clipToPlay != null && _src2D != null)
                        {
                            _src2D.PlayOneShot(clipToPlay);
                        }
                    });
                }
            }
        }

        // ===== PhraseSounds 访问 & 反射工具 =====

        private static PhraseSounds FindPhraseSounds()
        {
            UnityEngine.Object[] arr = Resources.FindObjectsOfTypeAll(typeof(PhraseSounds));
            if (arr != null && arr.Length > 0) return arr[0] as PhraseSounds;
            return null;
        }

        private static string GetVoiceName(object voiceObj)
        {
            if (voiceObj == null) return null;
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 优先属性 Name
            try
            {
                var pi = voiceObj.GetType().GetProperty("Name", BF);
                if (pi != null && pi.CanRead)
                {
                    object v = pi.GetValue(voiceObj, null);
                    if (v != null) return v.ToString();
                }
            }
            catch { }

            // 退回字段 Name
            try
            {
                var fi = voiceObj.GetType().GetField("Name", BF);
                if (fi != null)
                {
                    object v = fi.GetValue(voiceObj);
                    if (v != null) return v.ToString();
                }
            }
            catch { }
            return null;
        }

        private static string GetTriggerName(object tagBank)
        {
            if (tagBank == null) return null;
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                var pi = tagBank.GetType().GetProperty("Trigger", BF);
                if (pi != null && pi.CanRead)
                {
                    object v = pi.GetValue(tagBank, null);
                    if (v != null) return v.ToString();
                }
            }
            catch { }

            try
            {
                var namePi = tagBank.GetType().GetProperty("name", BF);
                if (namePi != null && namePi.CanRead)
                {
                    object v = namePi.GetValue(tagBank, null);
                    if (v != null) return v.ToString();
                }
            }
            catch { }

            return null;
        }

        private static System.Collections.IEnumerable GetClipsFromBank(object bank)
        {
            if (bank == null) return null;
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // TagBank 在部分声线中同时保存：
            //   1) 顶层 Clips；
            //   2) SpreadGroups[].Clips。
            // 某些顶层 TaggedClip 只有 NetId 而 Clip 为空，真正的 AudioClip 位于
            // SpreadGroup 的同 NetId 项中。只返回顶层集合会造成 OnMutter/OnBreath
            // 等分组出现可见 NetId 却无法导出的情况。
            var merged = new List<object>();
            var indexByNetId = new Dictionary<int, int>();

            System.Collections.IEnumerable direct = GetEnumerableMember(
                bank, BF, "Clips", "_clips", "clips");
            MergeTaggedClips(direct, merged, indexByNetId);

            System.Collections.IEnumerable spreadGroups = GetEnumerableMember(
                bank, BF, "SpreadGroups", "_spreadGroups", "spreadGroups");
            if (spreadGroups != null)
            {
                foreach (object group in spreadGroups)
                {
                    if (group == null) continue;
                    System.Collections.IEnumerable groupClips = GetEnumerableMember(
                        group, BF, "Clips", "_clips", "clips");
                    MergeTaggedClips(groupClips, merged, indexByNetId);
                }
            }

            return merged.Count > 0 ? merged : null;
        }

        private static System.Collections.IEnumerable GetEnumerableMember(
            object owner,
            BindingFlags bindingFlags,
            params string[] names)
        {
            if (owner == null || names == null) return null;
            var type = owner.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    var property = type.GetProperty(names[i], bindingFlags);
                    if (property != null && property.CanRead)
                    {
                        var value = property.GetValue(owner, null) as System.Collections.IEnumerable;
                        if (value != null) return value;
                    }

                    var field = type.GetField(names[i], bindingFlags);
                    if (field != null)
                    {
                        var value = field.GetValue(owner) as System.Collections.IEnumerable;
                        if (value != null) return value;
                    }
                }
                catch { }
            }
            return null;
        }

        private static void MergeTaggedClips(
            System.Collections.IEnumerable source,
            List<object> merged,
            Dictionary<int, int> indexByNetId)
        {
            if (source == null || merged == null || indexByNetId == null) return;
            foreach (object tagged in source)
            {
                if (tagged == null) continue;
                int? netId = GetNetIdRobust(tagged);
                if (!netId.HasValue)
                {
                    if (!merged.Contains(tagged)) merged.Add(tagged);
                    continue;
                }

                int existingIndex;
                if (!indexByNetId.TryGetValue(netId.Value, out existingIndex))
                {
                    indexByNetId.Add(netId.Value, merged.Count);
                    merged.Add(tagged);
                    continue;
                }

                // 同 NetId 在顶层和 SpreadGroup 同时出现时，保留真正带音频的版本。
                object existing = merged[existingIndex];
                if (GetAudioClipFromTagged(existing) == null &&
                    GetAudioClipFromTagged(tagged) != null)
                {
                    merged[existingIndex] = tagged;
                }
            }
        }

        private static AudioClip GetAudioClipFromTagged(object taggedClip)
        {
            if (taggedClip == null) return null;
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = taggedClip.GetType();

            try
            {
                var p = t.GetProperty("AudioClip", BF);
                if (p != null && p.CanRead)
                {
                    var v = p.GetValue(taggedClip, null) as AudioClip;
                    if (v != null) return v;
                }
            }
            catch { }

            try
            {
                var f = t.GetField("AudioClip", BF) ?? t.GetField("Clip", BF) ??
                        t.GetField("_audioClip", BF) ?? t.GetField("_clip", BF) ??
                        t.GetField("clip", BF);
                if (f != null)
                {
                    var v = f.GetValue(taggedClip) as AudioClip;
                    if (v != null) return v;
                }
            }
            catch { }

            // 兜底扫描
            var mems = t.GetMembers(BF);
            for (int i = 0; i < mems.Length; i++)
            {
                object v = null;
                var pi = mems[i] as PropertyInfo;
                if (pi != null && pi.CanRead) { try { v = pi.GetValue(taggedClip, null); } catch { } }
                else
                {
                    var fi = mems[i] as FieldInfo;
                    if (fi != null) { try { v = fi.GetValue(taggedClip); } catch { } }
                }
                var ac = v as AudioClip;
                if (ac != null) return ac;
            }
            return null;
        }

        // ===== UI 辅助 =====

        // 触发器分组标题（右侧列表用）
        private static void AddHeaderRow(RectTransform parent, string text)
        {
            // 父：带背景的容器（只挂 Image，不挂 Text）
            var box = new GameObject("Header");
            box.transform.SetParent(parent, false);

            var rt = box.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 26f);

            var img = box.AddComponent<Image>();
            img.color = new Color(0.18f, 0.18f, 0.18f, 1f);

            // 可选：固定高度，配合 VerticalLayoutGroup 更稳定
            var le = box.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;
            le.minHeight = 26f;

            // 子：真正的文本（只能让子物体挂 Text）
            var label = new GameObject("Label");
            label.transform.SetParent(box.transform, false);

            var tr = label.AddComponent<RectTransform>();
            tr.anchorMin = new Vector2(0f, 0f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.offsetMin = new Vector2(8f, 2f);
            tr.offsetMax = new Vector2(-8f, -2f);

            var t = label.AddComponent<Text>();
            t.font = UiWidgets.DefaultFont;
            t.text = text;
            t.fontSize = 14;
            t.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            t.alignment = TextAnchor.MiddleLeft;
        }

        // 从 PhraseSpeaker 上找 “EPhraseTrigger -> TagBank” 的映射     
        private static System.Collections.IDictionary GetTriggerBankMap(SpeakerClass spk)
        {
            if (spk == null) return null;
            const System.Reflection.BindingFlags BF =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            // 优先找字典类型字段/属性，键类型名里包含 EPhraseTrigger
            var fs = spk.GetType().GetFields(BF);
            for (int i = 0; i < fs.Length; i++)
            {
                object v = null; try { v = fs[i].GetValue(spk); } catch { }
                var dict = v as System.Collections.IDictionary;
                if (dict == null) continue;

                foreach (System.Collections.DictionaryEntry de in dict)
                {
                    if (de.Key == null) continue;
                    string keyTypeName = de.Key.GetType().Name;
                    if (keyTypeName.IndexOf("EPhraseTrigger", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return dict;
                    break; // 看到第一条就够了
                }
            }

            // 兜底：看看是否有属性暴露
            var ps = spk.GetType().GetProperties(BF);
            for (int i = 0; i < ps.Length; i++)
            {
                if (!ps[i].CanRead) continue;
                object v = null; try { v = ps[i].GetValue(spk, null); } catch { }
                var dict = v as System.Collections.IDictionary;
                if (dict == null) continue;

                foreach (System.Collections.DictionaryEntry de in dict)
                {
                    if (de.Key == null) continue;
                    string keyTypeName = de.Key.GetType().Name;
                    if (keyTypeName.IndexOf("EPhraseTrigger", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return dict;
                    break;
                }
            }

            return null;
        }

        private static void CollectSpeakersFromObject(object obj, System.Collections.Generic.List<SpeakerClass> dest)
        {
            if (obj == null) return;

            const System.Reflection.BindingFlags BF =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            var t = obj.GetType();
            var members = t.GetMembers(BF);
            for (int i = 0; i < members.Length; i++)
            {
                object v = null;
                var pi = members[i] as System.Reflection.PropertyInfo;
                if (pi != null && pi.CanRead)
                {
                    try { v = pi.GetValue(obj, null); } catch { v = null; }
                }
                else
                {
                    var fi = members[i] as System.Reflection.FieldInfo;
                    if (fi != null)
                    {
                        try { v = fi.GetValue(obj); } catch { v = null; }
                    }
                }

                if (v == null || v is string) continue;

                // 直接就是一个 speaker
                var single = v as SpeakerClass;
                if (single != null)
                {
                    if (!dest.Contains(single)) dest.Add(single);
                    continue;
                }

                // 字典：尝试 key/value
                var dict = v as System.Collections.IDictionary;
                if (dict != null)
                {
                    foreach (System.Collections.DictionaryEntry de in dict)
                    {
                        var sp1 = de.Key as SpeakerClass;
                        if (sp1 != null && !dest.Contains(sp1)) dest.Add(sp1);
                        var sp2 = de.Value as SpeakerClass;
                        if (sp2 != null && !dest.Contains(sp2)) dest.Add(sp2);
                    }
                    continue;
                }

                // 可枚举：遍历元素
                var en = v as System.Collections.IEnumerable;
                if (en != null)
                {
                    foreach (object it in en)
                    {
                        var sp = it as SpeakerClass;
                        if (sp != null && !dest.Contains(sp)) dest.Add(sp);
                    }
                }
            }
        }

        // 取所有在场 PhraseSpeaker（主角 + AI），仅通过反射在 GameWorld / SpeakerManager 上找
        private static System.Collections.Generic.IEnumerable<SpeakerClass> GetAllSpeakers()
        {
            var result = new System.Collections.Generic.List<SpeakerClass>();
            var gw = Comfort.Common.Singleton<GameWorld>.Instance;
            if (gw == null) return result;

            // 优先从玩家集合取 Speaker：这条路径天然保留 IPlayer/Profile 关系，可直接显示名字。
            try
            {
                var players = gw.AllAlivePlayersList;
                if (players != null)
                {
                    for (int i = 0; i < players.Count; i++)
                    {
                        IPlayer player = players[i];
                        if (player == null) continue;
                        SpeakerIndex.IndexPlayer(player);
                        var speaker = GetPlayerSpeakerObject(player) as SpeakerClass;
                        if (speaker != null && !result.Contains(speaker)) result.Add(speaker);
                    }
                }
            }
            catch { }

            try
            {
                IPlayer mainPlayer = gw.MainPlayer;
                if (mainPlayer != null)
                {
                    SpeakerIndex.IndexPlayer(mainPlayer);
                    var speaker = GetPlayerSpeakerObject(mainPlayer) as SpeakerClass;
                    if (speaker != null && !result.Contains(speaker)) result.Add(speaker);
                }
            }
            catch { }

            // A) 常规入口：GameWorld.SpeakerManager
            object mgr =
                HarmonyLib.Traverse.Create(gw).Property("SpeakerManager")?.GetValue() ??
                HarmonyLib.Traverse.Create(gw).Field("SpeakerManager")?.GetValue();

            if (mgr != null)
            {
                // 先尝试常见集合：Speakers/_speakers
                object raw =
                    HarmonyLib.Traverse.Create(mgr).Property("Speakers")?.GetValue() ??
                    HarmonyLib.Traverse.Create(mgr).Field("Speakers")?.GetValue() ??
                    HarmonyLib.Traverse.Create(mgr).Field("_speakers")?.GetValue();

                var en = raw as System.Collections.IEnumerable;
                if (en != null)
                {
                    foreach (object o in en)
                    {
                        var sp = o as SpeakerClass;
                        if (sp != null && !result.Contains(sp)) result.Add(sp);
                    }
                }

                // 如果还没抓到，全面反射 Manager 自己
                if (result.Count == 0) CollectSpeakersFromObject(mgr, result);
            }

            // B) 仍为空：全面反射 GameWorld
            if (result.Count == 0) CollectSpeakersFromObject(gw, result);

            return result;
        }

        // 从 IPlayer 里拿 voiceKey（Profile.Info.Voice），多重兜底
        private static string GetVoiceKey(IPlayer p)
        {
            if (p == null) return null;
            try
            {
                var profile = p.Profile;
                if (profile == null) return null;
                var info = HarmonyLib.Traverse.Create(profile).Property("Info")?.GetValue()
                        ?? HarmonyLib.Traverse.Create(profile).Field("Info")?.GetValue();
                if (info == null) return null;
                var voice = HarmonyLib.Traverse.Create(info).Property("Voice")?.GetValue()
                         ?? HarmonyLib.Traverse.Create(info).Field("Voice")?.GetValue();
                return voice != null ? voice.ToString() : null;
            }
            catch { return null; }
        }

        

        // ai / player 标签（用于日志）
        private static string GetAITypeOrPlayer(IPlayer p)
        {
            if (p == null) return "unknown";
            try
            {
                if (!p.IsAI) return "player";
                var role = p.Profile != null ? p.Profile.Info.Settings.Role.ToString() : "ai";
                return role;
            }
            catch { return "ai"; }
        }

        // 兜底拿 NetId（优先 NetId/Id/Index；其次模糊搜索带 "net" 或 "id" 的整数字段/属性）
        private static int? GetNetIdRobust(object tagged)
        {
            if (tagged == null) return null;
            const System.Reflection.BindingFlags BF =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            // 1) 常见名字
            string[] names = { "NetId", "netId", "NetID", "netID", "Id", "id", "Index", "index" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    var pi = tagged.GetType().GetProperty(names[i], BF);
                    if (pi != null && pi.CanRead)
                    {
                        object v = pi.GetValue(tagged, null);
                        if (v != null) return System.Convert.ToInt32(v);
                    }
                    var fi = tagged.GetType().GetField(names[i], BF);
                    if (fi != null)
                    {
                        object v = fi.GetValue(tagged);
                        if (v != null) return System.Convert.ToInt32(v);
                    }
                }
                catch { }
            }

            // 2) 模糊搜索：任何整型成员，名字含 "net" 或 "id"
            var mems = tagged.GetType().GetMembers(BF);
            for (int i = 0; i < mems.Length; i++)
            {
                object v = null;
                string n = mems[i].Name.ToLowerInvariant();

                var pi = mems[i] as System.Reflection.PropertyInfo;
                if (pi != null && pi.CanRead)
                {
                    try { v = pi.GetValue(tagged, null); } catch { v = null; }
                }
                else
                {
                    var fi = mems[i] as System.Reflection.FieldInfo;
                    if (fi != null) { try { v = fi.GetValue(tagged); } catch { v = null; } }
                }

                if (v == null) continue;

                var t = v.GetType();
                bool isIntLike =
                    t == typeof(int) || t == typeof(uint) ||
                    t == typeof(short) || t == typeof(ushort) ||
                    t == typeof(long) || t == typeof(ulong) || t.IsEnum;

                if (isIntLike && (n.Contains("net") || n == "id" || n.EndsWith("id") || n.Contains("index")))
                {
                    try { return System.Convert.ToInt32(v); } catch { }
                }
            }

            return null;
        }

        // 兜底拿时长（秒）：优先 Length/Duration/ClipLength；否则用 AudioClip.length
        private static float? GetLengthSecRobust(object tagged, AudioClip clipIfKnown)
        {
            const System.Reflection.BindingFlags BF =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            if (tagged != null)
            {
                string[] names = { "Length", "length", "Duration", "duration", "ClipLength", "clipLength" };
                for (int i = 0; i < names.Length; i++)
                {
                    try
                    {
                        var pi = tagged.GetType().GetProperty(names[i], BF);
                        if (pi != null && pi.CanRead)
                        {
                            object v = pi.GetValue(tagged, null);
                            if (v != null) return System.Convert.ToSingle(v);
                        }
                        var fi = tagged.GetType().GetField(names[i], BF);
                        if (fi != null)
                        {
                            object v = fi.GetValue(tagged);
                            if (v != null) return System.Convert.ToSingle(v);
                        }
                    }
                    catch { }
                }

                // 模糊：任意浮点成员，名字含 length/duration/seconds/time
                var mems = tagged.GetType().GetMembers(BF);
                for (int i = 0; i < mems.Length; i++)
                {
                    object v = null;
                    var pi = mems[i] as System.Reflection.PropertyInfo;
                    if (pi != null && pi.CanRead) { try { v = pi.GetValue(tagged, null); } catch { v = null; } }
                    else
                    {
                        var fi = mems[i] as System.Reflection.FieldInfo;
                        if (fi != null) { try { v = fi.GetValue(tagged); } catch { v = null; } }
                    }
                    if (v == null) continue;

                    var n = mems[i].Name.ToLowerInvariant();
                    var t = v.GetType();
                    bool isFloatLike = t == typeof(float) || t == typeof(double);

                    if (isFloatLike && (n.Contains("length") || n.Contains("duration") || n.Contains("second") || n.Contains("time")))
                    {
                        try { return System.Convert.ToSingle(v); } catch { }
                    }
                }
            }

            // 兜底：AudioClip.length
            if (clipIfKnown != null)
                return clipIfKnown.length;

            return null;
        }

        // 强化的 clips 抓取：如果没有 Clips/_clips，就扫描所有集合，找含有 NetId 成员的列表
        private static System.Collections.IEnumerable ExtractTaggedClips(object bank)
        {
            if (bank == null) return null;

            // 先走你原来的 GetClipsFromBank
            var clips = GetClipsFromBank(bank);
            if (clips != null) return clips;

            const System.Reflection.BindingFlags BF =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            var t = bank.GetType();
            var members = t.GetMembers(BF);
            for (int i = 0; i < members.Length; i++)
            {
                object v = null;
                var pi = members[i] as System.Reflection.PropertyInfo;
                if (pi != null && pi.CanRead) { try { v = pi.GetValue(bank, null); } catch { v = null; } }
                else
                {
                    var fi = members[i] as System.Reflection.FieldInfo;
                    if (fi != null) { try { v = fi.GetValue(bank); } catch { v = null; } }
                }
                var en = v as System.Collections.IEnumerable;
                if (en == null) continue;

                // 看第一个元素是否带 NetId
                var enumerator = en.GetEnumerator();
                try
                {
                    if (enumerator != null && enumerator.MoveNext())
                    {
                        var first = enumerator.Current;
                        if (first != null)
                        {
                            var hasNetId =
                                HarmonyLib.Traverse.Create(first).Property("NetId")?.GetValue() != null ||
                                first.GetType().GetField("NetId", BF) != null ||
                                first.GetType().GetProperty("NetId", BF) != null;
                            if (hasNetId) return en;
                        }
                    }
                }
                finally
                {
                    var disp = enumerator as System.IDisposable;
                    if (disp != null) disp.Dispose();
                }
            }
            return null;
        }
    }

}
