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
        }

        private DanmakuLane[] _lanes;
        private int _laneCount = 8;

        // 运动/间隔参数（会在 ApplyDanmakuSettings 重载）
        private float _speedPxPerSec = 180f;
        private int _minGapPx = 40;
        private int _fontSizeOverride = 0; // 0 表示不覆盖，用预设

        // 简单对象池（减少 GC）
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();

        // === 新增：发送队列与节流 ===
        private struct DanmakuItem { public string Text; public Color Color; }
        private readonly Queue<DanmakuItem> _danmakuQueue = new Queue<DanmakuItem>();
        private bool _spawnLoopRunning;
        private float _spawnDelaySec = 0.2f; // 默认 0.2s（可被设置覆盖）

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
                _minGapPx = Mathf.Max(0, Subtitle.Config.Settings.DanmakuMinGapPx.Value);
                _fontSizeOverride = Mathf.Max(0, Subtitle.Config.Settings.DanmakuFontSize.Value);
                // 发送间隔
                _spawnDelaySec = Mathf.Clamp(
                    Subtitle.Config.Settings.DanmakuSpawnDelaySec != null 
                    ? Subtitle.Config.Settings.DanmakuSpawnDelaySec.Value
                    : 0.2f, 0f, 1f);

                _danmakuTopOffsetPercent = Mathf.Clamp01(
                    Subtitle.Config.Settings.DanmakuTopOffsetPercent.Value);
                _danmakuAreaMaxPercent = Mathf.Clamp01(
                    Subtitle.Config.Settings.DanmakuAreaMaxPercent.Value);
                DLog("[Danmaku] ApplySettings lanes=" + _laneCount +
                    " speed=" + _speedPxPerSec +
                    " minGap=" + _minGapPx +
                    " spawnDelay=" + _spawnDelaySec +
                    " fontOverride=" + _fontSizeOverride +
                    " top%=" + _danmakuTopOffsetPercent +
                    " area%=" + _danmakuAreaMaxPercent);
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
        private IEnumerator CoSpawnLoop()
        {
            _spawnLoopRunning = true;
            var shortWait = new WaitForSecondsRealtime(0.05f); // 车道繁忙时的重试步进
            while (_danmakuQueue.Count > 0)
            {
                var item = _danmakuQueue.Peek();
                // 尝试生成；若车道暂不可用，等一小会再试，不丢消息
                if (TrySpawnDanmaku(item.Text, item.Color))
                {
                    _danmakuQueue.Dequeue();
                    // 两条弹幕之间留出极短间隔（可在设置改 0.1 或 0.3）
                    if (_spawnDelaySec > 0f)
                        yield return new WaitForSecondsRealtime(_spawnDelaySec);
                }
                else
                {
                    yield return shortWait;
                }
            }
            _spawnLoopRunning = false;
        }

        // ===== 内部：移动协程（右→左） =====
        private IEnumerator CoMoveLeft(GameObject go, float x, float endX)
        {
            var rt = (RectTransform)go.transform;
            bool first = true;
            while (x > endX)
            {
                x -= _speedPxPerSec * Time.unscaledDeltaTime;
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

        private int PickLaneGreedy(float textWidth, int laneCountEffective)
        {
            float now = Time.unscaledTime;
            if (_lanes == null || _lanes.Length == 0) return 0;
            if (laneCountEffective < 1) laneCountEffective = 1;
            if (laneCountEffective > _lanes.Length) laneCountEffective = _lanes.Length;
            // 从 0 开始顺序找——最大化复用低编号车道
                        for (int i = 0; i < laneCountEffective; i++)
                           {
                float minInterval = (_lanes[i].lastTextWidth + _minGapPx) / Mathf.Max(1f, _speedPxPerSec);
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

            // 计算尺寸
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            float textWidth = txt.preferredWidth;
            float textHeight = txt.preferredHeight;

            // 父尺寸
            float parentW = ((RectTransform)this.transform).rect.width;
            float parentH = ((RectTransform)this.transform).rect.height;
            if (parentW < 1f || parentH < 1f) { parentW = Screen.width; parentH = Screen.height; }

            // 区域与车道
            float laneH = Mathf.Max(textHeight + 8f, txt.fontSize + 8f);
            float topMarginPx = parentH * _danmakuTopOffsetPercent;
            float maxAreaH = Mathf.Max(laneH, parentH * _danmakuAreaMaxPercent);

            int maxByArea = Mathf.Max(1, Mathf.FloorToInt(maxAreaH / laneH));
            int laneCountEffective = Mathf.Min(_laneCount, maxByArea);

            int lane = PickLaneGreedy(textWidth, laneCountEffective);
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

            // 开始移动
            StartCoroutine(CoMoveLeft(go, startX, endX));
            return true;
        }

        // ===== 对象池 =====
        private GameObject GetDanmakuItem()
        {
            GameObject go = null;
            if (_pool.Count > 0) go = _pool.Dequeue();

            if (go == null)
            {
                go = new GameObject("DanmakuItem", typeof(RectTransform), typeof(Text));
                go.transform.SetParent(_danmakuLayer, false);

                var txt = go.GetComponent<Text>();
                Subtitle.Config.Settings.ApplyDanmakuTextOverrides(txt);
                txt.alignment = TextAnchor.MiddleLeft;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                txt.raycastTarget = false;
            }
            else
            {
                go.transform.SetParent(_danmakuLayer, false);
            }

            return go;
        }

        private void Recycle(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            go.transform.SetParent(_danmakuLayer, false);
            _pool.Enqueue(go);
        }

    }
}
