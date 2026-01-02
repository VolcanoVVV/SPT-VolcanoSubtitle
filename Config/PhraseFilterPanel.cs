using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Subtitle.Config
{
    public class PhraseFilterPanel : MonoBehaviour
    {
        private static PhraseFilterPanel s_instance;

        private Canvas _canvas;
        private GameObject _panelBg;
        private RectTransform _root;

        private ScrollRect _voiceScroll;
        private RectTransform _voiceContent;
        private Button _voiceBtnTpl;

        private ScrollRect _lineScroll;
        private RectTransform _lineContent;
        private Button _lineBtnTpl;

        private Text _title;
        private Text _hint;
        private GameObject _tooltipGo;
        private RectTransform _tooltipRt;
        private Text _tooltipText;
        private readonly Dictionary<string, bool> _triggerExpanded =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // 顶部：频道 3 按钮
        private Button _btnChSubtitle;
        private Button _btnChDanmaku;
        private Button _btnChWorld3D;

        private Button _btnApply;
        private Button _btnRefresh;
        private Button _btnClose;

        // 选择状态
        private string _currentVoiceKey;

        // 用于高亮：voiceKey -> button
        private readonly Dictionary<string, Button> _voiceButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

        // 自动刷新（等待资源加载）
        private Coroutine _autoRefresh;
        private int _lastVoiceCount = -1;
        private const float AutoRefreshInterval = 1f;

        // 颜色（你可自行微调）
        private static readonly Color VoiceRowNormal = new Color(0.20f, 0.20f, 0.20f, 1f);
        private static readonly Color VoiceRowSelected = new Color(0.34f, 0.34f, 0.34f, 1f);

        private static readonly Color ChannelNormal = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color ChannelSelected = new Color(0.40f, 0.40f, 0.40f, 1f);

        public static void ToggleVisible()
        {
            if (s_instance == null)
            {
                var go = new GameObject("PhraseFilterPanel");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<PhraseFilterPanel>();
            }
            s_instance.Toggle();
        }

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            StopAutoRefresh();
        }

        private void Toggle()
        {
            if (_panelBg == null) return;
            bool next = !_panelBg.activeSelf;
            _panelBg.SetActive(next);
            if (next)
            {
                // 进来先刷新一次；保持当前选择（如果之前选过）
                RefreshVoiceList(true);

                // 如果有已选声线，右侧也保持
                if (!string.IsNullOrEmpty(_currentVoiceKey))
                {
                    SetVoiceSelectionVisual(_currentVoiceKey);
                    RefreshLinesForVoice(_currentVoiceKey);
                    UpdateTitle();
                }
                else
                {
                    UpdateTitle();
                }

                StartAutoRefresh();
            }
            else
            {
                StopAutoRefresh();
            }
        }

        private void Hide()
        {
            if (_panelBg != null) _panelBg.SetActive(false);
            StopAutoRefresh();
            HideTooltip();
        }

        private void StartAutoRefresh()
        {
            if (_autoRefresh != null) return;
            _autoRefresh = StartCoroutine(AutoRefreshLoop());
        }

        private void StopAutoRefresh()
        {
            if (_autoRefresh == null) return;
            StopCoroutine(_autoRefresh);
            _autoRefresh = null;
        }

        private IEnumerator AutoRefreshLoop()
        {
            while (_panelBg != null && _panelBg.activeSelf)
            {
                if (_lastVoiceCount <= 0)
                {
                    // 资源没加载到时反复刷
                    RefreshVoiceList(true);
                    if (!string.IsNullOrEmpty(_currentVoiceKey))
                    {
                        SetVoiceSelectionVisual(_currentVoiceKey);
                        RefreshLinesForVoice(_currentVoiceKey);
                        UpdateTitle();
                    }
                }
                else
                {
                    _autoRefresh = null;
                    yield break;
                }
                yield return new WaitForSecondsRealtime(AutoRefreshInterval);
            }
            _autoRefresh = null;
        }

        private void BuildUI()
        {
            var goCanvas = new GameObject("PhraseFilterCanvas");
            goCanvas.transform.SetParent(transform, false);
            _canvas = goCanvas.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5001;
            goCanvas.AddComponent<CanvasScaler>();
            goCanvas.AddComponent<GraphicRaycaster>();

            _panelBg = new GameObject("PanelBg");
            _panelBg.transform.SetParent(goCanvas.transform, false);
            var bgRT = _panelBg.AddComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0f);
            bgRT.anchorMax = new Vector2(1f, 1f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var imgBg = _panelBg.AddComponent<Image>();
            imgBg.color = new Color(0f, 0f, 0f, 0.4f);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_panelBg.transform, false);
            _root = panel.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.1f, 0.1f);
            _root.anchorMax = new Vector2(0.9f, 0.9f);
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;
            var imgPanel = panel.AddComponent<Image>();
            imgPanel.color = new Color(0.1f, 0.1f, 0.1f, 0.92f);

            // ---------- TopBar ----------
            var top = CreateRect(panel.transform, "TopBar", new Vector2(0f, 0.9f), new Vector2(1f, 1f));
            var topImg = top.gameObject.AddComponent<Image>();
            topImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);

            _title = CreateText(top, "Title", "台词显示控制面板", 18, TextAnchor.MiddleLeft);
            var titleRT = _title.rectTransform;
            titleRT.anchorMin = new Vector2(0f, 0f);
            titleRT.anchorMax = new Vector2(0.38f, 1f);
            titleRT.offsetMin = new Vector2(10f, 0f);
            titleRT.offsetMax = new Vector2(-10f, 0f);

            var channelLabel = CreateText(top, "ChannelLabel", "类型:", 14, TextAnchor.MiddleRight);
            var channelLabelRT = channelLabel.rectTransform;
            channelLabelRT.anchorMin = new Vector2(0.38f, 0.1f);
            channelLabelRT.anchorMax = new Vector2(0.45f, 0.9f);
            channelLabelRT.offsetMin = new Vector2(4f, 0f);
            channelLabelRT.offsetMax = new Vector2(-4f, 0f);

            // 频道三按钮（并列）
            _btnChSubtitle = CreateButton(top, "ChSubtitle", "字幕", new Vector2(0.45f, 0.15f), new Vector2(0.55f, 0.85f));
            _btnChDanmaku = CreateButton(top, "ChDanmaku", "弹幕", new Vector2(0.55f, 0.15f), new Vector2(0.65f, 0.85f));
            _btnChWorld3D = CreateButton(top, "ChWorld3D", "3D气泡", new Vector2(0.65f, 0.15f), new Vector2(0.75f, 0.85f));

            _btnChSubtitle.onClick.AddListener(delegate { OnClickChannel("Subtitle"); });
            _btnChDanmaku.onClick.AddListener(delegate { OnClickChannel("Danmaku"); });
            _btnChWorld3D.onClick.AddListener(delegate { OnClickChannel("World3D"); });

            _btnApply = CreateButton(top, "Apply", "应用", new Vector2(0.82f, 0.15f), new Vector2(0.90f, 0.85f));
            _btnRefresh = CreateButton(top, "Refresh", "刷新", new Vector2(0.90f, 0.15f), new Vector2(0.95f, 0.85f));
            _btnClose = CreateButton(top, "Close", "关闭", new Vector2(0.95f, 0.15f), new Vector2(1.0f, 0.85f));

            _btnApply.onClick.AddListener(ApplyAndSave);
            _btnRefresh.onClick.AddListener(OnClickRefresh);
            _btnClose.onClick.AddListener(Hide);

            // 初始频道高亮  
            UpdateChannelButtonsVisual(PhraseFilterManager.CurrentChannel);

            // ---------- BottomBar ----------
            var bottom = CreateRect(panel.transform, "BottomBar", new Vector2(0f, 0f), new Vector2(1f, 0.08f));
            var bottomImg = bottom.gameObject.AddComponent<Image>();
            bottomImg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            _hint = CreateText(bottom, "Hint", "选择声线 -> 右侧控 语音事件/语音ID 开关台词。", 13, TextAnchor.MiddleLeft);
            _hint.rectTransform.offsetMin = new Vector2(10f, 0f);

            // ---------- Left ----------
            var left = CreateRect(panel.transform, "Left", new Vector2(0f, 0.08f), new Vector2(0.35f, 0.9f));
            var leftImg = left.gameObject.AddComponent<Image>();
            leftImg.color = new Color(0.12f, 0.12f, 0.12f, 1f);

            MakeScrollWithContent(left, out _voiceScroll, out _voiceContent);
            _voiceBtnTpl = CreateFlatButtonTemplate(panel.transform, "VoiceBtnTpl");

            // ---------- Right ----------
            var right = CreateRect(panel.transform, "Right", new Vector2(0.35f, 0.08f), new Vector2(1f, 0.9f));
            var rightImg = right.gameObject.AddComponent<Image>();
            rightImg.color = new Color(0.12f, 0.12f, 0.12f, 1f);

            MakeScrollWithContent(right, out _lineScroll, out _lineContent);
            _lineBtnTpl = CreateFlatButtonTemplate(panel.transform, "LineBtnTpl");

            CreateTooltip(panel.transform);
        }

        private void Update()
        {
            if (_tooltipGo != null && _tooltipGo.activeSelf)
                UpdateTooltipPosition();
        }

        private void OnClickRefresh()
        {
            // 重新加载 preset（如果你 Manager 内部就是默认 preset，这里也没问题）
            PhraseFilterManager.TryLoadPreset(PhraseFilterManager.CurrentPresetName);

            // 关键：刷新时保留当前选择
            RefreshVoiceList(true);

            if (!string.IsNullOrEmpty(_currentVoiceKey))
            {
                SetVoiceSelectionVisual(_currentVoiceKey);
                RefreshLinesForVoice(_currentVoiceKey);
            }
            UpdateTitle();
        }

        private void OnClickChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;

            // 切频道时保留声线选择
            PhraseFilterManager.SetCurrentChannel(channel);
            UpdateChannelButtonsVisual(channel);

            RefreshVoiceList(true);

            if (!string.IsNullOrEmpty(_currentVoiceKey))
            {
                SetVoiceSelectionVisual(_currentVoiceKey);
                RefreshLinesForVoice(_currentVoiceKey);
            }
            UpdateTitle();
        }

        private void ApplyAndSave()
        {
            var preset = PhraseFilterManager.GetOrCreateCurrent();
            PhraseFilterManager.SavePreset(PhraseFilterManager.CurrentPresetName, preset);

            // 保存后刷新，保留选择
            RefreshVoiceList(true);
            if (!string.IsNullOrEmpty(_currentVoiceKey))
            {
                SetVoiceSelectionVisual(_currentVoiceKey);
                RefreshLinesForVoice(_currentVoiceKey);
            }
            UpdateTitle();
        }

        private void RefreshVoiceList(bool keepSelection)
        {
            ClearChildren(_voiceContent);
            _voiceButtons.Clear();

            // 右侧是否清空：这里为了稳妥，先清空再按当前 voice 重新画
            ClearChildren(_lineContent);

            if (!keepSelection)
                _currentVoiceKey = null;

            var voices = PhraseFilterManager.ListVoiceKeys();
            _lastVoiceCount = voices.Count;

            if (voices.Count == 0)
            {
                AddInfoRow(_voiceContent, "未加载声线资源，稍后重试。");
                LayoutRebuilder.ForceRebuildLayoutImmediate(_voiceContent);
                return;
            }

            // 如果当前已选声线不在列表里，就清空（理论上不会发生）
            if (keepSelection && !string.IsNullOrEmpty(_currentVoiceKey))
            {
                bool exists = voices.Any(v => string.Equals(v, _currentVoiceKey, StringComparison.OrdinalIgnoreCase));
                if (!exists) _currentVoiceKey = null;
            }

            for (int i = 0; i < voices.Count; i++)
            {
                var vk = voices[i];
                var vf = PhraseFilterManager.GetOrCreateVoice(PhraseFilterManager.CurrentChannel, vk);
                string label = FormatToggle(vf.Enabled, GetVoiceDisplayName(vk));

                var btn = InstantiateButton(_voiceBtnTpl, _voiceContent, label, 8f);
                _voiceButtons[vk] = btn;

                var captured = vk;
                btn.onClick.AddListener(delegate { OnSelectVoice(captured); });

                // 初始高亮
                if (!string.IsNullOrEmpty(_currentVoiceKey) &&
                    string.Equals(_currentVoiceKey, vk, StringComparison.OrdinalIgnoreCase))
                {
                    SetButtonBg(btn, VoiceRowSelected);
                }
                else
                {
                    SetButtonBg(btn, VoiceRowNormal);
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_voiceContent);
            if (_voiceScroll != null) _voiceScroll.verticalNormalizedPosition = 1f;
            Canvas.ForceUpdateCanvases();
        }

        private void OnSelectVoice(string voiceKey)
        {
            _currentVoiceKey = voiceKey;
            SetVoiceSelectionVisual(voiceKey);
            UpdateTitle();
            RefreshLinesForVoice(voiceKey);
        }

        private void UpdateTitle()
        {
            if (_title == null) return;
            if (!string.IsNullOrEmpty(_currentVoiceKey))
                _title.text = "台词显示控制面板 - " + GetVoiceDisplayName(_currentVoiceKey);
            else
                _title.text = "台词显示控制面板";
        }

        private void SetVoiceSelectionVisual(string voiceKey)
        {
            foreach (var kv in _voiceButtons)
            {
                bool sel = string.Equals(kv.Key, voiceKey, StringComparison.OrdinalIgnoreCase);
                SetButtonBg(kv.Value, sel ? VoiceRowSelected : VoiceRowNormal);
            }
        }

        private void UpdateChannelButtonsVisual(string channel)
        {
            SetButtonBg(_btnChSubtitle, string.Equals(channel, "Subtitle", StringComparison.OrdinalIgnoreCase) ? ChannelSelected : ChannelNormal);
            SetButtonBg(_btnChDanmaku, string.Equals(channel, "Danmaku", StringComparison.OrdinalIgnoreCase) ? ChannelSelected : ChannelNormal);
            SetButtonBg(_btnChWorld3D, string.Equals(channel, "World3D", StringComparison.OrdinalIgnoreCase) ? ChannelSelected : ChannelNormal);
        }

        private static void SetButtonBg(Button btn, Color c)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = c;
        }

        private void RefreshLinesForVoice(string voiceKey)
        {
            ClearChildren(_lineContent);
            if (string.IsNullOrEmpty(voiceKey)) return;

            var vf = PhraseFilterManager.GetOrCreateVoice(PhraseFilterManager.CurrentChannel, voiceKey);

            var voiceToggle = InstantiateButton(_lineBtnTpl, _lineContent, FormatToggle(vf.Enabled, "声线启用"), 12f);
            AttachScrollHandlers(voiceToggle.gameObject, _lineScroll);
            voiceToggle.onClick.AddListener(delegate {
                vf.Enabled = !vf.Enabled;
                voiceToggle.GetComponentInChildren<Text>(true).text = FormatToggle(vf.Enabled, "声线启用");

                // 左侧同步刷新 + 保持选中  
                RefreshVoiceList(true);
                SetVoiceSelectionVisual(_currentVoiceKey);
            });

            var map = PhraseFilterManager.LoadVoiceTriggerNetIds(voiceKey);
            if (map.Count == 0)
            {
                AddInfoRow(_lineContent, "未找到该声线的触发器。");
                return;
            }

            foreach (var kv in map)
            {
                var trigger = kv.Key;
                var tf = PhraseFilterManager.GetOrCreateTrigger(PhraseFilterManager.CurrentChannel, voiceKey, trigger);

                string expKey = PhraseFilterManager.CurrentChannel + "|" + voiceKey + "|" + trigger;
                bool expanded;
                if (!_triggerExpanded.TryGetValue(expKey, out expanded))
                {
                    expanded = false;
                    _triggerExpanded[expKey] = false;
                }

                var triggerRow = new GameObject("TriggerRow");
                triggerRow.transform.SetParent(_lineContent, false);
                var triggerRt = triggerRow.AddComponent<RectTransform>();
                triggerRt.sizeDelta = new Vector2(100f, 24f);
                var triggerLe = triggerRow.AddComponent<LayoutElement>();
                triggerLe.preferredHeight = 24f;
                triggerLe.minHeight = 24f;
                var triggerLayout = triggerRow.AddComponent<HorizontalLayoutGroup>();
                triggerLayout.childControlHeight = true;
                triggerLayout.childControlWidth = true;
                triggerLayout.childForceExpandHeight = false;
                triggerLayout.childForceExpandWidth = false;
                triggerLayout.spacing = 2f;
                triggerLayout.padding = new RectOffset(0, 0, 0, 0);

                var expandBtn = InstantiateButton(_lineBtnTpl, triggerRt, expanded ? "v" : ">", 0f);
                var expandLe = expandBtn.GetComponent<LayoutElement>();
                if (expandLe != null)
                {
                    expandLe.preferredWidth = 24f;
                    expandLe.minWidth = 24f;
                }
                var expandText = expandBtn.GetComponentInChildren<Text>(true);
                if (expandText != null)
                {
                    expandText.alignment = TextAnchor.MiddleCenter;
                    var er = expandText.rectTransform;
                    er.offsetMin = Vector2.zero;
                    er.offsetMax = Vector2.zero;
                }
                AttachScrollHandlers(expandBtn.gameObject, _lineScroll);
                expandBtn.onClick.AddListener(delegate {
                    _triggerExpanded[expKey] = !_triggerExpanded[expKey];
                    Vector2 prevPos = Vector2.zero;
                    if (_lineScroll != null && _lineScroll.content != null)
                        prevPos = _lineScroll.content.anchoredPosition;
                    RefreshLinesForVoice(voiceKey);
                    if (_lineScroll != null)
                        StartCoroutine(RestoreScrollPositionNextFrame(_lineScroll, prevPos));
                });

                var header = InstantiateButton(_lineBtnTpl, triggerRt, FormatToggle(tf.Enabled, "语音事件: " + FormatTriggerLabel(trigger)), 16f);
                var headerLe = header.GetComponent<LayoutElement>();
                if (headerLe != null) headerLe.flexibleWidth = 1f;
                AttachScrollHandlers(header.gameObject, _lineScroll);
                header.onClick.AddListener(delegate {
                    tf.Enabled = !tf.Enabled;
                    header.GetComponentInChildren<Text>(true).text = FormatToggle(tf.Enabled, "语音事件: " + FormatTriggerLabel(trigger));
                });

                var ids = new List<string>(kv.Value ?? new List<string>());
                if (!ids.Any(x => string.Equals(x, "General", StringComparison.OrdinalIgnoreCase)))
                    ids.Insert(0, "General");
                else
                {
                    ids.RemoveAll(s => string.Equals(s, "General", StringComparison.OrdinalIgnoreCase));
                    ids.Insert(0, "General");
                }
                SortNetIdList(ids);

                if (tf.NetIds == null)
                    tf.NetIds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

                if (tf.GeneralOnly)
                {
                    for (int n = 0; n < ids.Count; n++)
                        tf.NetIds[ids[n]] = false;
                }
                else if (tf.NetIds != null)
                {
                    bool hasNonGeneral = false;
                    for (int n = 0; n < ids.Count; n++)
                    {
                        if (!string.Equals(ids[n], "General", StringComparison.OrdinalIgnoreCase))
                        {
                            hasNonGeneral = true;
                            break;
                        }
                    }

                    if (hasNonGeneral)
                    {
                        bool generalOn = false;
                        if (tf.NetIds.TryGetValue("General", out var genVal) && genVal)
                            generalOn = true;

                        if (generalOn)
                        {
                            for (int n = 0; n < ids.Count; n++)
                            {
                                string nid = ids[n];
                                if (string.Equals(nid, "General", StringComparison.OrdinalIgnoreCase)) continue;
                                tf.NetIds[nid] = false;
                            }
                        }
                        else
                        {
                            tf.NetIds["General"] = false;
                        }
                    }
                }

                var rows = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

                Action refreshRows = delegate
                {
                    for (int r = 0; r < ids.Count; r++)
                    {
                        string rid = ids[r];
                        Button btn;
                        if (!rows.TryGetValue(rid, out btn) || btn == null) continue;
                        bool val = true;
                        if (tf.NetIds != null && tf.NetIds.TryGetValue(rid, out var curVal))
                            val = curVal;
                        btn.GetComponentInChildren<Text>(true).text = FormatToggle(val, GetNetIdLabel(rid));
                    }
                };

                if (!expanded)
                    continue;

                var generalOnlyBtn = InstantiateButton(_lineBtnTpl, _lineContent, FormatToggle(tf.GeneralOnly, "仅使用全局默认台词"), 48f);
                AttachScrollHandlers(generalOnlyBtn.gameObject, _lineScroll);
                generalOnlyBtn.onClick.AddListener(delegate {
                    tf.GeneralOnly = !tf.GeneralOnly;
                    if (tf.GeneralOnly)
                    {
                        BackupNetIds(tf, ids);
                        for (int n = 0; n < ids.Count; n++)
                            tf.NetIds[ids[n]] = false;
                    }
                    else
                    {
                        RestoreNetIds(tf, ids);
                    }
                    generalOnlyBtn.GetComponentInChildren<Text>(true).text = FormatToggle(tf.GeneralOnly, "仅使用全局默认台词");
                    refreshRows();
                });
                AttachTooltip(generalOnlyBtn, delegate {
                    return PhraseFilterManager.GetGlobalLineText(voiceKey, trigger);
                });

                for (int i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    bool enabled = true;
                    if (tf.NetIds != null && tf.NetIds.TryGetValue(id, out var val)) enabled = val;
                    else tf.NetIds[id] = enabled;

                    var row = InstantiateButton(_lineBtnTpl, _lineContent, FormatToggle(enabled, GetNetIdLabel(id)), 64f);
                    rows[id] = row;
                    AttachScrollHandlers(row.gameObject, _lineScroll);

                    var capturedId = id;
                    bool isGeneral = string.Equals(capturedId, "General", StringComparison.OrdinalIgnoreCase);
                    if (isGeneral)
                    {
                        row.onClick.AddListener(delegate {
                            if (tf.GeneralOnly)
                            {
                                tf.GeneralOnly = false;
                                RestoreNetIds(tf, ids);
                                generalOnlyBtn.GetComponentInChildren<Text>(true).text = FormatToggle(false, "仅使用全局默认台词");
                            }
                            bool cur = tf.NetIds.ContainsKey(capturedId) ? tf.NetIds[capturedId] : true;
                            bool next = !cur;

                            if (next)
                            {
                                BackupNetIds(tf, ids);
                                for (int n = 0; n < ids.Count; n++)
                                {
                                    string nid = ids[n];
                                    if (string.Equals(nid, "General", StringComparison.OrdinalIgnoreCase)) continue;
                                    tf.NetIds[nid] = false;
                                }
                            }
                            else
                            {
                                RestoreNetIds(tf, ids);
                            }

                            tf.NetIds[capturedId] = next;
                            refreshRows();
                        });
                    }
                    else
                    {
                        row.onClick.AddListener(delegate {
                            bool restoredFromGeneralOnly = false;
                            if (tf.GeneralOnly)
                            {
                                tf.GeneralOnly = false;
                                RestoreNetIds(tf, ids);
                                generalOnlyBtn.GetComponentInChildren<Text>(true).text = FormatToggle(false, "仅使用全局默认台词");
                                restoredFromGeneralOnly = true;
                            }
                            bool cur = tf.NetIds.ContainsKey(capturedId) ? tf.NetIds[capturedId] : true;
                            bool next = !cur;
                            tf.NetIds[capturedId] = next;

                            bool generalOn = false;
                            if (tf.NetIds.TryGetValue("General", out var genVal))
                                generalOn = genVal;

                            if (next && generalOn)
                            {
                                RestoreNetIds(tf, ids);
                                tf.NetIds["General"] = false;
                                tf.NetIds[capturedId] = true;
                                refreshRows();
                            }
                            else
                            {
                                if (restoredFromGeneralOnly)
                                    refreshRows();
                                else
                                    row.GetComponentInChildren<Text>(true).text = FormatToggle(tf.NetIds[capturedId], GetNetIdLabel(capturedId));
                            }
                        });
                    }
                    AttachTooltip(row, delegate {
                        return PhraseFilterManager.GetVoiceLineText(voiceKey, trigger, capturedId);
                    });
                }}

            LayoutRebuilder.ForceRebuildLayoutImmediate(_lineContent);
        }

        // ---------- UI helpers ----------

        // 用更顺眼的 ●/○ 代替 [?]/[ ]
        private static string FormatToggle(bool enabled, string label)
        {
            return (enabled ? "● " : "○ ") + label;
        }

        private static string GetVoiceDisplayName(string voiceKey)
        {
            if (string.IsNullOrEmpty(voiceKey)) return voiceKey;
            if (string.Equals(voiceKey, PhraseFilterManager.DefaultVoiceKey, StringComparison.OrdinalIgnoreCase))
                return "全局台词";
            if (s_voiceNameMap.TryGetValue(voiceKey, out var mapped) && !string.IsNullOrEmpty(mapped))
                return mapped + " - " + voiceKey;
            return voiceKey;
        }

        private static string GetNetIdLabel(string id)
        {
            if (string.Equals(id, "General", StringComparison.OrdinalIgnoreCase))
                return "默认台词";
            return "NetId: " + id;
        }

        private static void SortNetIdList(List<string> ids)
        {
            if (ids == null || ids.Count <= 1) return;
            bool hasGeneral = ids.Any(s => string.Equals(s, "General", StringComparison.OrdinalIgnoreCase));
            var rest = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (string.Equals(id, "General", StringComparison.OrdinalIgnoreCase)) continue;
                rest.Add(id);
            }
            rest.Sort(CompareNetId);

            ids.Clear();
            if (hasGeneral) ids.Add("General");
            ids.AddRange(rest);
        }

        private static int CompareNetId(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 0;
            int ai, bi;
            bool aNum = int.TryParse(a, out ai);
            bool bNum = int.TryParse(b, out bi);
            if (aNum && bNum) return ai.CompareTo(bi);
            if (aNum != bNum) return aNum ? -1 : 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<string, string> s_voiceNameMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Usec_1", "Michael" },
                { "Usec_2", "Chris" },
                { "Usec_3", "Josh" },
                { "Usec_4", "Brent" },
                { "Usec_5", "Patrick" },
                { "Usec_6", "Charlie" },
                { "Usec_7", "Bob" },
                { "Bear_1", "Alex" },
                { "Bear_2", "Mikhail" },
                { "Bear_3", "Sergei" },
                { "Bear_1_Eng", "Alex" },
                { "Bear_2_Eng", "Sergei" },
                { "Bear_4", "Vitaly" },
            };

        private static readonly Dictionary<string, string> s_triggerNameMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "None", "无" },
                { "Mooing", "哞叫" },
                { "Look", "警戒" },
                { "PhraseNone", "无事件" },
                { "OnAgony", "痛苦死亡  (不推荐开启，否则会导致无法正常显示台词)" },
                { "OnGoodWork", "好配合" },
                { "OnEnemyGrenade", "敌方手雷" },
                { "OnFirstContact", "首次遇敌" },
                { "OnLostVisual", "敌人消失" },
                { "OnFriendlyDown", "队友阵亡" },
                { "OnBeingHurt", "受伤疼叫  (不推荐开启，否则会导致无法正常显示台词)" },
                { "OnBeingHurtDissapoinment", "受伤状态" },
                { "OnEnemyConversation", "发现敌人" },
                { "OnEnemyDown", "击毙敌人" },
                { "OnEnemyShot", "命中敌人" },
                { "OnOutOfAmmo", "缺少弹药" },
                { "OnRepeatedContact", "再次遇敌" },
                { "OnGrenade", "投掷手雷" },
                { "OnWeaponReload", "装填弹药" },
                { "OnWeaponJammed", "武器卡壳" },
                { "OnWeaponMisfired", "武器失火" },
                { "OnDeath", "濒死留言" },
                { "OnFight", "激情对喷" },
                { "OnMutter", "自言自语" },
                { "OnBreath", "呼吸声   (不推荐开启，否则会导致无法正常显示台词)" },
                { "CoverMe", "掩护我" },
                { "FollowMe", "跟随我" },
                { "GetBack", "撤退" },
                { "GoForward", "向前走" },
                { "Gogogo", "冲锋" },
                { "Attention", "警戒" },
                { "HoldPosition", "原地驻守" },
                { "GoLoot", "去搜刮" },
                { "Stop", "停下" },
                { "LocateHostiles", "定位敌人" },
                { "OnSwitchToMeleeWeapon", "切换至近战" },
                { "Silence", "保持安静" },
                { "OnYourOwn", "各自行动" },
                { "Fire", "开火" },
                { "HoldFire", "停火" },
                { "Suppress", "压制" },
                { "Spreadout", "分散" },
                { "GetInCover", "寻找掩体" },
                { "KnifesOnly", "只用刀" },
                { "Regroup", "集合" },
                { "HandBroken", "断手" },
                { "LegBroken", "断腿" },
                { "Bleeding", "流血状态" },
                { "Dehydrated", "脱水状态" },
                { "Exhausted", "饥饿状态" },
                { "HurtLight", "轻微受伤" },
                { "HurtMedium", "中度受伤" },
                { "HurtHeavy", "严重受伤" },
                { "HurtNearDeath", "接近死亡" },
                { "StartHeal", "开始治疗" },
                { "DontKnow", "不知道" },
                { "Clear", "区域安全" },
                { "Going", "离开" },
                { "Covering", "掩护" },
                { "BadWork", "烂配合" },
                { "Negative", "拒绝" },
                { "Ready", "准备" },
                { "OnPosition", "已就位" },
                { "OnLoot", "在搜刮" },
                { "GoodWork", "好配合" },
                { "Roger", "收到" },
                { "Repeat", "请求重复" },
                { "Toxic", "垃圾话" },
                { "Greetings", "问好" },
                { "Warning", "警告" },
                { "Mine", "拌雷" },
                { "LeftFlank", "左侧" },
                { "Scav", "对话Scav" },
                { "SniperPhrase", "狙击手" },
                { "RightFlank", "右侧" },
                { "InTheFront", "前方" },
                { "OnSix", "后方" },
                { "UnderFire", "被压制" },
                { "EnemyDown", "敌人击毙" },
                { "ScavDown", "Scav击毙" },
                { "LostVisual", "敌人消失" },
                { "EnemyHit", "命中敌人" },
                { "KnifeKill", "近战击杀" },
                { "NoisePhrase", "保持安静" },
                { "LowKarmaAttack", "低业力攻击" },
                { "Provocation", "挑衅" },
                { "FriendlyFire", "友伤" },
                { "Rat", "叛徒" },
                { "Down", "击毙" },
                { "Hit", "命中" },
                { "NeedFrag", "需要手雷" },
                { "NeedSniper", "需要狙击掩护" },
                { "NeedAmmo", "需要弹药" },
                { "NeedHelp", "需要帮助" },
                { "NeedWeapon", "需要武器" },
                { "NeedMedkit", "需要医疗" },
                { "ExitLocated", "找到撤离点" },
                { "LootKey", "搜刮到钥匙" },
                { "LockedDoor", "门上锁" },
                { "LootBody", "搜刮尸体" },
                { "LootContainer", "搜刮容器" },
                { "LootGeneric", "正常搜刮" },
                { "LootMoney", "搜刮货币" },
                { "LootWeapon", "搜刮武器" },
                { "Cooperation", "请求合作" },
                { "LootNothing", "搜刮无果" },
                { "WeaponBroken", "武器破损" },
                { "OpenDoor", "打开门" },
                { "CheckHim", "搜身检查" },
                { "MumblePhrase", "含糊低语" }
            };

        private static string FormatTriggerLabel(string trigger)
        {
            if (string.IsNullOrEmpty(trigger)) return trigger;
            if (s_triggerNameMap.TryGetValue(trigger, out var mapped) && !string.IsNullOrEmpty(mapped))
                return trigger + " - " + mapped;
            return trigger;
        }

        private static void BackupNetIds(TriggerFilter tf, List<string> ids)
        {
            if (tf == null || ids == null) return;
            if (tf.NetIds == null)
                tf.NetIds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            tf.NetIdsBackup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (string.Equals(id, "General", StringComparison.OrdinalIgnoreCase)) continue;
                bool val = true;
                if (tf.NetIds.TryGetValue(id, out var existing))
                    val = existing;
                tf.NetIdsBackup[id] = val;
            }
        }

        private static void RestoreNetIds(TriggerFilter tf, List<string> ids)
        {
            if (tf == null || ids == null) return;
            if (tf.NetIds == null)
                tf.NetIds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (tf.NetIdsBackup == null) return;

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (string.Equals(id, "General", StringComparison.OrdinalIgnoreCase)) continue;
                bool val;
                if (tf.NetIdsBackup.TryGetValue(id, out val))
                    tf.NetIds[id] = val;
                else if (!tf.NetIds.ContainsKey(id))
                    tf.NetIds[id] = true;
            }
        }

        private void CreateTooltip(Transform parent)
        {
            _tooltipGo = new GameObject("Tooltip");
            _tooltipGo.transform.SetParent(parent, false);
            _tooltipRt = _tooltipGo.AddComponent<RectTransform>();
            _tooltipRt.pivot = new Vector2(0f, 1f);
            _tooltipRt.sizeDelta = new Vector2(360f, 100f);

            var bg = _tooltipGo.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.85f);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_tooltipGo.transform, false);
            _tooltipText = textGo.AddComponent<Text>();
            _tooltipText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _tooltipText.fontSize = 14;
            _tooltipText.color = Color.white;
            _tooltipText.alignment = TextAnchor.UpperLeft;
            _tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tooltipText.verticalOverflow = VerticalWrapMode.Overflow;

            var textRt = _tooltipText.rectTransform;
            textRt.anchorMin = new Vector2(0f, 0f);
            textRt.anchorMax = new Vector2(1f, 1f);
            textRt.offsetMin = new Vector2(8f, 6f);
            textRt.offsetMax = new Vector2(-8f, -6f);

            _tooltipGo.SetActive(false);
        }

        private void AttachTooltip(Button btn, System.Func<string> textProvider)
        {
            if (btn == null || textProvider == null) return;
            var trigger = btn.gameObject.GetComponent<EventTrigger>();
            if (trigger == null) trigger = btn.gameObject.AddComponent<EventTrigger>();
            if (trigger.triggers == null) trigger.triggers = new List<EventTrigger.Entry>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(delegate { ShowTooltip(textProvider()); });
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(delegate { HideTooltip(); });
            trigger.triggers.Add(exit);
        }

        private static void AttachScrollHandlers(GameObject go, ScrollRect scroll)
        {
            if (go == null || scroll == null) return;
            var trigger = go.GetComponent<EventTrigger>();
            if (trigger == null) trigger = go.AddComponent<EventTrigger>();
            if (trigger.triggers == null) trigger.triggers = new List<EventTrigger.Entry>();

            AddScrollEvent(trigger, EventTriggerType.BeginDrag, delegate(BaseEventData d) {
                var ped = d as PointerEventData;
                if (ped != null) scroll.OnBeginDrag(ped);
            });
            AddScrollEvent(trigger, EventTriggerType.Drag, delegate(BaseEventData d) {
                var ped = d as PointerEventData;
                if (ped != null) scroll.OnDrag(ped);
            });
            AddScrollEvent(trigger, EventTriggerType.EndDrag, delegate(BaseEventData d) {
                var ped = d as PointerEventData;
                if (ped != null) scroll.OnEndDrag(ped);
            });
            AddScrollEvent(trigger, EventTriggerType.Scroll, delegate(BaseEventData d) {
                var ped = d as PointerEventData;
                if (ped != null) scroll.OnScroll(ped);
            });
        }

        private static void AddScrollEvent(EventTrigger trigger, EventTriggerType type, System.Action<BaseEventData> handler)
        {
            if (trigger == null || handler == null) return;
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => handler(data));
            trigger.triggers.Add(entry);
        }

        private IEnumerator RestoreScrollPositionNextFrame(ScrollRect scroll, Vector2 prevContentPos)
        {
            yield return null;
            if (scroll == null || scroll.content == null) yield break;
            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();
            scroll.content.anchoredPosition = ClampContentPosition(scroll, prevContentPos);
        }

        private static Vector2 ClampContentPosition(ScrollRect scroll, Vector2 pos)
        {
            if (scroll == null || scroll.content == null || scroll.viewport == null)
                return pos;

            float contentHeight = scroll.content.rect.height;
            float viewHeight = scroll.viewport.rect.height;
            float maxY = Mathf.Max(0f, contentHeight - viewHeight);
            float y = Mathf.Clamp(pos.y, 0f, maxY);
            return new Vector2(pos.x, y);
        }

        private void ShowTooltip(string text)
        {
            if (_tooltipGo == null || _tooltipText == null || _tooltipRt == null) return;
            string display = string.IsNullOrEmpty(text) ? "（空）" : text;
            _tooltipText.text = display;
            _tooltipRt.sizeDelta = new Vector2(360f, Mathf.Clamp(_tooltipText.preferredHeight + 12f, 40f, 320f));
            _tooltipGo.SetActive(true);
            UpdateTooltipPosition();
        }

        private void HideTooltip()
        {
            if (_tooltipGo != null) _tooltipGo.SetActive(false);
        }

        private void UpdateTooltipPosition()
        {
            if (_tooltipRt == null) return;
            Vector3 pos = Input.mousePosition;
            _tooltipRt.position = pos + new Vector3(16f, -16f, 0f);
        }
