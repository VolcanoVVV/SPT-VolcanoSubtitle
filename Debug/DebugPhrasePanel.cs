using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Comfort.Common;
using EFT;
using BepInEx.Logging;
using Subtitle.Config;

namespace Subtitle.DebugTools
{
    public class DebugPhrasePanel : MonoBehaviour
    {
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
        private Button _btnClose;
        private Button _btnRefresh;
        private Text _hint;

        // 播放（2D）
        private AudioSource _src2D;

        // 当前所选 voiceKey
        private string _currentVoiceKey;

        void Awake()
        {
            // ★ 若未开启调试，直接自毁，省内存与 UI 构建
            if (Subtitle.Config.Settings.EnableDebugTools == null ||
                !Subtitle.Config.Settings.EnableDebugTools.Value)
            {
                Destroy(this.gameObject);
                return;
            }

            BuildUI();
            Hide();
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

            _title = UiWidgets.CreateText(top, "Title", "Phrase Debug (2D)", 18, TextAnchor.MiddleLeft, new Vector2(8f, 4f), new Vector2(-8f, -4f));
            var titleRT = _title.rectTransform;
            titleRT.anchorMin = new Vector2(0f, 0f);
            titleRT.anchorMax = new Vector2(0.6f, 1f);
            titleRT.offsetMin = new Vector2(10f, 0f);
            titleRT.offsetMax = new Vector2(-10f, 0f);

            _btnRefresh = UiWidgets.CreateButton(top, "Refresh", "刷新", new Vector2(0.7f, 0.1f), new Vector2(0.8f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 14, false);
            _btnRefresh.onClick.AddListener(new UnityEngine.Events.UnityAction(RefreshVoiceKeys));

            _btnClose = UiWidgets.CreateButton(top, "Close", "关闭", new Vector2(0.85f, 0.1f), new Vector2(0.95f, 0.9f), new Color(0.25f, 0.25f, 0.25f, 1f), 14, false);
            _btnClose.onClick.AddListener(new UnityEngine.Events.UnityAction(Hide));

            // 底部提示
            var bottom = UiWidgets.CreateRect(panel.transform, "BottomBar", new Vector2(0f, 0f), new Vector2(1f, 0.08f), new Vector2(8f, 8f), new Vector2(-8f, -8f));
            var bottomImg = bottom.gameObject.AddComponent<Image>();
            bottomImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            _hint = UiWidgets.CreateText(bottom, "Hint", "选择一个 VoiceKey（跨声线） → 右侧选择 trigger/netId → 点击播放", 14, TextAnchor.MiddleLeft, new Vector2(8f, 4f), new Vector2(-8f, -4f));
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
        }

        // ===== 数据刷新 =====

        private void RefreshVoiceKeys()
        {
            UiWidgets.ClearChildren(_voiceContent);
            UiWidgets.ClearChildren(_clipContent);
            _currentVoiceKey = null;

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
                if (count == 0) UiWidgets.AddInfoRow(_voiceContent, "PhraseSounds.Voices 为空。", false, new Color(0.9f, 0.9f, 0.9f, 1f));
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_voiceContent);
                return;
            }

            // —— 没有 PhraseSounds：退回“Speaker 模式” —— //
            AddHeaderRow(_voiceContent, "当前场景说话者（Speaker）");

            int sc = 0;
            BaseSpeaker first = null;

            foreach (var sp in GetAllSpeakers())
            {
                var ip = GetSpeakerOwner(sp);
                // ★ 优先：直接从 Speaker 拿 voiceKey（更稳）
                string vk = GetSpeakerVoiceKey(sp);
                if (string.IsNullOrEmpty(vk)) vk = GetVoiceKey(ip);

                // ★ label 优先显示 voiceKey；没有就退回玩家昵称/AI 角色
                string label = !string.IsNullOrEmpty(vk) ? vk : GetDisplayName(ip);

                var btn = UiWidgets.InstantiateButton(_voiceBtnTpl, _voiceContent, label, new Vector2(8f, 2f), new Vector2(-8f, -2f), false, 28f);
                btn.onClick.AddListener(delegate {
                    if (_currentSpeakerBtn != null) _currentSpeakerBtn.targetGraphic.color = new Color(0.2f, 0.2f, 0.2f, 1f);
                    _currentSpeakerBtn = btn;
                    _currentSpeakerBtn.targetGraphic.color = new Color(0.35f, 0.35f, 0.35f, 1f);

                    _title.text = "Phrase Debug (2D)  -  " + label;
                    RefreshClipsForSpeaker(sp);
                });

                if (first == null) first = sp;
                sc++;
            }

