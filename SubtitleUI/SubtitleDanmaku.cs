// Filename: Subtitle.Danmaku.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SubtitleSystem
{
    // 注意：SubtitleManager 在 SubtitleComponent.cs 已被标记为 partial
    public partial class SubtitleManager
    {
        //Debug日志
        private static void DLog(string msg)
        {
            try
            {
                if (!Subtitle.Config.Settings.DanmakuDebugVerbose.Value) return;
                Subtitle.Plugin.Log?.LogInfo(msg);
            }
            catch { }
        }

        // ===== 弹幕层字段 =====
        private RectTransform _danmakuLayer;
        private bool _danmakuInited;

        // 车道（lane）控制
        private class DanmakuLane
        {
            public float lastSpawnTime;
            public float lastTextWidth;
            public float lastSpeed;
        }

        private DanmakuLane[] _lanes;
        private int _laneCount = 8;

        // 运动/间隔参数（会在 ApplyDanmakuSettings 重载）
        private float _speedPxPerSec = 180f;
        private int _minGapPx = 40;
        private int _fontSizeOverride = 0; // 0 表示不覆盖，用预设
        private float _densityMultiplier = 1f;
        private float _danmakuOpacity = 1f;
        private bool _lengthSpeedEnabled;
        private float _lengthSpeedMultiplier = 0.5f;
        private int _lengthSpeedStartChars = 20;
        private int _lengthSpeedStepChars = 20;
        private float _lengthSpeedMaxMultiplier = 4f;
        private float _laneVerticalSpacingPx = 8f;

        // 简单对象池（减少 GC）
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();

        // === 新增：发送队列与节流 ===
        private struct DanmakuItem { public string Text; public Color Color; }
        private readonly Queue<DanmakuItem> _danmakuQueue = new Queue<DanmakuItem>();
        private bool _spawnLoopRunning;
        private float _spawnDelaySec = 0.2f; // 默认 0.2s（可被设置覆盖）
        private WaitForSecondsRealtime _spawnDelayWait = new WaitForSecondsRealtime(0.2f);

        // ===== 初始化弹幕层 =====
        public void InitializeDanmakuLayer()
        {
            if (_danmakuInited) return;
            if (this == null || this.transform == null) return;

            // 创建全屏 RectTransform 容器
            var go = new GameObject("DanmakuLayer", typeof(RectTransform));
            go.transform.SetParent(this.transform, false);
            _danmakuLayer = go.GetComponent<RectTransform>();
            _danmakuLayer.anchorMin = new Vector2(0f, 0f);
            _danmakuLayer.anchorMax = new Vector2(1f, 1f);
            _danmakuLayer.pivot = new Vector2(0.5f, 0.5f);
            _danmakuLayer.sizeDelta = Vector2.zero;

            // 读一次配置（若没有 Settings 则使用默认）
            ApplyDanmakuSettings();

            _lanes = new DanmakuLane[_laneCount];
            for (int i = 0; i < _lanes.Length; i++) _lanes[i] = new DanmakuLane();

            _danmakuInited = true;
            DLog("[Danmaku] Layer inited. lanes=" + _laneCount);
        }

        private float _danmakuTopOffsetPercent = 0.10f;  // 顶部起点，默认 10% 屏高
        private float _danmakuAreaMaxPercent = 0.35f;    // 最大占用高度，默认 35%

        // 供外部（例如 Settings 变更时）调用，实时更新配置
        public void ApplyDanmakuSettings()
        {
            try
            {
                int newLaneCount = Mathf.Max(1, Subtitle.Config.Settings.DanmakuLanes.Value);
                if (_lanes == null || _lanes.Length != newLaneCount)
                {
                    _lanes = new DanmakuLane[newLaneCount];
                    for (int i = 0; i < _lanes.Length; i++) _lanes[i] = new DanmakuLane();
                }
                _laneCount = newLaneCount;
                _speedPxPerSec = Mathf.Max(30f, Subtitle.Config.Settings.DanmakuSpeed.Value);
                _densityMultiplier = Subtitle.Config.Settings.DanmakuDensityMultiplier != null
                    ? Mathf.Clamp(Subtitle.Config.Settings.DanmakuDensityMultiplier.Value, 0.25f, 3f)
                    : 1f;
                _danmakuOpacity = Subtitle.Config.Settings.DanmakuOpacity != null
                    ? Mathf.Clamp(Subtitle.Config.Settings.DanmakuOpacity.Value, 0.1f, 1f)
                    : 1f;
                _lengthSpeedEnabled = Subtitle.Config.Settings.DanmakuLengthSpeedEnabled != null &&
                    Subtitle.Config.Settings.DanmakuLengthSpeedEnabled.Value;
                _lengthSpeedMultiplier = Subtitle.Config.Settings.DanmakuLengthSpeedMultiplier != null
                    ? Mathf.Clamp(Subtitle.Config.Settings.DanmakuLengthSpeedMultiplier.Value, 0f, 2f)
                    : 0.5f;
                _lengthSpeedStartChars = Subtitle.Config.Settings.DanmakuLengthSpeedStartChars != null
                    ? Mathf.Clamp(Subtitle.Config.Settings.DanmakuLengthSpeedStartChars.Value, 0, 200)
                    : 20;
                _lengthSpeedStepChars = Subtitle.Config.Settings.DanmakuLengthSpeedStepChars != null
                    ? Mathf.Clamp(Subtitle.Config.Settings.DanmakuLengthSpeedStepChars.Value, 1, 100)
                    : 20;
                _lengthSpeedMaxMultiplier = Subtitle.Config.Settings.DanmakuLengthSpeedMaxMultiplier != null
                    ? Mathf.Clamp(Subtitle.Config.Settings.DanmakuLengthSpeedMaxMultiplier.Value, 1f, 10f)
                    : 4f;
                _laneVerticalSpacingPx = Subtitle.Config.Settings.DanmakuLaneVerticalSpacingPx != null
                    ? Mathf.Clamp(Subtitle.Config.Settings.DanmakuLaneVerticalSpacingPx.Value, 0f, 50f)
                    : 8f;
                int baseGap = Mathf.Max(0, Subtitle.Config.Settings.DanmakuMinGapPx.Value);
                _minGapPx = Mathf.RoundToInt(baseGap / _densityMultiplier);
                _fontSizeOverride = Mathf.Max(0, Subtitle.Config.Settings.DanmakuFontSize.Value);
                // 发送间隔
                float baseSpawnDelay = Mathf.Clamp(
                    Subtitle.Config.Settings.DanmakuSpawnDelaySec != null 
                    ? Subtitle.Config.Settings.DanmakuSpawnDelaySec.Value
                    : 0.2f, 0f, 1f);
                _spawnDelaySec = baseSpawnDelay / _densityMultiplier;
                _spawnDelayWait = _spawnDelaySec > 0f ? new WaitForSecondsRealtime(_spawnDelaySec) : null;

                _danmakuTopOffsetPercent = Mathf.Clamp01(
                    Subtitle.Config.Settings.DanmakuTopOffsetPercent.Value);
                _danmakuAreaMaxPercent = Mathf.Clamp01(
                    Subtitle.Config.Settings.DanmakuAreaMaxPercent.Value);
                DLog("[Danmaku] ApplySettings lanes=" + _laneCount +
                    " speed=" + _speedPxPerSec +
                    " density=" + _densityMultiplier +
                    " opacity=" + _danmakuOpacity +
                    " minGap=" + _minGapPx +
                    " spawnDelay=" + _spawnDelaySec +
                    " fontOverride=" + _fontSizeOverride +
                    " top%=" + _danmakuTopOffsetPercent +
                    " area%=" + _danmakuAreaMaxPercent);
                ApplyDanmakuOpacityToItems();
            }
            catch { }
        }

        public void RefreshDanmakuStyles()
        {
            if (_danmakuLayer == null) return;
            for (int i = 0; i < _danmakuLayer.childCount; i++)
            {
                var child = _danmakuLayer.GetChild(i);
                var txt = child.GetComponent<Text>();
                if (txt != null) Subtitle.Config.Settings.ApplyDanmakuTextOverrides(txt);
            }
            ApplyDanmakuOpacityToItems();
        }

        private void ApplyDanmakuOpacityToItems()
        {
            if (_danmakuLayer == null) return;
            for (int i = 0; i < _danmakuLayer.childCount; i++)
            {
                var child = _danmakuLayer.GetChild(i);
                var group = child.GetComponent<CanvasGroup>();
                if (group == null) group = child.gameObject.AddComponent<CanvasGroup>();
                group.alpha = _danmakuOpacity;
            }
        }

        // ===== 外部 API：添加一条弹幕 =====
        public void AddDanmaku(string text, Color color)
        {
            if (!_danmakuInited) InitializeDanmakuLayer();
            if (_danmakuLayer == null || string.IsNullOrEmpty(text)) return;
            _danmakuQueue.Enqueue(new DanmakuItem { Text = text, Color = color });
            if (!_spawnLoopRunning) StartCoroutine(CoSpawnLoop());
        }

        // 逐条发送弹幕（有间隔），车道不可用时等待直到可用
        private static readonly WaitForSecondsRealtime _laneBusyWait = new WaitForSecondsRealtime(0.05f); // 车道繁忙时的重试步进（缓存避免分配）

        private IEnumerator CoSpawnLoop()
        {
            _spawnLoopRunning = true;
            while (_danmakuQueue.Count > 0)
            {
                var item = _danmakuQueue.Peek();
                // 尝试生成；若车道暂不可用，等一小会再试，不丢消息
                if (TrySpawnDanmaku(item.Text, item.Color))
                {
                    _danmakuQueue.Dequeue();
                    // 两条弹幕之间留出极短间隔（可在设置改 0.1 或 0.3）
                    if (_spawnDelayWait != null)
                        yield return _spawnDelayWait;
                }
                else
                {
                    yield return _laneBusyWait;
                }
            }
            _spawnLoopRunning = false;
        }

        // ===== 内部：移动协程（右→左） =====
        private IEnumerator CoMoveLeft(GameObject go, float x, float endX, float speedPxPerSec)
        {
            var rt = (RectTransform)go.transform;
            bool first = true;
            while (x > endX)
            {
                x -= speedPxPerSec * Time.unscaledDeltaTime;
                rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
                if (first)
                {
                    DLog("[Danmaku] moving... x=" + x + " y=" + rt.anchoredPosition.y);
                    first = false;
                }
                yield return null;
            }
            DLog("[Danmaku] recycle");
            Recycle(go);
        }

        private int PickLaneGreedy(int laneCountEffective, float incomingSpeed, float parentWidth)
        {
            float now = Time.unscaledTime;
            if (_lanes == null || _lanes.Length == 0) return 0;
            if (laneCountEffective < 1) laneCountEffective = 1;
            if (laneCountEffective > _lanes.Length) laneCountEffective = _lanes.Length;
            // 从 0 开始顺序找——最大化复用低编号车道
                        for (int i = 0; i < laneCountEffective; i++)
                           {
                float previousSpeed = _lanes[i].lastSpeed > 0f ? _lanes[i].lastSpeed : _speedPxPerSec;
                float minInterval;
                if (incomingSpeed > previousSpeed + 0.01f)
                {
                    // 后一条更快时会持续追近前一条；保守等待前一条完全离屏，避免同车道追尾重叠
                    minInterval = (parentWidth + _lanes[i].lastTextWidth + 40f) / Mathf.Max(1f, previousSpeed);
                }
                else
                {
                    minInterval = (_lanes[i].lastTextWidth + _minGapPx) / Mathf.Max(1f, previousSpeed);
                }
                                if (now - _lanes[i].lastSpawnTime >= minInterval) return i;
                            }
            return -1;
        }

        // 真的去生成一条弹幕；成功 true，车道忙 false  
        private bool TrySpawnDanmaku(string text, Color color)
        {
            // 创建/复用
            var go = GetDanmakuItem();
            var rt = (RectTransform)go.transform;
            var txt = go.GetComponent<Text>();
            Subtitle.Config.Settings.ApplyDanmakuTextOverrides(txt);

            // 内容 & 样式
            txt.text = text;
            txt.supportRichText = true;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;



            // ★ 最终颜色：由调用方传入（稍后来自 Settings.DanmakuTextColor）
            txt.color = color;

            // 计算尺寸（preferred* 按需计算，无需强制重建所有 Canvas）
            float textWidth = txt.preferredWidth;
            float textHeight = txt.preferredHeight;
            float itemSpeed = GetDanmakuItemSpeed(text);

            // 父尺寸
            float parentW = ((RectTransform)this.transform).rect.width;
            float parentH = ((RectTransform)this.transform).rect.height;
            if (parentW < 1f || parentH < 1f) { parentW = Screen.width; parentH = Screen.height; }

            // 区域与车道
            float laneH = Mathf.Max(textHeight + _laneVerticalSpacingPx, txt.fontSize + _laneVerticalSpacingPx);
            float topMarginPx = parentH * _danmakuTopOffsetPercent;
            float maxAreaH = Mathf.Max(laneH, parentH * _danmakuAreaMaxPercent);

            int maxByArea = Mathf.Max(1, Mathf.FloorToInt(maxAreaH / laneH));
            int laneCountEffective = Mathf.Min(_laneCount, maxByArea);

            int lane = PickLaneGreedy(laneCountEffective, itemSpeed, parentW);
            if (lane < 0)
            {
                // 车道暂不可用：把对象放回池里，告诉上层“稍后再来”
                Recycle(go);
                return false;
            }

            // Y 位置（从上到下）
            float yTopCenter = (parentH * 0.5f) - topMarginPx - (laneH * 0.5f);
            float y = yTopCenter - lane * laneH;

            // 起止点（右进左出）
            float margin = 20f;
            float startX = +(parentW * 0.5f) + (textWidth * 0.5f) + margin;
            float endX = -(parentW * 0.5f) - (textWidth * 0.5f) - margin;

            // 放置起点
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(startX, y);
            go.SetActive(true);

            // 占用记录
            _lanes[lane].lastSpawnTime = Time.unscaledTime;
            _lanes[lane].lastTextWidth = textWidth;
            _lanes[lane].lastSpeed = itemSpeed;

            // 开始移动
            StartCoroutine(CoMoveLeft(go, startX, endX, itemSpeed));
            return true;
        }

        private float GetDanmakuItemSpeed(string text)
        {
            if (!_lengthSpeedEnabled) return _speedPxPerSec;
            int visibleChars = CountVisibleChars(text);
            float extraUnits = Mathf.Max(0f, visibleChars - _lengthSpeedStartChars) / _lengthSpeedStepChars;
            float factor = Mathf.Clamp(1f + extraUnits * _lengthSpeedMultiplier, 1f, _lengthSpeedMaxMultiplier);
            return _speedPxPerSec * factor;
        }

        // ===== 对象池 =====
        private GameObject GetDanmakuItem()
        {
            GameObject go = null;
            if (_pool.Count > 0) go = _pool.Dequeue();

            if (go == null)
            {
                go = new GameObject("DanmakuItem", typeof(RectTransform), typeof(Text), typeof(CanvasGroup));
                go.transform.SetParent(_danmakuLayer, false);

                var txt = go.GetComponent<Text>();
                Subtitle.Config.Settings.ApplyDanmakuTextOverrides(txt);
                txt.alignment = TextAnchor.MiddleLeft;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                txt.raycastTarget = false;
            }

            var group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>();
            group.alpha = _danmakuOpacity;

            return go;
        }

        private void Recycle(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            _pool.Enqueue(go);
        }

    }
}
