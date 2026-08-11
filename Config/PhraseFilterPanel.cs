using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Subtitle.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Subtitle.Config
{
    public class PhraseFilterPanel : MonoBehaviour
    {
        private static PhraseFilterPanel s_instance;

        private Canvas _canvas;
        private CanvasScaler _canvasScaler;
        private GameObject _panelBg;
        private CanvasGroup _panelCanvasGroup;
        private RectTransform _root;
        private RectTransform _windowRt;

        private Image _channelAccent;
        private Image _rightHeaderBg;
        private Text _currentChannelText;
        private Text _editScopeLabel;
        private Text _voiceHeaderText;
        private Text _rightHeaderText;
        private Text _statusText;
        private InputField _voiceSearch;

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
        private Button _btnScopeCurrent;
        private Button _btnScopeAll;

        private Button _btnApply;
        private Button _btnRefresh;
        private Button _btnClose;
        private Button _btnFilterAll;
        private Button _btnFilterEnabled;
        private Button _btnFilterDisabled;
        private Button _btnExpandAll;
        private Button _btnCollapseAll;
        private Text _applyLabel;
        private Text _refreshLabel;

        // 选择状态
        private string _currentVoiceKey;
        private readonly Dictionary<string, string> _selectedVoiceByChannel =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector2> _voiceScrollPositions =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector2> _lineScrollPositions =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);

        private enum VoiceListFilter
        {
            All,
            Enabled,
            Disabled
        }

        private VoiceListFilter _voiceListFilter = VoiceListFilter.All;
        private bool _editAllChannels;
        private readonly HashSet<string> _dirtyChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _pendingChangeCount;
        private float _refreshArmedAt = -999f;
        private string _temporaryStatus;
        private float _temporaryStatusUntil = -999f;

        // 用于高亮：voiceKey -> button
        private readonly Dictionary<string, Button> _voiceButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

        // 自动刷新（等待资源加载）
        private Coroutine _autoRefresh;
        private int _lastVoiceCount = -1;
        private const float AutoRefreshInterval = 1f;

        // 与新版设置窗口共用的中性底色；频道只使用局部强调色，不改变整窗背景。
        private static readonly Color WindowBg = new Color(0.10f, 0.10f, 0.10f, 0.96f);
        private static readonly Color PanelBg = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color HeaderBg = new Color(0.15f, 0.15f, 0.15f, 1f);
        private static readonly Color ButtonNormal = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color VoiceRowNormal = new Color(0.20f, 0.20f, 0.20f, 1f);
        private static readonly Color VoiceRowDisabled = new Color(0.145f, 0.145f, 0.145f, 1f);
        private static readonly Color VoiceRowSelected = new Color(0.34f, 0.34f, 0.34f, 1f);
        private static readonly Color MutedText = new Color(0.66f, 0.66f, 0.66f, 1f);
        private static readonly Color SubtitleAccent = new Color(0.28f, 0.62f, 0.82f, 1f);
        private static readonly Color DanmakuAccent = new Color(0.82f, 0.59f, 0.24f, 1f);
        private static readonly Color World3DAccent = new Color(0.30f, 0.66f, 0.48f, 1f);

        private static readonly Vector2 MinWindowSize = new Vector2(1000f, 600f);
        private const float ConfirmWindowSec = 3f;
        private const float TooltipMaxWidth = 420f;
        private static readonly Vector2 TooltipOffset = new Vector2(14f, -14f);

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

        public static void ApplyOpacity()
        {
            if (s_instance != null) s_instance.ApplyOpacityInstance();
        }

        public static void ApplyScale()
        {
            if (s_instance != null) s_instance.ApplyScaleInstance();
        }

        public static void RefreshLocalization()
        {
            if (s_instance != null) s_instance.RefreshLocalizationInstance();
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
            SaveCurrentScrollPositions(PhraseFilterManager.CurrentChannel, _currentVoiceKey);
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
            // 高于设置窗口（5002）：从设置页打开时台词面板显示在最上层，关闭后返回设置页。
            _canvas.sortingOrder = 5003;
            _canvas.pixelPerfect = true;
            _canvasScaler = goCanvas.AddComponent<CanvasScaler>();
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
            _canvasScaler.matchWidthOrHeight = 0.5f;
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
            _panelCanvasGroup = _panelBg.AddComponent<CanvasGroup>();

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_panelBg.transform, false);
            _root = panel.AddComponent<RectTransform>();
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(1480f, 820f);
            _root.anchoredPosition = Vector2.zero;
            _windowRt = _root;
            var imgPanel = panel.AddComponent<Image>();
            imgPanel.color = WindowBg;

            // ---------- 标题栏 ----------
            var top = UiWidgets.CreateRect(panel.transform, "TitleBar",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -40f), Vector2.zero);
            var topImg = top.gameObject.AddComponent<Image>();
            topImg.color = HeaderBg;

            _title = UiWidgets.CreateText(top, "Title", I18n.Text("PhraseFilter.Title", "台词过滤面板"), 18,
                TextAnchor.MiddleLeft, new Vector2(12f, 0f), new Vector2(-300f, 0f));
            var titleRT = _title.rectTransform;
            titleRT.anchorMin = new Vector2(0f, 0f);
            titleRT.anchorMax = new Vector2(1f, 1f);

            _btnApply = CreateRightAnchoredButton(top, "Save", I18n.Text("PhraseFilter.Save", "保存"), -280f, -190f, ButtonNormal, 13);
            _btnRefresh = CreateRightAnchoredButton(top, "Refresh", I18n.Text("PhraseFilter.Refresh", "刷新"), -184f, -92f, ButtonNormal, 13);
            _btnClose = CreateRightAnchoredButton(top, "Close", I18n.Text("Close", "关闭"), -86f, -8f, ButtonNormal, 13);
            _applyLabel = _btnApply.GetComponentInChildren<Text>(true);
            _refreshLabel = _btnRefresh.GetComponentInChildren<Text>(true);

            _btnApply.onClick.AddListener(ApplyAndSave);
            _btnRefresh.onClick.AddListener(OnClickRefresh);
            _btnClose.onClick.AddListener(Hide);
            AttachWindowDrag(top.gameObject);

            var accentRt = UiWidgets.CreateRect(panel.transform, "ChannelAccent",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -43f), new Vector2(0f, -40f));
            _channelAccent = accentRt.gameObject.AddComponent<Image>();

            // ---------- 频道工具栏 ----------
            var toolbar = UiWidgets.CreateRect(panel.transform, "ChannelToolbar",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(8f, -89f), new Vector2(-8f, -47f));
            var toolbarImg = toolbar.gameObject.AddComponent<Image>();
            toolbarImg.color = PanelBg;

            _currentChannelText = UiWidgets.CreateText(toolbar, "CurrentChannel", "", 14, TextAnchor.MiddleLeft,
                new Vector2(12f, 0f), new Vector2(-900f, 0f));
            var currentChannelRt = _currentChannelText.rectTransform;
            currentChannelRt.anchorMin = new Vector2(0f, 0f);
            currentChannelRt.anchorMax = new Vector2(0f, 1f);
            currentChannelRt.offsetMin = new Vector2(12f, 0f);
            currentChannelRt.offsetMax = new Vector2(160f, 0f);

            _btnChSubtitle = CreateFixedButton(toolbar, "ChSubtitle", GetChannelDisplayName("Subtitle"), 170f, 294f, ButtonNormal, 13);
            _btnChDanmaku = CreateFixedButton(toolbar, "ChDanmaku", GetChannelDisplayName("Danmaku"), 298f, 422f, ButtonNormal, 13);
            _btnChWorld3D = CreateFixedButton(toolbar, "ChWorld3D", GetChannelDisplayName("World3D"), 426f, 566f, ButtonNormal, 13);

            _btnChSubtitle.onClick.AddListener(delegate { OnClickChannel("Subtitle"); });
            _btnChDanmaku.onClick.AddListener(delegate { OnClickChannel("Danmaku"); });
            _btnChWorld3D.onClick.AddListener(delegate { OnClickChannel("World3D"); });

            _editScopeLabel = UiWidgets.CreateText(toolbar, "EditScopeLabel", I18n.Text("PhraseFilter.EditScope", "编辑范围："), 13, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.zero);
            var scopeLabelRt = _editScopeLabel.rectTransform;
            scopeLabelRt.anchorMin = new Vector2(0f, 0f);
            scopeLabelRt.anchorMax = new Vector2(0f, 1f);
            scopeLabelRt.offsetMin = new Vector2(602f, 0f);
            scopeLabelRt.offsetMax = new Vector2(680f, 0f);
            _btnScopeCurrent = CreateFixedButton(toolbar, "ScopeCurrent", I18n.Text("PhraseFilter.ScopeCurrent", "当前类型"), 684f, 800f, ButtonNormal, 12);
            _btnScopeAll = CreateFixedButton(toolbar, "ScopeAll", I18n.Text("PhraseFilter.ScopeAll", "三种类型"), 804f, 924f, ButtonNormal, 12);
            _btnScopeCurrent.onClick.AddListener(delegate { SetEditScope(false); });
            _btnScopeAll.onClick.AddListener(delegate { SetEditScope(true); });

            // ---------- 状态栏 ----------
            var bottom = UiWidgets.CreateRect(panel.transform, "StatusBar",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(8f, 8f), new Vector2(-8f, 40f));
            var bottomImg = bottom.gameObject.AddComponent<Image>();
            bottomImg.color = HeaderBg;
            _statusText = UiWidgets.CreateText(bottom, "Status", "", 12, TextAnchor.MiddleLeft,
                new Vector2(10f, 0f), new Vector2(-10f, 0f));
            _hint = _statusText;

            // ---------- 左栏：角色与声线 ----------
            var left = UiWidgets.CreateRect(panel.transform, "Left",
                new Vector2(0f, 0f), new Vector2(0.31f, 1f), new Vector2(8f, 44f), new Vector2(-4f, -93f));
            var leftImg = left.gameObject.AddComponent<Image>();
            leftImg.color = PanelBg;

            var leftHead = UiWidgets.CreateRect(left, "LeftHeader",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(2f, -74f), new Vector2(-2f, -2f));
            var leftHeadImg = leftHead.gameObject.AddComponent<Image>();
            leftHeadImg.color = HeaderBg;
            _voiceHeaderText = UiWidgets.CreateText(leftHead, "Title", I18n.Text("PhraseFilter.Voices", "角色与声线"), 14, TextAnchor.MiddleLeft,
                new Vector2(8f, 38f), new Vector2(-8f, 0f));

            _voiceSearch = CreateSearchInput(leftHead, new Vector2(8f, 6f), new Vector2(-184f, 34f));
            _voiceSearch.onValueChanged.AddListener(delegate { RefreshVoiceList(true, true); });
            _btnFilterAll = CreateBottomRightButton(leftHead, "FilterAll", I18n.Text("PhraseFilter.FilterAll", "全部"), -178f, -122f);
            _btnFilterEnabled = CreateBottomRightButton(leftHead, "FilterEnabled", I18n.Text("PhraseFilter.FilterEnabled", "启用"), -118f, -62f);
            _btnFilterDisabled = CreateBottomRightButton(leftHead, "FilterDisabled", I18n.Text("PhraseFilter.FilterDisabled", "停用"), -58f, -4f);
            _btnFilterAll.onClick.AddListener(delegate { SetVoiceListFilter(VoiceListFilter.All); });
            _btnFilterEnabled.onClick.AddListener(delegate { SetVoiceListFilter(VoiceListFilter.Enabled); });
            _btnFilterDisabled.onClick.AddListener(delegate { SetVoiceListFilter(VoiceListFilter.Disabled); });

            var voiceScrollWrap = UiWidgets.CreateRect(left, "VoiceScrollWrap", Vector2.zero, Vector2.one,
                new Vector2(2f, 2f), new Vector2(-2f, -78f));
            UiWidgets.MakeScrollWithContent(voiceScrollWrap, out _voiceScroll, out _voiceContent, true);
            _voiceBtnTpl = UiWidgets.CreateFlatButtonTemplate(panel.transform, "VoiceBtnTpl", 28f, VoiceRowNormal, false,
                new Color(0.38f, 0.38f, 0.38f, 1f), new Color(0.20f, 0.20f, 0.20f, 1f), 13, "", new Vector2(6f, 0f), new Vector2(-6f, 0f), true);

            // ---------- 右栏：事件与台词 ID ----------
            var right = UiWidgets.CreateRect(panel.transform, "Right",
                new Vector2(0.31f, 0f), new Vector2(1f, 1f), new Vector2(4f, 44f), new Vector2(-8f, -93f));
            var rightImg = right.gameObject.AddComponent<Image>();
            rightImg.color = PanelBg;

            var rightHead = UiWidgets.CreateRect(right, "RightHeader",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(2f, -36f), new Vector2(-2f, -2f));
            _rightHeaderBg = rightHead.gameObject.AddComponent<Image>();
            _rightHeaderText = UiWidgets.CreateText(rightHead, "Context", "", 14, TextAnchor.MiddleLeft,
                new Vector2(10f, 0f), new Vector2(-220f, 0f));
            _btnExpandAll = CreateRightAnchoredButton(rightHead, "ExpandAll", I18n.Text("PhraseFilter.ExpandAll", "全部展开"), -208f, -108f, ButtonNormal, 12);
            _btnCollapseAll = CreateRightAnchoredButton(rightHead, "CollapseAll", I18n.Text("PhraseFilter.CollapseAll", "全部收起"), -104f, -4f, ButtonNormal, 12);
            _btnExpandAll.onClick.AddListener(delegate { SetAllTriggersExpanded(true); });
            _btnCollapseAll.onClick.AddListener(delegate { SetAllTriggersExpanded(false); });

            var lineScrollWrap = UiWidgets.CreateRect(right, "LineScrollWrap", Vector2.zero, Vector2.one,
                new Vector2(2f, 2f), new Vector2(-2f, -40f));
            UiWidgets.MakeScrollWithContent(lineScrollWrap, out _lineScroll, out _lineContent, true);
            _lineBtnTpl = UiWidgets.CreateFlatButtonTemplate(panel.transform, "LineBtnTpl", 28f, VoiceRowNormal, false,
                new Color(0.38f, 0.38f, 0.38f, 1f), new Color(0.20f, 0.20f, 0.20f, 1f), 13, "", new Vector2(6f, 0f), new Vector2(-6f, 0f), true);

            BuildResizeHandle(panel.transform);
            CreateTooltip(goCanvas.transform);
            ApplyOpacityInstance();
            ApplyScaleInstance();
            UpdateChannelButtonsVisual(PhraseFilterManager.CurrentChannel);
            UpdateEditScopeButtons();
            UpdateVoiceFilterButtons();
            ShowRightEmptyState(I18n.Text("PhraseFilter.Empty", "从左侧选择角色或声线。"));
            UpdateTitle();
        }

        private void ApplyOpacityInstance()
        {
            if (_panelCanvasGroup == null || Settings.SettingsWindowOpacity == null) return;
            _panelCanvasGroup.alpha = Mathf.Clamp(Settings.SettingsWindowOpacity.Value, 0.2f, 1.0f);
        }

        private void ApplyScaleInstance()
        {
            if (_canvasScaler == null) return;
            float scale = Settings.InterfaceScale == null ? 1f : Mathf.Clamp(Settings.InterfaceScale.Value, 0.75f, 1.30f);
            _canvasScaler.referenceResolution = new Vector2(1920f / scale, 1080f / scale);
            Canvas.ForceUpdateCanvases();
            ClampWindowToScreen();
        }

        private void RefreshLocalizationInstance()
        {
            if (_title != null) _title.text = I18n.Text("PhraseFilter.Title", "台词过滤面板");
            if (_editScopeLabel != null) _editScopeLabel.text = I18n.Text("PhraseFilter.EditScope", "编辑范围：");
            SetButtonLabel(_btnClose, I18n.Text("Close", "关闭"));
            SetButtonLabel(_btnScopeCurrent, I18n.Text("PhraseFilter.ScopeCurrent", "当前类型"));
            SetButtonLabel(_btnScopeAll, I18n.Text("PhraseFilter.ScopeAll", "三种类型"));
            SetButtonLabel(_btnFilterAll, I18n.Text("PhraseFilter.FilterAll", "全部"));
            SetButtonLabel(_btnFilterEnabled, I18n.Text("PhraseFilter.FilterEnabled", "启用"));
            SetButtonLabel(_btnFilterDisabled, I18n.Text("PhraseFilter.FilterDisabled", "停用"));
            SetButtonLabel(_btnExpandAll, I18n.Text("PhraseFilter.ExpandAll", "全部展开"));
            SetButtonLabel(_btnCollapseAll, I18n.Text("PhraseFilter.CollapseAll", "全部收起"));
            if (_voiceSearch != null && _voiceSearch.placeholder != null)
            {
                var placeholder = _voiceSearch.placeholder as Text;
                if (placeholder != null)
                    placeholder.text = I18n.Text("PhraseFilter.SearchPlaceholder", "搜索角色或声线");
            }

            UpdateChannelButtonsVisual(PhraseFilterManager.CurrentChannel);
            if (_panelBg != null && _panelBg.activeSelf)
            {
                RefreshVoiceList(true, false, false);
                if (!string.IsNullOrEmpty(_currentVoiceKey))
                    RefreshLinesForVoice(_currentVoiceKey);
            }
            else
            {
                UpdateTitle();
            }
        }

        private void ClampWindowToScreen()
        {
            if (_windowRt == null || _canvas == null) return;
            float scale = _canvas.scaleFactor <= 0f ? 1f : _canvas.scaleFactor;
            float screenW = Screen.width / scale;
            float screenH = Screen.height / scale;
            Vector2 size = _windowRt.sizeDelta;
            size.x = Mathf.Min(size.x, screenW);
            size.y = Mathf.Min(size.y, screenH);
            _windowRt.sizeDelta = size;

            float maxX = Mathf.Max(0f, (screenW - size.x) * 0.5f);
            float maxY = Mathf.Max(0f, (screenH - size.y) * 0.5f);
            Vector2 pos = _windowRt.anchoredPosition;
            pos.x = Mathf.Clamp(pos.x, -maxX, maxX);
            pos.y = Mathf.Clamp(pos.y, -maxY, maxY);
            _windowRt.anchoredPosition = pos;
        }

        private void Update()
        {
            if (_tooltipGo != null && _tooltipGo.activeSelf)
                UpdateTooltipPosition();

            if (_refreshArmedAt > -900f && Time.unscaledTime - _refreshArmedAt > ConfirmWindowSec)
            {
                _refreshArmedAt = -999f;
                if (_refreshLabel != null) _refreshLabel.text = I18n.Text("PhraseFilter.Refresh", "刷新");
            }

            if (!string.IsNullOrEmpty(_temporaryStatus) && Time.unscaledTime > _temporaryStatusUntil)
            {
                _temporaryStatus = null;
                UpdateStatus();
            }
        }

        private void OnClickRefresh()
        {
            if (_dirtyChannels.Count > 0 && Time.unscaledTime - _refreshArmedAt > ConfirmWindowSec)
            {
                _refreshArmedAt = Time.unscaledTime;
                if (_refreshLabel != null) _refreshLabel.text = I18n.Text("PhraseFilter.RefreshConfirm", "确认刷新？");
                SetTemporaryStatus(I18n.Text("PhraseFilter.RefreshWarning", "再次点击刷新将放弃未保存修改。"), ConfirmWindowSec);
                return;
            }

            _refreshArmedAt = -999f;
            if (_refreshLabel != null) _refreshLabel.text = I18n.Text("PhraseFilter.Refresh", "刷新");
            bool loaded = PhraseFilterManager.TryLoadPreset(PhraseFilterManager.CurrentPresetName);
            if (loaded)
            {
                _dirtyChannels.Clear();
                _pendingChangeCount = 0;
                SetTemporaryStatus(I18n.Text("PhraseFilter.Refreshed", "已从文件重新载入。"), 2f);
            }
            else
            {
                SetTemporaryStatus(I18n.Text("PhraseFilter.RefreshFailed", "重新载入失败，请查看日志。"), 3f);
            }

            UpdateChannelButtonsVisual(PhraseFilterManager.CurrentChannel);
            RefreshVoiceList(true, false, false);

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
            if (string.Equals(channel, PhraseFilterManager.CurrentChannel, StringComparison.OrdinalIgnoreCase)) return;

            string previousChannel = PhraseFilterManager.CurrentChannel;
            SaveCurrentScrollPositions(previousChannel, _currentVoiceKey);
            if (!string.IsNullOrEmpty(_currentVoiceKey))
                _selectedVoiceByChannel[previousChannel] = _currentVoiceKey;

            PhraseFilterManager.SetCurrentChannel(channel);
            string selected;
            if (_selectedVoiceByChannel.TryGetValue(channel, out selected))
                _currentVoiceKey = selected;

            UpdateChannelButtonsVisual(channel);

            RefreshVoiceList(true, false, false);

            if (!string.IsNullOrEmpty(_currentVoiceKey))
            {
                SetVoiceSelectionVisual(_currentVoiceKey);
                RefreshLinesForVoice(_currentVoiceKey);
            }
            UpdateTitle();
        }

        private void SetEditScope(bool allChannels)
        {
            _editAllChannels = allChannels;
            UpdateEditScopeButtons();
            UpdateTitle();
        }

        private void UpdateEditScopeButtons()
        {
            Color accent = GetChannelAccent(PhraseFilterManager.CurrentChannel);
            SetButtonBg(_btnScopeCurrent, !_editAllChannels ? accent : ButtonNormal);
            SetButtonBg(_btnScopeAll, _editAllChannels ? accent : ButtonNormal);
        }

        private void ApplyAndSave()
        {
            var preset = PhraseFilterManager.GetOrCreateCurrent();
            bool saved = PhraseFilterManager.SavePreset(PhraseFilterManager.CurrentPresetName, preset);
            if (saved)
            {
                _dirtyChannels.Clear();
                _pendingChangeCount = 0;
                SetTemporaryStatus(I18n.Text("PhraseFilter.Saved", "修改已保存。"), 2f);
            }
            else
            {
                SetTemporaryStatus(I18n.Text("PhraseFilter.SaveFailed", "保存失败，请查看日志。"), 3f);
            }

            UpdateChannelButtonsVisual(PhraseFilterManager.CurrentChannel);
            RefreshVoiceList(true);
            if (!string.IsNullOrEmpty(_currentVoiceKey))
            {
                SetVoiceSelectionVisual(_currentVoiceKey);
                RefreshLinesForVoice(_currentVoiceKey);
            }
            UpdateTitle();
        }

        private void RefreshVoiceList(bool keepSelection, bool resetScroll = false, bool captureCurrentScroll = true)
        {
            string channel = PhraseFilterManager.CurrentChannel;
            if (captureCurrentScroll && !resetScroll && _voiceScroll != null && _voiceScroll.content != null)
                _voiceScrollPositions[channel] = _voiceScroll.content.anchoredPosition;

            UiWidgets.ClearChildren(_voiceContent);
            _voiceButtons.Clear();

            if (!keepSelection)
                _currentVoiceKey = null;

            var voices = PhraseFilterManager.ListVoiceKeys();
            _lastVoiceCount = voices.Count;

            if (voices.Count == 0)
            {
                UiWidgets.AddInfoRow(_voiceContent, I18n.Text("PhraseFilter.NoVoices", "未加载声线资源，稍后重试。"), true, Color.white);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_voiceContent);
                if (_voiceHeaderText != null)
                    _voiceHeaderText.text = string.Format(I18n.Text("PhraseFilter.VoiceCount", "角色与声线 · {0}/{1}"), 0, 0);
                ShowRightEmptyState(I18n.Text("PhraseFilter.NoVoices", "未加载声线资源，稍后重试。"));
                return;
            }

            // 如果当前已选声线不在列表里，就清空（理论上不会发生）
            if (keepSelection && !string.IsNullOrEmpty(_currentVoiceKey))
            {
                bool exists = voices.Any(v => string.Equals(v, _currentVoiceKey, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    _currentVoiceKey = null;
                    ShowRightEmptyState(I18n.Text("PhraseFilter.Empty", "从左侧选择角色或声线。"));
                }
            }

            string query = _voiceSearch == null ? string.Empty : (_voiceSearch.text ?? string.Empty).Trim();
            int visibleCount = 0;
            for (int i = 0; i < voices.Count; i++)
            {
                var vk = voices[i];
                var vf = PhraseFilterManager.GetOrCreateVoice(PhraseFilterManager.CurrentChannel, vk);
                if (_voiceListFilter == VoiceListFilter.Enabled && !vf.Enabled) continue;
                if (_voiceListFilter == VoiceListFilter.Disabled && vf.Enabled) continue;

                string displayName = GetVoiceDisplayName(vk);
                if (!string.IsNullOrEmpty(query) &&
                    displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    vk.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string label = FormatToggle(vf.Enabled, GetVoiceDisplayName(vk));

                var btn = UiWidgets.InstantiateButton(_voiceBtnTpl, _voiceContent, label, new Vector2(8f, 0f), new Vector2(-6f, 0f), true, 0f);
                _voiceButtons[vk] = btn;
                visibleCount++;

                var captured = vk;
                btn.onClick.AddListener(delegate { OnSelectVoice(captured); });
                ApplyVoiceButtonVisual(btn, vf.Enabled,
                    !string.IsNullOrEmpty(_currentVoiceKey) && string.Equals(_currentVoiceKey, vk, StringComparison.OrdinalIgnoreCase));
            }

            if (visibleCount == 0)
                UiWidgets.AddInfoRow(_voiceContent, I18n.Text("PhraseFilter.NoMatchingVoices", "没有符合当前搜索或筛选条件的声线。"), true, MutedText);

            if (_voiceHeaderText != null)
                _voiceHeaderText.text = string.Format(I18n.Text("PhraseFilter.VoiceCount", "角色与声线 · {0}/{1}"), visibleCount, voices.Count);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_voiceContent);
            Canvas.ForceUpdateCanvases();
            if (_voiceScroll != null)
            {
                if (resetScroll)
                    _voiceScroll.verticalNormalizedPosition = 1f;
                else
                {
                    Vector2 savedPos;
                    if (_voiceScrollPositions.TryGetValue(channel, out savedPos))
                        StartCoroutine(RestoreScrollPositionNextFrame(_voiceScroll, savedPos));
                }
            }
        }

        private void OnSelectVoice(string voiceKey)
        {
            SaveCurrentScrollPositions(PhraseFilterManager.CurrentChannel, _currentVoiceKey);
            _currentVoiceKey = voiceKey;
            _selectedVoiceByChannel[PhraseFilterManager.CurrentChannel] = voiceKey;
            SetVoiceSelectionVisual(voiceKey);
            UpdateTitle();
            RefreshLinesForVoice(voiceKey);
        }

        private void UpdateTitle()
        {
            string channel = PhraseFilterManager.CurrentChannel;
            string channelName = GetChannelDisplayName(channel);
            if (_title != null) _title.text = I18n.Text("PhraseFilter.Title", "台词过滤面板");
            if (_currentChannelText != null)
                _currentChannelText.text = string.Format(I18n.Text("PhraseFilter.CurrentEdit", "当前编辑：{0}"), channelName);
            if (_rightHeaderText != null)
            {
                _rightHeaderText.text = string.IsNullOrEmpty(_currentVoiceKey)
                    ? channelName + " / " + I18n.Text("PhraseFilter.NoSelection", "未选择声线")
                    : channelName + " / " + GetVoiceDisplayName(_currentVoiceKey);
            }
            UpdateStatus();
        }

        private void SetVoiceSelectionVisual(string voiceKey)
        {
            foreach (var kv in _voiceButtons)
            {
                bool sel = string.Equals(kv.Key, voiceKey, StringComparison.OrdinalIgnoreCase);
                var vf = PhraseFilterManager.GetOrCreateVoice(PhraseFilterManager.CurrentChannel, kv.Key);
                ApplyVoiceButtonVisual(kv.Value, vf.Enabled, sel);
            }
        }

        private void UpdateChannelButtonsVisual(string channel)
        {
            Color accent = GetChannelAccent(channel);
            SetButtonBg(_btnChSubtitle, string.Equals(channel, "Subtitle", StringComparison.OrdinalIgnoreCase) ? accent : ButtonNormal);
            SetButtonBg(_btnChDanmaku, string.Equals(channel, "Danmaku", StringComparison.OrdinalIgnoreCase) ? accent : ButtonNormal);
            SetButtonBg(_btnChWorld3D, string.Equals(channel, "World3D", StringComparison.OrdinalIgnoreCase) ? accent : ButtonNormal);
            UpdateEditScopeButtons();
            SetButtonBg(_btnApply, _dirtyChannels.Count > 0 ? accent : ButtonNormal);
            if (_channelAccent != null) _channelAccent.color = accent;
            if (_rightHeaderBg != null) _rightHeaderBg.color = Color.Lerp(HeaderBg, accent, 0.16f);

            SetButtonLabel(_btnChSubtitle, GetChannelDisplayName("Subtitle") + (_dirtyChannels.Contains("Subtitle") ? "  •" : string.Empty));
            SetButtonLabel(_btnChDanmaku, GetChannelDisplayName("Danmaku") + (_dirtyChannels.Contains("Danmaku") ? "  •" : string.Empty));
            SetButtonLabel(_btnChWorld3D, GetChannelDisplayName("World3D") + (_dirtyChannels.Contains("World3D") ? "  •" : string.Empty));
            if (_applyLabel != null)
                _applyLabel.text = I18n.Text("PhraseFilter.Save", "保存") + (_dirtyChannels.Count > 0 ? "  •" : string.Empty);
            UpdateVoiceFilterButtons();
            UpdateTitle();
        }

        private static void SetButtonBg(Button btn, Color c)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = c;
        }

        private void RefreshLinesForVoice(string voiceKey)
        {
            UiWidgets.ClearChildren(_lineContent);
            if (string.IsNullOrEmpty(voiceKey))
            {
                ShowRightEmptyState(I18n.Text("PhraseFilter.Empty", "从左侧选择角色或声线。"));
                return;
            }

            UpdateTitle();

            var vf = PhraseFilterManager.GetOrCreateVoice(PhraseFilterManager.CurrentChannel, voiceKey);

            var voiceToggle = UiWidgets.InstantiateButton(_lineBtnTpl, _lineContent, "", new Vector2(12f, 0f), new Vector2(-6f, 0f), true, 0f);
            StyleToggleButton(voiceToggle, vf.Enabled, I18n.Text("PhraseFilter.VoiceEnabled", "声线启用"), true);
            AttachScrollHandlers(voiceToggle.gameObject, _lineScroll);
            voiceToggle.onClick.AddListener(delegate {
                SetVoiceEnabledForEditScope(voiceKey, !vf.Enabled);
                StyleToggleButton(voiceToggle, vf.Enabled, I18n.Text("PhraseFilter.VoiceEnabled", "声线启用"), true);
                MarkDirty();

                RefreshVoiceList(true);
                SetVoiceSelectionVisual(_currentVoiceKey);
            });

            var map = PhraseFilterManager.LoadVoiceTriggerNetIds(voiceKey);
            if (map.Count == 0)
            {
                UiWidgets.AddInfoRow(_lineContent, I18n.Text("PhraseFilter.NoTriggers", "未找到该声线的触发器。"), true, Color.white);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_lineContent);
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
                triggerRt.sizeDelta = new Vector2(100f, 28f);
                var triggerLe = triggerRow.AddComponent<LayoutElement>();
                triggerLe.preferredHeight = 28f;
                triggerLe.minHeight = 28f;
                var triggerLayout = triggerRow.AddComponent<HorizontalLayoutGroup>();
                triggerLayout.childControlHeight = true;
                triggerLayout.childControlWidth = true;
                triggerLayout.childForceExpandHeight = false;
                triggerLayout.childForceExpandWidth = false;
                triggerLayout.spacing = 2f;
                triggerLayout.padding = new RectOffset(0, 0, 0, 0);

                var expandBtn = UiWidgets.InstantiateButton(_lineBtnTpl, triggerRt, expanded ? "▼" : "▶", new Vector2(0f, 0f), new Vector2(-6f, 0f), true, 0f);
                var expandLe = expandBtn.GetComponent<LayoutElement>();
                if (expandLe != null)
                {
                    expandLe.preferredWidth = 32f;
                    expandLe.minWidth = 32f;
                }
                SetButtonBg(expandBtn, Color.Lerp(HeaderBg, GetChannelAccent(PhraseFilterManager.CurrentChannel), expanded ? 0.28f : 0.08f));
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
                    SaveCurrentScrollPositions(PhraseFilterManager.CurrentChannel, voiceKey);
                    RefreshLinesForVoice(voiceKey);
                    if (_lineScroll != null)
                        StartCoroutine(RestoreScrollPositionNextFrame(_lineScroll, prevPos));
                });

                var triggerLabel = I18n.Text("PhraseFilter.TriggerPrefix", "语音事件：") + FormatTriggerLabel(trigger);
                var header = UiWidgets.InstantiateButton(_lineBtnTpl, triggerRt, "", new Vector2(16f, 0f), new Vector2(-6f, 0f), true, 0f);
                var headerLe = header.GetComponent<LayoutElement>();
                if (headerLe != null) headerLe.flexibleWidth = 1f;
                StyleToggleButton(header, tf.Enabled, triggerLabel, true);
                AttachScrollHandlers(header.gameObject, _lineScroll);
                header.onClick.AddListener(delegate {
                    SetTriggerEnabledForEditScope(voiceKey, trigger, !tf.Enabled);
                    StyleToggleButton(header, tf.Enabled, triggerLabel, true);
                    MarkDirty();
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
                        StyleToggleButton(btn, val, GetNetIdLabel(rid), false);
                    }
                };

                if (!expanded)
                    continue;

                var generalOnlyBtn = UiWidgets.InstantiateButton(_lineBtnTpl, _lineContent, "", new Vector2(48f, 0f), new Vector2(-6f, 0f), true, 0f);
                StyleToggleButton(generalOnlyBtn, tf.GeneralOnly, I18n.Text("PhraseFilter.GeneralOnly", "仅使用全局默认台词"), false);
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
                    SyncTriggerToEditScope(voiceKey, trigger, tf);
                    StyleToggleButton(generalOnlyBtn, tf.GeneralOnly, I18n.Text("PhraseFilter.GeneralOnly", "仅使用全局默认台词"), false);
                    refreshRows();
                    MarkDirty();
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

                    var row = UiWidgets.InstantiateButton(_lineBtnTpl, _lineContent, "", new Vector2(64f, 0f), new Vector2(-6f, 0f), true, 0f);
                    StyleToggleButton(row, enabled, GetNetIdLabel(id), false);
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
                                StyleToggleButton(generalOnlyBtn, false, I18n.Text("PhraseFilter.GeneralOnly", "仅使用全局默认台词"), false);
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
                            SyncTriggerToEditScope(voiceKey, trigger, tf);
                            refreshRows();
                            MarkDirty();
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
                                StyleToggleButton(generalOnlyBtn, false, I18n.Text("PhraseFilter.GeneralOnly", "仅使用全局默认台词"), false);
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
                                    StyleToggleButton(row, tf.NetIds[capturedId], GetNetIdLabel(capturedId), false);
                            }
                            SyncTriggerToEditScope(voiceKey, trigger, tf);
                            MarkDirty();
                        });
                    }
                    AttachTooltip(row, delegate {
                        return PhraseFilterManager.GetVoiceLineText(voiceKey, trigger, capturedId);
                    });
                }}

            LayoutRebuilder.ForceRebuildLayoutImmediate(_lineContent);
            Canvas.ForceUpdateCanvases();
            Vector2 savedLinePos;
            if (_lineScrollPositions.TryGetValue(GetLineScrollKey(PhraseFilterManager.CurrentChannel, voiceKey), out savedLinePos))
                StartCoroutine(RestoreScrollPositionNextFrame(_lineScroll, savedLinePos));
        }

        private Button CreateRightAnchoredButton(RectTransform parent, string name, string label,
            float left, float right, Color color, int fontSize)
        {
            var button = UiWidgets.CreateButton(parent, name, label,
                new Vector2(1f, 0.15f), new Vector2(1f, 0.85f), color, fontSize, true);
            var rt = (RectTransform)button.transform;
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(right, 0f);
            return button;
        }

        private Button CreateFixedButton(RectTransform parent, string name, string label,
            float left, float right, Color color, int fontSize)
        {
            var button = UiWidgets.CreateButton(parent, name, label,
                new Vector2(0f, 0.15f), new Vector2(0f, 0.85f), color, fontSize, true);
            var rt = (RectTransform)button.transform;
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(right, 0f);
            return button;
        }

        private Button CreateBottomRightButton(RectTransform parent, string name, string label, float left, float right)
        {
            var button = UiWidgets.CreateButton(parent, name, label,
                new Vector2(1f, 0f), new Vector2(1f, 0f), ButtonNormal, 11, false);
            var rt = (RectTransform)button.transform;
            rt.offsetMin = new Vector2(left, 6f);
            rt.offsetMax = new Vector2(right, 34f);
            return button;
        }

        private InputField CreateSearchInput(RectTransform parent, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = UiWidgets.CreateRect(parent, "VoiceSearch",
                new Vector2(0f, 0f), new Vector2(1f, 0f), offsetMin, offsetMax);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 1f);

            var input = rt.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 80;

            var text = UiWidgets.CreateText(rt, "Text", "", 12, TextAnchor.MiddleLeft,
                new Vector2(8f, 0f), new Vector2(-8f, 0f));
            text.supportRichText = false;
            input.textComponent = text;

            var placeholder = UiWidgets.CreateText(rt, "Placeholder", I18n.Text("PhraseFilter.SearchPlaceholder", "搜索角色或声线"), 12, TextAnchor.MiddleLeft,
                new Vector2(8f, 0f), new Vector2(-8f, 0f));
            placeholder.color = MutedText;
            input.placeholder = placeholder;

            var colors = input.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            input.colors = colors;
            return input;
        }

        private void SetVoiceListFilter(VoiceListFilter filter)
        {
            if (_voiceListFilter == filter) return;
            _voiceListFilter = filter;
            UpdateVoiceFilterButtons();
            RefreshVoiceList(true, true);
        }

        private void UpdateVoiceFilterButtons()
        {
            Color accent = GetChannelAccent(PhraseFilterManager.CurrentChannel);
            SetButtonBg(_btnFilterAll, _voiceListFilter == VoiceListFilter.All ? accent : ButtonNormal);
            SetButtonBg(_btnFilterEnabled, _voiceListFilter == VoiceListFilter.Enabled ? accent : ButtonNormal);
            SetButtonBg(_btnFilterDisabled, _voiceListFilter == VoiceListFilter.Disabled ? accent : ButtonNormal);
        }

        private void SetAllTriggersExpanded(bool expanded)
        {
            if (string.IsNullOrEmpty(_currentVoiceKey)) return;
            SaveCurrentScrollPositions(PhraseFilterManager.CurrentChannel, _currentVoiceKey);
            var map = PhraseFilterManager.LoadVoiceTriggerNetIds(_currentVoiceKey);
            foreach (var kv in map)
            {
                string key = PhraseFilterManager.CurrentChannel + "|" + _currentVoiceKey + "|" + kv.Key;
                _triggerExpanded[key] = expanded;
            }
            RefreshLinesForVoice(_currentVoiceKey);
        }

        private void SetVoiceEnabledForEditScope(string voiceKey, bool enabled)
        {
            if (_editAllChannels)
            {
                var channels = PhraseFilterManager.Channels;
                for (int i = 0; i < channels.Count; i++)
                    PhraseFilterManager.GetOrCreateVoice(channels[i], voiceKey).Enabled = enabled;
                return;
            }

            PhraseFilterManager.GetOrCreateVoice(PhraseFilterManager.CurrentChannel, voiceKey).Enabled = enabled;
        }

        private void SetTriggerEnabledForEditScope(string voiceKey, string trigger, bool enabled)
        {
            if (_editAllChannels)
            {
                var channels = PhraseFilterManager.Channels;
                for (int i = 0; i < channels.Count; i++)
                    PhraseFilterManager.GetOrCreateTrigger(channels[i], voiceKey, trigger).Enabled = enabled;
                return;
            }

            PhraseFilterManager.GetOrCreateTrigger(PhraseFilterManager.CurrentChannel, voiceKey, trigger).Enabled = enabled;
        }

        // 触发器细项存在互斥和备份状态，联动时按一次完整状态复制，避免三个频道内部状态不一致。
        private void SyncTriggerToEditScope(string voiceKey, string trigger, TriggerFilter source)
        {
            if (!_editAllChannels || source == null) return;
            var channels = PhraseFilterManager.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                if (string.Equals(channels[i], PhraseFilterManager.CurrentChannel, StringComparison.OrdinalIgnoreCase))
                    continue;

                TriggerFilter target = PhraseFilterManager.GetOrCreateTrigger(channels[i], voiceKey, trigger);
                target.Enabled = source.Enabled;
                target.DefaultAllow = source.DefaultAllow;
                target.GeneralOnly = source.GeneralOnly;
                target.NetIds = CloneToggleMap(source.NetIds);
                target.NetIdsBackup = source.NetIdsBackup == null ? null : CloneToggleMap(source.NetIdsBackup);
            }
        }

        private static Dictionary<string, bool> CloneToggleMap(Dictionary<string, bool> source)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return result;
            foreach (var kv in source) result[kv.Key] = kv.Value;
            return result;
        }

        private void MarkDirty()
        {
            string channel = PhraseFilterManager.CurrentChannel;
            if (_editAllChannels)
            {
                var channels = PhraseFilterManager.Channels;
                for (int i = 0; i < channels.Count; i++)
                    _dirtyChannels.Add(channels[i]);
            }
            else if (!string.IsNullOrEmpty(channel))
            {
                _dirtyChannels.Add(channel);
            }
            _pendingChangeCount++;
            _refreshArmedAt = -999f;
            if (_refreshLabel != null) _refreshLabel.text = I18n.Text("PhraseFilter.Refresh", "刷新");
            UpdateChannelButtonsVisual(channel);
        }

        private void SetTemporaryStatus(string text, float duration)
        {
            _temporaryStatus = text;
            _temporaryStatusUntil = Time.unscaledTime + Mathf.Max(0.1f, duration);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_statusText == null) return;
            Color accent = GetChannelAccent(PhraseFilterManager.CurrentChannel);
            _statusText.color = accent;
            if (!string.IsNullOrEmpty(_temporaryStatus) && Time.unscaledTime <= _temporaryStatusUntil)
            {
                _statusText.text = _temporaryStatus;
                return;
            }

            string status = string.Format(I18n.Text("PhraseFilter.CurrentEdit", "当前编辑：{0}"),
                GetChannelDisplayName(PhraseFilterManager.CurrentChannel));
            status += _editAllChannels
                ? "  ·  " + I18n.Text("PhraseFilter.StatusAllChannels", "同步三种类型")
                : "  ·  " + I18n.Text("PhraseFilter.StatusCurrentChannel", "仅当前类型");
            if (!string.IsNullOrEmpty(_currentVoiceKey))
                status += "  ·  " + GetVoiceDisplayName(_currentVoiceKey);
            status += _dirtyChannels.Count > 0
                ? "  ·  " + string.Format(I18n.Text("PhraseFilter.StatusUnsaved", "未保存修改 {0} 项"), _pendingChangeCount)
                : "  ·  " + I18n.Text("PhraseFilter.StatusSaved", "已保存");
            _statusText.text = status;
        }

        private void ApplyVoiceButtonVisual(Button button, bool enabled, bool selected)
        {
            if (button == null) return;
            Color color;
            if (selected)
                color = Color.Lerp(VoiceRowSelected, GetChannelAccent(PhraseFilterManager.CurrentChannel), 0.30f);
            else
                color = enabled ? VoiceRowNormal : VoiceRowDisabled;
            SetButtonBg(button, color);
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.color = enabled || selected ? Color.white : MutedText;
        }

        private void StyleToggleButton(Button button, bool enabled, string label, bool emphasize)
        {
            if (button == null) return;
            Color color = enabled ? VoiceRowNormal : VoiceRowDisabled;
            if (enabled && emphasize)
                color = Color.Lerp(VoiceRowNormal, GetChannelAccent(PhraseFilterManager.CurrentChannel), 0.20f);
            SetButtonBg(button, color);
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = FormatToggle(enabled, label);
                text.color = enabled ? Color.white : MutedText;
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null) text.text = label;
        }

        private static string GetChannelDisplayName(string channel)
        {
            if (string.Equals(channel, "Danmaku", StringComparison.OrdinalIgnoreCase))
                return I18n.Text("PhraseFilter.Channel.Danmaku", "弹幕");
            if (string.Equals(channel, "World3D", StringComparison.OrdinalIgnoreCase))
                return I18n.Text("PhraseFilter.Channel.World3D", "3D气泡");
            return I18n.Text("PhraseFilter.Channel.Subtitle", "字幕");
        }

        private static Color GetChannelAccent(string channel)
        {
            if (string.Equals(channel, "Danmaku", StringComparison.OrdinalIgnoreCase)) return DanmakuAccent;
            if (string.Equals(channel, "World3D", StringComparison.OrdinalIgnoreCase)) return World3DAccent;
            return SubtitleAccent;
        }

        private void ShowRightEmptyState(string message)
        {
            if (_lineContent == null) return;
            UiWidgets.ClearChildren(_lineContent);
            UiWidgets.AddInfoRow(_lineContent, message, true, MutedText);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_lineContent);
            UpdateTitle();
        }

        private void SaveCurrentScrollPositions(string channel, string voiceKey)
        {
            if (string.IsNullOrEmpty(channel)) return;
            if (_voiceScroll != null && _voiceScroll.content != null)
                _voiceScrollPositions[channel] = _voiceScroll.content.anchoredPosition;
            if (!string.IsNullOrEmpty(voiceKey) && _lineScroll != null && _lineScroll.content != null)
                _lineScrollPositions[GetLineScrollKey(channel, voiceKey)] = _lineScroll.content.anchoredPosition;
        }

        private static string GetLineScrollKey(string channel, string voiceKey)
        {
            return (channel ?? string.Empty) + "|" + (voiceKey ?? string.Empty);
        }

        private void AttachWindowDrag(GameObject titleBar)
        {
            if (titleBar == null) return;
            var trigger = titleBar.AddComponent<EventTrigger>();
            var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            drag.callback.AddListener(delegate(BaseEventData data)
            {
                var ped = data as PointerEventData;
                if (ped == null || _windowRt == null || _canvas == null) return;
                float scale = _canvas.scaleFactor <= 0f ? 1f : _canvas.scaleFactor;
                _windowRt.anchoredPosition += ped.delta / scale;
            });
            trigger.triggers.Add(drag);
        }

        private void BuildResizeHandle(Transform parent)
        {
            var grip = UiWidgets.CreateRect(parent, "ResizeGrip",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 2f), new Vector2(-2f, 24f));
            var gripImg = grip.gameObject.AddComponent<Image>();
            gripImg.color = new Color(0.20f, 0.20f, 0.20f, 0.9f);
            var hint = UiWidgets.CreateText(grip, "Hint", "◢", 14, TextAnchor.LowerRight,
                Vector2.zero, new Vector2(-3f, 1f));
            hint.color = MutedText;
            hint.raycastTarget = false;

            var trigger = grip.gameObject.AddComponent<EventTrigger>();
            var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            drag.callback.AddListener(delegate(BaseEventData data)
            {
                var ped = data as PointerEventData;
                if (ped == null || _windowRt == null || _canvas == null) return;
                float scale = _canvas.scaleFactor <= 0f ? 1f : _canvas.scaleFactor;
                Vector2 delta = ped.delta / scale;
                Vector2 maxSize = new Vector2(Screen.width / scale, Screen.height / scale);
                Vector2 size = _windowRt.sizeDelta + new Vector2(delta.x, -delta.y);
                size.x = Mathf.Clamp(size.x, MinWindowSize.x, maxSize.x);
                size.y = Mathf.Clamp(size.y, MinWindowSize.y, maxSize.y);
                _windowRt.sizeDelta = size;
            });
            trigger.triggers.Add(drag);
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
                return I18n.Text("PhraseFilter.DefaultVoice", "全局默认台词");
            if (s_voiceNameMap.TryGetValue(voiceKey, out var mapped) && !string.IsNullOrEmpty(mapped))
                return mapped + " - " + voiceKey;
            return voiceKey;
        }

        private static string GetNetIdLabel(string id)
        {
            if (string.Equals(id, "General", StringComparison.OrdinalIgnoreCase))
                return I18n.Text("PhraseFilter.DefaultLine", "默认台词");
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
                return trigger + " - " + I18n.Text("PhraseFilter.Trigger." + trigger, mapped);
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
            _tooltipRt.anchorMin = Vector2.zero;
            _tooltipRt.anchorMax = Vector2.zero;
            _tooltipRt.pivot = new Vector2(0f, 1f);
            _tooltipRt.sizeDelta = new Vector2(TooltipMaxWidth, 100f);

            var bg = _tooltipGo.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.92f);
            bg.raycastTarget = false;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_tooltipGo.transform, false);
            _tooltipText = textGo.AddComponent<Text>();
            _tooltipText.font = UiWidgets.DefaultFont;
            _tooltipText.fontSize = 14;
            _tooltipText.color = Color.white;
            _tooltipText.alignment = TextAnchor.UpperLeft;
            _tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tooltipText.verticalOverflow = VerticalWrapMode.Overflow;
            _tooltipText.raycastTarget = false;

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
            string display = string.IsNullOrEmpty(text) ? I18n.Text("PhraseFilter.TooltipEmpty", "（空）") : text;
            _tooltipText.text = display;

            var widthSettings = _tooltipText.GetGenerationSettings(new Vector2(TooltipMaxWidth, 0f));
            float preferredWidth = _tooltipText.cachedTextGeneratorForLayout.GetPreferredWidth(display, widthSettings) /
                _tooltipText.pixelsPerUnit;
            float textWidth = Mathf.Min(Mathf.Ceil(preferredWidth), TooltipMaxWidth);
            var heightSettings = _tooltipText.GetGenerationSettings(new Vector2(textWidth, 0f));
            float preferredHeight = _tooltipText.cachedTextGeneratorForLayout.GetPreferredHeight(display, heightSettings) /
                _tooltipText.pixelsPerUnit;
            _tooltipRt.sizeDelta = new Vector2(textWidth + 16f, Mathf.Clamp(Mathf.Ceil(preferredHeight) + 12f, 40f, 360f));
            _tooltipGo.SetActive(true);
            UpdateTooltipPosition();
        }

        private void HideTooltip()
        {
            if (_tooltipGo != null) _tooltipGo.SetActive(false);
        }

        private void UpdateTooltipPosition()
        {
            if (_tooltipRt == null || _canvas == null) return;
            float scale = _canvas.scaleFactor <= 0f ? 1f : _canvas.scaleFactor;
            Vector2 mouse = (Vector2)Input.mousePosition / scale;
            Vector2 size = _tooltipRt.sizeDelta;
            float screenWidth = Screen.width / scale;
            float screenHeight = Screen.height / scale;

            float x = mouse.x + TooltipOffset.x;
            float y = mouse.y + TooltipOffset.y;
            if (x + size.x > screenWidth - 4f)
                x = mouse.x - TooltipOffset.x - size.x;
            if (y - size.y < 4f)
                y = mouse.y - TooltipOffset.y + size.y;

            x = Mathf.Clamp(x, 4f, Mathf.Max(4f, screenWidth - size.x - 4f));
            y = Mathf.Clamp(y, size.y + 4f, Mathf.Max(size.y + 4f, screenHeight - 4f));
            _tooltipRt.anchoredPosition = new Vector2(x, y);
        }
    }
}