            if (sc == 0)
            {
                UiWidgets.AddInfoRow(_voiceContent, "未找到 PhraseSounds；且当前没有可用的 Speaker。请进入藏身处/离线局。", false, new Color(0.9f, 0.9f, 0.9f, 1f));
            }
            else
            {
                UiWidgets.AddInfoRow(_voiceContent, "发现 Speaker 数量: " + sc, false, new Color(0.9f, 0.9f, 0.9f, 1f));
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

        private static IPlayer GetSpeakerOwner(BaseSpeaker sp)
        {
            if (sp == null) return null;

            // 快路径
            object v =
                HarmonyLib.Traverse.Create(sp).Field("_player")?.GetValue() ??
                HarmonyLib.Traverse.Create(sp).Property("Player")?.GetValue() ??
                HarmonyLib.Traverse.Create(sp).Field("player")?.GetValue();

            var ip = TryUnwrapPlayer(v);
            if (ip != null) return ip;

            // 兜底：全扫描
            return TryUnwrapPlayer(sp);
        }

        private static string GetSpeakerVoiceKey(BaseSpeaker sp)
        {
            return GetVoiceKey(GetSpeakerOwner(sp));
        }

        private static string GetDisplayName(IPlayer p)
        {
            if (p == null) return "Speaker";
            try
            {
                if (!p.IsAI && p.Profile != null && !string.IsNullOrEmpty(p.Profile.Nickname))
                    return "Player: " + p.Profile.Nickname;
            }
            catch { }
            try
            {
                string role = (p.Profile != null && p.Profile.Info != null) ?
                    p.Profile.Info.Settings.Role.ToString() : "AI";
                return "AI: " + role;
            }
            catch { }
            return p.IsAI ? "AI" : "Player";
        }

        private void RefreshClipsForSpeaker(BaseSpeaker speaker)
        {
            UiWidgets.ClearChildren(_clipContent);
            if (speaker == null)
            {
                UiWidgets.AddInfoRow(_clipContent, "无效的 Speaker。", false, new Color(0.9f, 0.9f, 0.9f, 1f));
                return;
            }

            var map = GetTriggerBankMap(speaker);
            if (map == null)
            {
                UiWidgets.AddInfoRow(_clipContent, "未能读取该 Speaker 的短句映射。", false, new Color(0.9f, 0.9f, 0.9f, 1f));
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
                    var empty = UiWidgets.InstantiateButton(_clipBtnTpl, groupRT, "（此 Trigger 下未找到剪辑）", new Vector2(8f, 2f), new Vector2(-8f, -2f), false, 28f);
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
            _title.text = "Phrase Debug (2D)  -  " + voiceKey;
            RefreshClipsForVoice(voiceKey);
        }

        private void RefreshClipsForVoice(string voiceKey)
        {
            UiWidgets.ClearChildren(_clipContent);

            var ps = FindPhraseSounds();
            if (ps == null)
            {
                UiWidgets.AddInfoRow(_clipContent, "找不到 PhraseSounds（跨声线资源）。", false, new Color(0.9f, 0.9f, 0.9f, 1f));
                return;
            }

            // 用阵营推默认回退（Usec 作为默认）
            TagBank[] banks = null;
            try { banks = ps.GetVoice(voiceKey, EPlayerSide.Usec); } catch { }
            if (banks == null || banks.Length == 0)
            {
                UiWidgets.AddInfoRow(_clipContent, "该 VoiceKey 没有可用的 TagBank。", false, new Color(0.9f, 0.9f, 0.9f, 1f));
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
                        UiWidgets.AddInfoRow(_clipContent, "  (无 AudioClip) #" + nid, false, new Color(0.9f, 0.9f, 0.9f, 1f));
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

            var pi = bank.GetType().GetProperty("Clips", BF);
            if (pi != null && pi.CanRead)
            {
                try { return pi.GetValue(bank, null) as System.Collections.IEnumerable; } catch { }
            }

            var fi = bank.GetType().GetField("_clips", BF) ?? bank.GetType().GetField("clips", BF);
            if (fi != null)
            {
                try { return fi.GetValue(bank) as System.Collections.IEnumerable; } catch { }
            }

            return null;
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
                var f = t.GetField("AudioClip", BF) ?? t.GetField("_audioClip", BF) ?? t.GetField("clip", BF);
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
        private static System.Collections.IDictionary GetTriggerBankMap(BaseSpeaker spk)
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

        private static void CollectSpeakersFromObject(object obj, System.Collections.Generic.List<BaseSpeaker> dest)
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
                var single = v as BaseSpeaker;
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
                        var sp1 = de.Key as BaseSpeaker;
                        if (sp1 != null && !dest.Contains(sp1)) dest.Add(sp1);
                        var sp2 = de.Value as BaseSpeaker;
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
                        var sp = it as BaseSpeaker;
                        if (sp != null && !dest.Contains(sp)) dest.Add(sp);
                    }
                }
            }
        }

        // 取所有在场 PhraseSpeaker（主角 + AI），仅通过反射在 GameWorld / SpeakerManager 上找
        private static System.Collections.Generic.IEnumerable<BaseSpeaker> GetAllSpeakers()
        {
            var result = new System.Collections.Generic.List<BaseSpeaker>();
            var gw = Comfort.Common.Singleton<GameWorld>.Instance;
            if (gw == null) return result;

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
                        var sp = o as BaseSpeaker;
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
