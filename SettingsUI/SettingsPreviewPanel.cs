using System;
using BepInEx.Configuration;
using Subtitle.Config;
using Subtitle.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Subtitle.SettingsUI
{
    /// <summary>
    /// 设置窗口右栏的实时预览（第二阶段）：用普通 uGUI Text 复刻三个字幕通道（底部字幕/弹幕/3D气泡）的效果。
    /// 样式全部走 Settings 里与真实渲染相同的公开入口（Apply*TextOverrides / GetRoleColor / GetTextColor /
    /// WrapRoleTag / BuildSubtitleLayoutSpec / BuildSubtitleBackgroundSpec），预览因此天然与局内一致；
    /// 只有少量测量/强制换行/背景盒计算在 SubtitleComponent、SubtitleWorld3D 里是 private，
    /// 这里按原逻辑等价复制（不改动原文件可见性）。不经过 SubtitleManager，不生成真实字幕。
    /// 实时刷新：OnEnable 订阅 Settings.Config.SettingChanged，任何设置写入都会重排样例；OnDisable/OnDestroy 退订。
    /// 预览不体现：屏幕锚点/偏移/安全区等纯屏幕定位项（面板内无从对应），弹幕的滚动运动也不体现；
    /// 字幕超长滚动（marquee）不演示动画，不换行且超限时以“截断+省略号”静态样例呈现（盒宽与真实渲染一致）。
    /// 底部控制行：可循环切换角色类别（驱动三个样例的标签/颜色）、输入自定义台词（留空回退默认样例），
    /// 「随机」按钮复用 Settings.TryPickRandomPreviewLine 挑一条真实语音行并联动角色类别。
    /// 面板根部带 RectMask2D（由 SettingsWindow 添加），所有样例都被裁剪在面板内。
    /// </summary>
    internal sealed class SettingsPreviewPanel : MonoBehaviour
    {
        private RectTransform _pane;

        // —— 预览状态：选中的角色类别 / 自定义台词 / 最近一次随机台词（RefreshAll 统一读取） ——
        private Settings.RoleKind _kind = Settings.RoleKind.PmcUsec;
        private string _customText = "";
        private string _lastRandomLine;

        // 面板宽度监视：窗口被拖拽缩放后自动重排样例（SettingChanged 之外唯一的尺寸变化来源）
        private float _lastPaneW = -1f;

        // —— 底部控制行 ——
        private Text _kindBtnLabel;

        // —— 字幕样例 ——
        private RectTransform _subStage;
        private RectTransform _subRowRt;
        private Text _subText;
        private Image _subBg; // 懒创建（对应真实实现里的 "BG" 子节点）

        // —— 弹幕样例 ——
        private RectTransform _dmStage;
        private Text _dmText;

        // —— 3D气泡样例 ——
        private RectTransform _w3dStage;
        private RectTransform _w3dBubbleRt;
        private Image _w3dBg;
        private RectTransform _w3dTextRt;
        private Text _w3dText;

        private bool _subscribed;

        private static readonly Color CaptionColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        private static readonly Color StageColor = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Color NoteColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        // 与 SubtitleComponent.ApplyRowLayoutAndBackground 保持一致的默认盒宽比
        private const float DefaultMaxWidthPercent = 0.9f;
        // 与 SubtitleWorld3D 的私有常量一致（气泡最大宽 / 内边距）
        private const float World3DMaxWidth = 420f;
        private const float World3DPaddingX = 14f;
        private const float World3DPaddingY = 8f;

        // ---------- 生命周期 ----------

        private void Awake()
        {
            _pane = (RectTransform)transform;
            BuildSamples();
            BuildNote();
            BuildControlRow();
        }

        private void Update()
        {
            // 窗口拖拽缩放时面板宽度变化：自动重排（与 SettingChanged 刷新并列的唯一入口）
            if (_pane == null) return;
            float w = _pane.rect.width;
            if (Mathf.Abs(w - _lastPaneW) > 1f) RefreshAll();
        }

        private void OnEnable()
        {
            Subscribe();
            RefreshAll();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        // ---------- 实时刷新订阅 ----------

        private void Subscribe()
        {
            if (_subscribed) return;
            var cfg = Settings.Config; // Settings.Init(Config) 里保存的插件 ConfigFile
            if (cfg == null) return;
            cfg.SettingChanged += OnSettingChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            var cfg = Settings.Config;
            if (cfg != null) cfg.SettingChanged -= OnSettingChanged;
            _subscribed = false;
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            RefreshAll();
        }

        // ---------- 一次性构建（之后只改文本/样式/尺寸，不重建 GameObject） ----------

        private void BuildSamples()
        {
            // 字幕：舞台 → 行（居中）→ BG(懒) + Text
            CreateCaption(_pane, "SubCaption", I18n.Text("Preview.CaptionSubtitle", "字幕"));
            _subStage = CreateStage(_pane, "SubStage");
            _subRowRt = CreateCenteredChild(_subStage, "SubRow");
            _subText = CreateSampleText(_subRowRt, "Text");
            _subText.supportRichText = true;
            _subText.verticalOverflow = VerticalWrapMode.Overflow; // 与真实渲染一致：不硬截断台词

            // 弹幕：舞台 → 单条静态文本（真实弹幕从右往左滚动，预览只做静态样式样例）
            CreateCaption(_pane, "DmCaption", I18n.Text("Preview.CaptionDanmaku", "弹幕"));
            _dmStage = CreateStage(_pane, "DmStage");
            _dmText = CreateSampleText(_dmStage, "DmText");
            _dmText.supportRichText = true;
            _dmText.alignment = TextAnchor.MiddleLeft; // 与 SubtitleDanmaku 一致
            _dmText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _dmText.verticalOverflow = VerticalWrapMode.Overflow;

            // 3D气泡：舞台 → 气泡(背景 Image) → Text（World3D 的 2D 近似）
            CreateCaption(_pane, "W3dCaption", I18n.Text("Preview.CaptionWorld3D", "3D气泡"));
            _w3dStage = CreateStage(_pane, "W3dStage");
            _w3dBubbleRt = CreateCenteredChild(_w3dStage, "Bubble");
            _w3dBg = _w3dBubbleRt.gameObject.AddComponent<Image>();
            _w3dBg.raycastTarget = false;
            _w3dTextRt = CreateCenteredChild(_w3dBubbleRt, "Text");
            _w3dText = _w3dTextRt.gameObject.AddComponent<Text>();
            _w3dText.font = UiWidgets.DefaultFont;
            _w3dText.supportRichText = true;
            _w3dText.verticalOverflow = VerticalWrapMode.Overflow; // 与 SubtitleWorld3D 一致
        }

        private void BuildNote()
        {
            var note = UiWidgets.CreateText(_pane, "Note",
                I18n.Text("Preview.Note", "注：锚点/偏移/安全区等屏幕定位项不在预览中体现；样例文本含敏感词，用于演示主播模式打码。"),
                11, TextAnchor.UpperLeft, Vector2.zero, Vector2.zero);
            note.color = NoteColor;
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            note.verticalOverflow = VerticalWrapMode.Truncate;
            var rt = note.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 30f);
            rt.anchoredPosition = Vector2.zero;
        }

        // ---------- 底部控制行：角色循环按钮 + 自定义台词输入框 + 随机按钮 ----------

        // 角色类别循环顺序：与颜色查找表的键集合一致（不含 Unknown）
        private static readonly Settings.RoleKind[] kCycleKinds =
        {
            Settings.RoleKind.Player, Settings.RoleKind.Teammate,
            Settings.RoleKind.PmcBear, Settings.RoleKind.PmcUsec,
            Settings.RoleKind.Scav, Settings.RoleKind.Raider, Settings.RoleKind.Rogue, Settings.RoleKind.Cultist,
            Settings.RoleKind.BossFollower, Settings.RoleKind.Zombie, Settings.RoleKind.Goons, Settings.RoleKind.Bosses
        };

        private void BuildControlRow()
        {
            // 固定在面板底部、注释条（0~30px）之上；锚点拉伸，随面板宽度自适应
            var row = UiWidgets.CreateRect(_pane, "ControlRow",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 32f), new Vector2(0f, 58f));

            var lblRole = UiWidgets.CreateText(row, "RoleLabel", I18n.Text("Preview.Role", "角色"), 12, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            lblRole.color = CaptionColor;
            PlaceFixed(lblRole.rectTransform, 2f, 28f);

            // 角色循环按钮：点击切到下一类别，驱动三个样例的标签与颜色
            var kindBtn = UiWidgets.CreateButton(row, "KindBtn", KindDisplayName(_kind),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Color(0.25f, 0.25f, 0.25f, 1f), 12, true);
            var kindBtnRt = kindBtn.GetComponent<RectTransform>();
            kindBtnRt.pivot = new Vector2(0f, 0.5f);
            kindBtnRt.sizeDelta = new Vector2(104f, 22f);
            kindBtnRt.anchoredPosition = new Vector2(32f, 0f);
            _kindBtnLabel = kindBtn.GetComponentInChildren<Text>();
            kindBtn.onClick.AddListener(delegate
            {
                int idx = System.Array.IndexOf(kCycleKinds, _kind);
                _kind = kCycleKinds[(idx + 1) % kCycleKinds.Length];
                UpdateKindButtonLabel();
                RefreshAll();
            });

            var lblLine = UiWidgets.CreateText(row, "LineLabel", I18n.Text("Preview.Line", "台词"), 12, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
            lblLine.color = CaptionColor;
            PlaceFixed(lblLine.rectTransform, 140f, 28f);

            // 自定义台词输入框：留空 = 回退默认样例（默认样例含“操”，用于演示主播模式打码）
            var inputGo = new GameObject("LineInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            inputGo.transform.SetParent(row, false);
            var inputRt = (RectTransform)inputGo.transform;
            inputRt.anchorMin = new Vector2(0f, 0.5f);
            inputRt.anchorMax = new Vector2(1f, 0.5f);
            inputRt.pivot = new Vector2(0.5f, 0.5f);
            inputRt.offsetMin = new Vector2(170f, -11f);
            inputRt.offsetMax = new Vector2(-58f, 11f);
            inputGo.GetComponent<Image>().color = new Color(0.18f, 0.18f, 0.18f, 1f);
            var input = inputGo.GetComponent<InputField>();

            var ph = UiWidgets.CreateText(inputRt, "Placeholder", I18n.Text("Preview.LinePlaceholder", "留空使用默认样例"), 12, TextAnchor.MiddleLeft,
                new Vector2(6f, 0f), new Vector2(-6f, 0f));
            ph.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            ph.fontStyle = FontStyle.Italic;

            var inputText = UiWidgets.CreateText(inputRt, "Text", "", 12, TextAnchor.MiddleLeft,
                new Vector2(6f, 0f), new Vector2(-6f, 0f));
            inputText.color = Color.white;
            inputText.supportRichText = false; // 输入框里原文显示，不解析富文本标签

            input.textComponent = inputText;
            input.placeholder = ph;
            input.onValueChanged.AddListener(delegate (string v)
            {
                _customText = v;
                RefreshAll();
            });

            // 随机按钮：挑一条真实语音行（走台词过滤），并联动角色类别
            var randBtn = UiWidgets.CreateButton(row, "RandomBtn", I18n.Text("Preview.Random", "随机"),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Color(0.25f, 0.25f, 0.25f, 1f), 12, true);
            var randBtnRt = randBtn.GetComponent<RectTransform>();
            randBtnRt.pivot = new Vector2(1f, 0.5f);
            randBtnRt.sizeDelta = new Vector2(52f, 22f);
            randBtnRt.anchoredPosition = new Vector2(-2f, 0f);
            randBtn.onClick.AddListener(delegate
            {
                string aiType, line;
                Settings.RoleKind picked;
                if (Settings.TryPickRandomPreviewLine("Subtitle", out aiType, out picked, out line))
                {
                    _lastRandomLine = line;
                    _kind = picked; // voiceKey → aiType → RoleKind，与 F12 测试按钮同一归类链路
                    UpdateKindButtonLabel();
                    RefreshAll();
                }
                else
                {
                    Subtitle.Plugin.Log?.LogWarning("[SettingsUI] 预览随机台词：未找到可用的本地台词文件。");
                }
            });
        }

        // 在控制行里放一个左对齐的定宽小控件（锚点都在左中）
        private static void PlaceFixed(RectTransform rt, float x, float width)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, 20f);
            rt.anchoredPosition = new Vector2(x, 0f);
        }

        private void UpdateKindButtonLabel()
        {
            if (_kindBtnLabel != null) _kindBtnLabel.text = KindDisplayName(_kind);
        }

        private static string KindDisplayName(Settings.RoleKind kind)
        {
            string fallback;
            switch (kind)
            {
                case Settings.RoleKind.Player: fallback = "玩家"; break;
                case Settings.RoleKind.Teammate: fallback = "队友"; break;
                case Settings.RoleKind.PmcBear: fallback = "PMC·BEAR"; break;
                case Settings.RoleKind.PmcUsec: fallback = "PMC·USEC"; break;
                case Settings.RoleKind.Scav: fallback = "Scav"; break;
                case Settings.RoleKind.Raider: fallback = "Raider"; break;
                case Settings.RoleKind.Rogue: fallback = "Rogue"; break;
                case Settings.RoleKind.Cultist: fallback = "邪教徒"; break;
                case Settings.RoleKind.BossFollower: fallback = "Boss小弟"; break;
                case Settings.RoleKind.Zombie: fallback = "丧尸"; break;
                case Settings.RoleKind.Goons: fallback = "Goons"; break;
                case Settings.RoleKind.Bosses: fallback = "Boss"; break;
                default: return kind.ToString();
            }
            // 角色类别名走 I18n（Preview.Kind.<枚举名>），语言表缺失时回落上面的中文
            return I18n.Text("Preview.Kind." + kind, fallback);
        }

        // 样例正文：自定义输入 > 最近随机 > 该通道默认样例（StreamerFilter 打码在 ComposePreviewLine 里统一做）
        private string GetSampleLine(string defaultLine)
        {
            if (!string.IsNullOrEmpty(_customText)) return _customText;
            if (!string.IsNullOrEmpty(_lastRandomLine)) return _lastRandomLine;
            return defaultLine;
        }

        // 角色类别 → 预览用的 aiType 键 / 兜底标签 / “显示名字”样例名。
        // Player/Teammate 没有真实 aiType 键，直接以中文标签作键（未命中映射时 GetRoleLabel 原样透传）。
        private static void GetKindSampleInfo(Settings.RoleKind kind, out string aiType, out string fallbackLabel, out string nameForShow)
        {
            switch (kind)
            {
                case Settings.RoleKind.PmcUsec: aiType = "pmcUSEC"; fallbackLabel = "USEC"; nameForShow = "Michael"; return;
                case Settings.RoleKind.PmcBear: aiType = "pmcBEAR"; fallbackLabel = "BEAR"; nameForShow = "Ivan"; return;
                case Settings.RoleKind.Scav: aiType = "assault"; fallbackLabel = "Scav"; nameForShow = "Kisliy_77"; return;
                case Settings.RoleKind.Raider: aiType = "pmcBot"; fallbackLabel = "Raider"; nameForShow = null; return;
                case Settings.RoleKind.Rogue: aiType = "exUsec"; fallbackLabel = "Rogue"; nameForShow = null; return;
                case Settings.RoleKind.Cultist: aiType = "sectantPriest"; fallbackLabel = "邪教徒"; nameForShow = null; return;
                case Settings.RoleKind.BossFollower: aiType = "followerGluharAssault"; fallbackLabel = "Boss小弟"; nameForShow = null; return;
                case Settings.RoleKind.Zombie: aiType = "infectedpmc"; fallbackLabel = "丧尸"; nameForShow = null; return;
                case Settings.RoleKind.Goons: aiType = "bossKnight"; fallbackLabel = "Knight"; nameForShow = null; return;
                case Settings.RoleKind.Bosses: aiType = "bossKilla"; fallbackLabel = "Killa"; nameForShow = null; return;
                case Settings.RoleKind.Player: aiType = "玩家"; fallbackLabel = "玩家"; nameForShow = null; return;
                case Settings.RoleKind.Teammate: aiType = "队友"; fallbackLabel = "队友"; nameForShow = null; return;
                default: aiType = ""; fallbackLabel = "AI"; nameForShow = null; return;
            }
        }

        private static Text CreateCaption(RectTransform parent, string name, string label)
        {
            var t = UiWidgets.CreateText(parent, name, label, 13, TextAnchor.MiddleLeft,
                new Vector2(6f, 0f), new Vector2(-6f, 0f));
            t.color = CaptionColor;
            return t;
        }

        private static RectTransform CreateStage(RectTransform parent, string name)
        {
            var rt = UiWidgets.CreateRect(parent, name,
                new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = StageColor;
            img.raycastTarget = false;
            return rt;
        }

        // 在父节点中央放置一个子节点（尺寸由 Refresh 按内容计算）
        private static RectTransform CreateCenteredChild(RectTransform parent, string name)
        {
            var rt = UiWidgets.CreateRect(parent, name,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        private static Text CreateSampleText(RectTransform parent, string name)
        {
            var rt = CreateCenteredChild(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UiWidgets.DefaultFont;
            return t;
        }

        // ---------- 刷新入口 ----------

        private void RefreshAll()
        {
            if (_pane == null) return;
            float paneW = _pane.rect.width;
            if (paneW < 50f) paneW = 400f; // 首帧布局未完成时的兜底
            _lastPaneW = _pane.rect.width; // 记录真实宽度，供 Update 的宽度监视对比

            float y = 2f;
            try { y = RefreshSubtitle(paneW, y); } catch { }
            y += 10f;
            try { y = RefreshDanmaku(paneW, y); } catch { }
            y += 10f;
            try { RefreshWorld3D(paneW, y); } catch { }
        }

        // 顶对齐竖排：把条带放在距顶部 y 处，返回条带底边的 y
        private static float PlaceStrip(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -y);
            return y + height;
        }

        private float PlaceCaption(string name, float y)
        {
            var tr = _pane.Find(name) as RectTransform;
            if (tr == null) return y;
            return PlaceStrip(tr, y, 20f);
        }

        // ---------- 字幕样例 ----------

        private float RefreshSubtitle(float paneW, float y)
        {
            y = PlaceCaption("SubCaption", y);

            // 1) 组句：角色标签（可被“显示PMC名字”替换）+ 主播模式打码 + 距离后缀，与 SpeakPatch.EmitPhrase 一致
            string aiType, fallbackLabel, nameForShow;
            GetKindSampleInfo(_kind, out aiType, out fallbackLabel, out nameForShow);
            string line = ComposePreviewLine(
                _kind, Settings.Channel.Subtitle,
                aiType, fallbackLabel, nameForShow,
                GetSampleLine("操，接敌！两点钟方向，找掩护！"),
                Settings.SubtitleShowRoleTag, Settings.SubtitleShowPmcName, Settings.SubtitleShowDistance);

            // 2) 样式：与 CreateSubtitleLine 同一入口
            Settings.ApplySubtitleTextOverrides(_subText);
            _subText.color = Settings.GetTextColor(_kind, Settings.Channel.Subtitle);

            bool wrap = Settings.SubtitleWrap != null && Settings.SubtitleWrap.Value;
            int limit = Settings.SubtitleWrapLength != null ? Settings.SubtitleWrapLength.Value : 0;
            // 与 SubtitleComponent.GetSubtitleMaxLineChars 一致
            int cap = Settings.SubtitleMaxLineChars != null ? Mathf.Clamp(Settings.SubtitleMaxLineChars.Value, 0, 200) : 0;
            if (wrap)
            {
                // 与真实渲染一致：换行模式下长度上限充当换行宽度（与原阈值取较小值）
                int eff = limit > 0 ? (cap > 0 ? Mathf.Min(limit, cap) : limit) : cap;
                _subText.text = ApplyWrapBySetting(line, true, eff);
            }
            else if (cap > 0)
            {
                // 预览不演示滚动：不换行且超限时统一按“截断+省略号”静态样例（盒宽与真实渲染一致）
                _subText.text = TruncateVisibleChars(line, cap);
            }
            else
            {
                _subText.text = line;
            }

            // 3) 行盒/背景：ApplyRowLayoutAndBackground 的等价简化（面板内无屏幕锚点，只做居中）
            var layout = Settings.BuildSubtitleLayoutSpec() ?? new SubtitleSystem.TextStyle.LayoutSpec();
            var bgSpec = Settings.BuildSubtitleBackgroundSpec() ?? new SubtitleSystem.TextStyle.BackgroundSpec();

            float maxPct = Mathf.Clamp01((float)layout.maxWidthPercent);
            if (maxPct <= 0f) maxPct = DefaultMaxWidthPercent;
            float maxWidth = Mathf.Max(60f, (paneW - 16f) * maxPct);

            // 与真实渲染一致的两遍测量 + 抗低估余量：
            // 第一遍按换行宽度测宽，第二遍按最终文本宽复测高度（渲染按最终宽度重新换行，行数可能变多）
            float measureCap = maxWidth;
            if (wrap && cap > 0) measureCap = Mathf.Min(maxWidth, cap * _subText.fontSize);
            // 与真实渲染一致：先强制一次真实排版，预热动态字体字形度量（冷字形一次性测量会系统性偏小）
            _subText.rectTransform.sizeDelta = new Vector2(measureCap, 0f);
            Canvas.ForceUpdateCanvases();
            Vector2 pref = MeasurePreferredSize(_subText, measureCap);
            float textW = pref.x + Mathf.Max(2f, Mathf.Ceil(pref.x * 0.05f));
            float textH = Mathf.Max(pref.y, MeasurePreferredSize(_subText, Mathf.Max(1f, textW)).y) + 2f;

            float padX = 0f, padY = 0f;
            if (bgSpec.padding != null && bgSpec.padding.Length >= 2)
            {
                padX = (float)bgSpec.padding[0];
                padY = (float)bgSpec.padding[1];
            }

            // 描边/投影位移计入盒子（对称加入，保持视觉居中；投影另做半值反向平移）
            float extraX = 0f, extraY = 0f, shadowDx = 0f, shadowDy = 0f;
            if (Settings.SubtitleOutlineEnabled != null && Settings.SubtitleOutlineEnabled.Value)
            {
                float dx = Settings.SubtitleOutlineDistX != null ? Settings.SubtitleOutlineDistX.Value : 0f;
                float dy = Settings.SubtitleOutlineDistY != null ? Settings.SubtitleOutlineDistY.Value : 0f;
                extraX = Mathf.Max(extraX, Mathf.Abs(dx));
                extraY = Mathf.Max(extraY, Mathf.Abs(dy));
            }
            if (Settings.SubtitleShadowEnabled != null && Settings.SubtitleShadowEnabled.Value)
            {
                shadowDx = Settings.SubtitleShadowDistX != null ? Settings.SubtitleShadowDistX.Value : 0f;
                shadowDy = Settings.SubtitleShadowDistY != null ? Settings.SubtitleShadowDistY.Value : 0f;
                extraX = Mathf.Max(extraX, Mathf.Abs(shadowDx));
                extraY = Mathf.Max(extraY, Mathf.Abs(shadowDy));
            }

            float boxW, boxH;
            if (string.Equals(bgSpec.fit, "fullRow", StringComparison.OrdinalIgnoreCase))
            {
                boxW = maxWidth;
                boxH = textH + padY * 2f + extraY * 2f;
            }
            else
            {
                boxW = textW + padX * 2f + extraX * 2f;
                boxH = textH + padY * 2f + extraY * 2f;
            }

            // 防溢出：关闭自动换行时首选宽度可能远超面板，把行盒/文本钳制在面板内（视觉余量由 RectMask2D 兜底裁剪）
            float paneLimit = Mathf.Max(60f, paneW - 16f);
            if (boxW > paneLimit) boxW = paneLimit;

            _subRowRt.sizeDelta = new Vector2(boxW, boxH);
            _subRowRt.anchoredPosition = Vector2.zero;

            var txtRt = _subText.rectTransform;
            float txtW = Mathf.Min(textW + extraX * 2f, paneLimit);
            txtRt.sizeDelta = new Vector2(txtW, textH + extraY * 2f);
            txtRt.anchoredPosition = new Vector2(-shadowDx * 0.5f, -shadowDy * 0.5f);

            ApplySubtitleBackground(bgSpec, boxW, boxH);

            float stageH = boxH + 12f;
            PlaceStrip(_subStage, y, stageH);
            return y + stageH;
        }

        // 背景盒：与 ApplyRowLayoutAndBackground 第 8 步一致（懒建 "BG"、颜色/九宫格/背景投影）
        private void ApplySubtitleBackground(SubtitleSystem.TextStyle.BackgroundSpec bgSpec, float boxW, float boxH)
        {
            if (bgSpec != null && bgSpec.enabled)
            {
                if (_subBg == null)
                {
                    var bgGo = new GameObject("BG", typeof(RectTransform), typeof(Image));
                    bgGo.transform.SetParent(_subRowRt, false);
                    bgGo.transform.SetAsFirstSibling(); // 放在 Text 之下
                    _subBg = bgGo.GetComponent<Image>();
                    _subBg.raycastTarget = false;
                    var bgRt = (RectTransform)bgGo.transform;
                    bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 0.5f);
                    bgRt.pivot = new Vector2(0.5f, 0.5f);
                    bgRt.anchoredPosition = Vector2.zero;
                }

                var rt = (RectTransform)_subBg.transform;
                rt.sizeDelta = new Vector2(boxW, boxH);

                Color bcol;
                if (!SubtitleSystem.ColorUtil.TryParseColor(bgSpec.color, out bcol))
                    bcol = new Color(0f, 0f, 0f, 0.35f);
                _subBg.color = bcol;

                if (!string.IsNullOrEmpty(bgSpec.sprite))
                {
                    var sp = Resources.Load<Sprite>(bgSpec.sprite);
                    if (sp != null)
                    {
                        _subBg.sprite = sp;
                        _subBg.type = Image.Type.Sliced;
                    }
                }
                else
                {
                    _subBg.sprite = null;
                    _subBg.type = Image.Type.Simple;
                }

                if (bgSpec.shadow != null && bgSpec.shadow.enabled)
                {
                    var s = _subBg.GetComponent<Shadow>();
                    if (s == null) s = _subBg.gameObject.AddComponent<Shadow>();
                    s.useGraphicAlpha = bgSpec.shadow.useGraphicAlpha;
                    Color sc;
                    if (SubtitleSystem.ColorUtil.TryParseColor(bgSpec.shadow.color, out sc))
                        s.effectColor = sc;
                    if (bgSpec.shadow.distance != null && bgSpec.shadow.distance.Length >= 2)
                        s.effectDistance = new Vector2((float)bgSpec.shadow.distance[0], (float)bgSpec.shadow.distance[1]);
                }
                else
                {
                    var s = _subBg.GetComponent<Shadow>();
                    if (s != null) Destroy(s);
                }
            }
            else if (_subBg != null)
            {
                Destroy(_subBg.gameObject);
                _subBg = null;
            }
        }

        // ---------- 弹幕样例 ----------

        private float RefreshDanmaku(float paneW, float y)
        {
            y = PlaceCaption("DmCaption", y);

            string aiType, fallbackLabel, nameForShow;
            GetKindSampleInfo(_kind, out aiType, out fallbackLabel, out nameForShow);
            string line = ComposePreviewLine(
                _kind, Settings.Channel.Danmaku,
                aiType, fallbackLabel, nameForShow,
                GetSampleLine("有人吗？操，脚步声就在隔壁！"),
                Settings.DanmakuShowRoleTag, Settings.DanmakuShowScavName, Settings.DanmakuShowDistance);

            // 与 SubtitleDanmaku.Spawn 同一入口（对齐/溢出在构建时已按真实实现固定）
            Settings.ApplyDanmakuTextOverrides(_dmText);
            _dmText.color = Settings.GetTextColor(_kind, Settings.Channel.Danmaku);
            _dmText.text = line;

            // Overflow 模式下 extents 不影响测量，给一个足够大的宽度即可；
            // 宽度钳制在面板内（长行超出部分由 RectMask2D 裁剪，与真实弹幕滚出屏幕的观感一致）
            Vector2 pref = MeasurePreferredSize(_dmText, Mathf.Max(200f, paneW));
            float dmMaxW = Mathf.Max(60f, paneW - 16f);
            var rt = _dmText.rectTransform;
            rt.sizeDelta = new Vector2(Mathf.Min(pref.x + 4f, dmMaxW), pref.y + 4f);
            rt.anchoredPosition = Vector2.zero;

            float stageH = pref.y + 14f;
            PlaceStrip(_dmStage, y, stageH);
            return y + stageH;
        }

        // ---------- 3D气泡样例 ----------

        private float RefreshWorld3D(float paneW, float y)
        {
            y = PlaceCaption("W3dCaption", y);

            string aiType, fallbackLabel, nameForShow;
            GetKindSampleInfo(_kind, out aiType, out fallbackLabel, out nameForShow);
            string line = ComposePreviewLine(
                _kind, Settings.Channel.World3D,
                aiType, fallbackLabel, nameForShow,
                GetSampleLine("这片区域是我的地盘，滚！"),
                Settings.World3DShowRoleTag, null, Settings.World3DShowDistance);

            // 与 SubtitleWorld3D 同一入口（字体/对齐/换行/描边/投影）
            Settings.ApplyWorld3DTextOverrides(_w3dText);
            _w3dText.color = Settings.GetTextColor(_kind, Settings.Channel.World3D);

            bool wrap = Settings.World3DWrap != null && Settings.World3DWrap.Value;
            int limit = Settings.World3DWrapLength != null ? Settings.World3DWrapLength.Value : 0;
            _w3dText.text = ApplyWrapBySetting(line, wrap, limit);

            // 气泡盒：text + 2×padding（与 World3DBubble.UpdateLayout 一致）
            float maxWidth = Mathf.Max(60f, Mathf.Min(World3DMaxWidth, paneW - World3DPaddingX * 2f - 16f));
            Vector2 pref = MeasurePreferredSize(_w3dText, maxWidth);
            float textW = Mathf.Max(10f, pref.x);
            float textH = Mathf.Max(10f, pref.y);

            _w3dTextRt.sizeDelta = new Vector2(textW, textH);
            _w3dTextRt.anchoredPosition = Vector2.zero;

            float bubbleW = textW + World3DPaddingX * 2f;
            float bubbleH = textH + World3DPaddingY * 2f;
            // 防溢出：关闭换行时首选宽度可能超过 maxWidth，气泡钳制在面板内（余量由 RectMask2D 裁剪）
            float w3dLimit = Mathf.Max(60f, paneW - 16f);
            if (bubbleW > w3dLimit) bubbleW = w3dLimit;
            _w3dBubbleRt.sizeDelta = new Vector2(bubbleW, bubbleH);
            _w3dBubbleRt.anchoredPosition = Vector2.zero;

            // 背景开关/颜色：与 World3DBubble.ApplyBackground 一致
            bool bgEnabled = Settings.World3DBGEnabled == null || Settings.World3DBGEnabled.Value;
            _w3dBg.enabled = bgEnabled;
            if (bgEnabled && Settings.World3DBGColor != null)
                _w3dBg.color = Settings.World3DBGColor.Value;

            float stageH = bubbleH + 12f;
            PlaceStrip(_w3dStage, y, stageH);
            return y + stageH;
        }

        // ---------- 组句（SpeakPatch.EmitPhrase 的等价简化：角色标签可被名字替换、距离后缀、主播模式打码） ----------

        private static string ComposePreviewLine(Settings.RoleKind kind, Settings.Channel ch,
            string aiType, string fallbackLabel, string nameForShow, string sampleText,
            ConfigEntry<bool> showRole, ConfigEntry<bool> showName, ConfigEntry<bool> showDist)
        {
            string body = Subtitle.Utils.StreamerFilter.Apply(sampleText);
            string result = body;

            if (showRole == null || showRole.Value)
            {
                string tag = (showName != null && showName.Value && !string.IsNullOrEmpty(nameForShow))
                    ? nameForShow
                    : Settings.GetRoleLabel(aiType, fallbackLabel);
                result = Settings.WrapRoleTag(tag + "：", kind, ch) + result;
            }

            if (showDist != null && showDist.Value)
                result += " <b>·</b>42m";

            return result;
        }

        // ---------- 以下为 SubtitleComponent 私有实现的等价复制（避免改动原文件可见性） ----------

        // 对应 ApplyWrapBySetting：开启换行且设了强制长度时按可见字符数断行
        private static string ApplyWrapBySetting(string src, bool wrapEnabled, int limit)
        {
            if (string.IsNullOrEmpty(src) || !wrapEnabled) return src;
            return (limit > 0) ? ForceWrapByLength(src, limit) : src;
        }

        // 对应 ForceWrapByLength：跳过富文本标签，只数可见字符
        private static string ForceWrapByLength(string src, int limit)
        {
            if (string.IsNullOrEmpty(src) || limit <= 0) return src;

            var sb = new System.Text.StringBuilder(src.Length + 16);
            bool inTag = false;
            int count = 0;

            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];

                if (c == '<')
                {
                    inTag = true;
                    sb.Append(c);
                    continue;
                }
                if (inTag)
                {
                    sb.Append(c);
                    if (c == '>') inTag = false;
                    continue;
                }

                sb.Append(c);
                if (c != '\n' && c != '\r')
                {
                    count++;
                    if (count >= limit)
                    {
                        sb.Append('\n');
                        count = 0;
                    }
                }
            }
            return sb.ToString();
        }

        // 对应 SubtitleComponent.TruncateVisibleChars：按可见字符数截断，超限追加“…”（预览滚动模式的静态样例）
        private static string TruncateVisibleChars(string src, int cap)
        {
            if (string.IsNullOrEmpty(src) || cap <= 0) return src;

            bool inTag = false;
            int count = 0;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '<') { inTag = true; continue; }
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (c == '\n' || c == '\r') continue;
                count++;
                if (count > cap) break;
            }
            if (count <= cap) return src;

            int keep = Mathf.Max(1, cap - 1);
            var sb = new System.Text.StringBuilder(src.Length + 1);
            inTag = false;
            count = 0;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '<') { inTag = true; sb.Append(c); continue; }
                if (inTag)
                {
                    sb.Append(c);
                    if (c == '>') inTag = false;
                    continue;
                }
                if (count >= keep) break;
                sb.Append(c);
                if (c != '\n' && c != '\r') count++;
            }
            sb.Append('…');
            return sb.ToString();
        }

        // 对应 MeasurePreferredSize：考虑最大宽度（Wrap 时自动换行）测出首选尺寸
        private static Vector2 MeasurePreferredSize(Text txt, float maxWidth)
        {
            if (txt == null) return Vector2.zero;

            var settings = txt.GetGenerationSettings(new Vector2(maxWidth, 0f));
            float w = txt.cachedTextGeneratorForLayout.GetPreferredWidth(txt.text, settings) / txt.pixelsPerUnit;
            float h = txt.cachedTextGeneratorForLayout.GetPreferredHeight(txt.text, settings) / txt.pixelsPerUnit;
            return new Vector2(Mathf.Ceil(w), Mathf.Ceil(h));
        }
    }
}