private static void ClearChildren(RectTransform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        private static void AddInfoRow(RectTransform parent, string text)
        {
            var row = new GameObject("Info");
            row.transform.SetParent(parent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 24f);

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 24f;
            le.minHeight = 24f;

            var t = row.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = text;
            t.fontSize = 13;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
        }

        private static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Text CreateText(RectTransform parent, string name, string text, int size, TextAnchor align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.text = text;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = align;

            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            return t;
        }

        private static Button CreateButton(RectTransform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = ChannelNormal;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            colors.pressedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            btn.colors = colors;

            var txt = CreateText(rt, "Label", label, 13, TextAnchor.MiddleCenter);
            txt.color = Color.white;

            // 加个描边，防止暗背景“看不见”
            var outline = txt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);

            return btn;
        }

        private static Button CreateFlatButtonTemplate(Transform parent, string name)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 24f);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 24f;
            le.minHeight = 24f;

            var img = go.AddComponent<Image>();
            img.color = VoiceRowNormal;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.38f, 0.38f, 0.38f, 1f);
            colors.pressedColor = new Color(0.20f, 0.20f, 0.20f, 1f);
            btn.colors = colors;

            var txt = CreateText(rt, "Label", "", 13, TextAnchor.MiddleLeft);
            txt.color = Color.white;
            txt.rectTransform.offsetMin = new Vector2(6f, 0f);
            txt.rectTransform.offsetMax = new Vector2(-6f, 0f);

            var outline = txt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);

            go.SetActive(false);
            return btn;
        }

        private static Button InstantiateButton(Button tpl, RectTransform parent, string text, float leftPadding)
        {
            var btn = Instantiate(tpl, parent);
            btn.gameObject.SetActive(true);

            // 注意：true = 包含 inactive 子物体
            var lbl = btn.GetComponentInChildren<Text>(true);
            if (lbl != null)
            {
                lbl.text = text;
                lbl.enabled = true;
                if (lbl.font == null) lbl.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                lbl.color = Color.white;

                var rt = lbl.rectTransform;
                rt.offsetMin = new Vector2(leftPadding, 0f);
                rt.offsetMax = new Vector2(-6f, 0f);
            }

            return btn;
        }

        // 你当前已经换成 RectMask2D（保留）
        private static void MakeScrollWithContent(RectTransform parent, out ScrollRect scroll, out RectTransform content)
        {
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(parent, false);

            var rt = scrollGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(6f, 6f);
            rt.offsetMax = new Vector2(-6f, -6f);

            var img = scrollGo.AddComponent<Image>();
            img.color = new Color(0.08f, 0.08f, 0.08f, 1f);

            scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;
            scroll.inertia = true;

            const float scrollbarWidth = 12f;
            const float scrollbarPadding = 2f;

            var scrollbarGo = new GameObject("Scrollbar");
            scrollbarGo.transform.SetParent(scrollGo.transform, false);
            var sbRt = scrollbarGo.AddComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 1f);
            sbRt.sizeDelta = new Vector2(scrollbarWidth, 0f);
            sbRt.anchoredPosition = new Vector2(-scrollbarPadding, 0f);

            var sbBg = scrollbarGo.AddComponent<Image>();
            sbBg.color = new Color(0.18f, 0.18f, 0.18f, 1f);

            var sb = scrollbarGo.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(scrollbarGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.anchorMin = new Vector2(0f, 0f);
            handleRt.anchorMax = new Vector2(1f, 1f);
            handleRt.offsetMin = new Vector2(2f, 2f);
            handleRt.offsetMax = new Vector2(-2f, -2f);

            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(0.55f, 0.55f, 0.55f, 1f);

            sb.handleRect = handleRt;
            sb.targetGraphic = handleImg;

            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing = scrollbarPadding;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGo.transform, false);

            var vRt = viewport.AddComponent<RectTransform>();
            vRt.anchorMin = new Vector2(0f, 0f);
            vRt.anchorMax = new Vector2(1f, 1f);
            vRt.offsetMin = Vector2.zero;
            vRt.offsetMax = new Vector2(-(scrollbarWidth + scrollbarPadding), 0f);

            var vImg = viewport.AddComponent<Image>();
            vImg.color = new Color(0f, 0f, 0f, 0.001f);

            viewport.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewport.transform, false);

            content = contentGo.AddComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(4, 4, 4, 4);

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.viewport = vRt;
            scroll.content = content;
        }
    }
}









