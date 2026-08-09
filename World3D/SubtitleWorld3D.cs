using System.Collections;
using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubtitleSystem
{
    public partial class SubtitleManager : MonoBehaviour
    {
        private const float World3DScaleDefault = 0.01f;
        private const float World3DDynamicPixelsPerUnitDefault = 20f;
        private const float World3DMaxWidth = 420f;
        private const float World3DPaddingX = 14f;
        private const float World3DPaddingY = 8f;
        private const float World3DFadeInSecDefault = 0.15f;
        private const float World3DFadeOutSecDefault = 0.25f;
        private const int World3DStackMaxLinesDefault = 3;
        private const float World3DStackOffsetYDefault = 0.18f;
        private const float World3DMaxDurationSec = 20f;
        private const float World3DHeadOffset = 0.18f;
        private const float World3DBodyOffset = 1.6f;

        private readonly Dictionary<int, World3DBubbleGroup> _world3dBubbles = new Dictionary<int, World3DBubbleGroup>();
        private readonly List<int> _world3dRemoveIds = new List<int>();
        private readonly Queue<World3DBubble> _world3dPool = new Queue<World3DBubble>(); // 气泡对象池（减少 GC）
        private readonly Dictionary<IPlayer, Transform> _headTransformCache = new Dictionary<IPlayer, Transform>(); // 头部 Transform 缓存
        private Camera _world3dCamera;
        private float _world3dNextCamRefresh;

        // World3D 设置快照：避免每帧/每气泡重复读 ConfigEntry
        private struct World3DSettingsSnapshot
        {
            public float ExtraOffsetY;
            public float StackOffsetY;
            public bool FacePlayer;
            public float FaceUpdateInterval;
        }
        private World3DSettingsSnapshot _w3dSnap;

        // 刷新 World3D 设置快照（创建气泡时与设置变更时调用）
        private void RefreshWorld3DSnapshot()
        {
            _w3dSnap.ExtraOffsetY = GetWorld3DExtraOffsetY();
            _w3dSnap.StackOffsetY = GetWorld3DStackOffsetY();
            _w3dSnap.FacePlayer = ShouldFacePlayer();
            _w3dSnap.FaceUpdateInterval = GetWorld3DFaceUpdateInterval();
        }

        // 回收气泡到对象池
        private void RecycleWorld3DBubble(World3DBubble bubble)
        {
            if (bubble == null || !bubble.IsAlive) return;
            bubble.Deactivate();
            _world3dPool.Enqueue(bubble);
        }

        public void AddWorld3D(IPlayer speaker, string text, Color color, float durationSec)
        {
            if (speaker == null || string.IsNullOrEmpty(text)) return;
            if (Subtitle.Config.Settings.EnableWorld3D != null && !Subtitle.Config.Settings.EnableWorld3D.Value) return;

            RefreshWorld3DSnapshot(); // 每条语音刷新一次设置快照

            Transform anchor;
            float baseYOffset;
            if (!TryGetWorld3DAnchor(speaker, out anchor, out baseYOffset)) return;

            int key = anchor.GetInstanceID();
            World3DBubbleGroup group;
            if (!_world3dBubbles.TryGetValue(key, out group) || group == null || group.Anchor != anchor)
            {
                group = new World3DBubbleGroup(anchor, baseYOffset);
                _world3dBubbles[key] = group;
            }

            float extraDur = GetWorld3DExtraDurationSec();
            float baseDur = durationSec > 0f ? durationSec : 2.5f;
            float dur = baseDur + extraDur;

            var bubble = GetWorld3DBubble(anchor, baseYOffset);
            bubble.Show(text, color, dur);
            group.Bubbles.Insert(0, bubble);

            int maxLines = GetWorld3DStackMaxLines();
            if (group.Bubbles.Count > maxLines)
            {
                int removeCount = group.Bubbles.Count - maxLines;
                for (int i = 0; i < removeCount; i++)
                {
                    int idx = group.Bubbles.Count - 1;
                    var old = group.Bubbles[idx];
                    group.Bubbles.RemoveAt(idx);
                    if (old != null) RecycleWorld3DBubble(old);
                }
            }

            UpdateWorld3DStack(group);
        }

        private void UpdateWorld3DBubbles()
        {
            if (_world3dBubbles.Count == 0) return;

            float now = Time.unscaledTime;
            var cam = GetWorld3DCamera();

            _world3dRemoveIds.Clear();
            float stackOffset = _w3dSnap.StackOffsetY; // 循环不变量，提到组循环外
            foreach (var kv in _world3dBubbles)
            {
                var group = kv.Value;
                if (group == null || group.Anchor == null)
                {
                    _world3dRemoveIds.Add(kv.Key);
                    continue;
                }

                for (int i = group.Bubbles.Count - 1; i >= 0; i--)
                {
                    var bubble = group.Bubbles[i];
                    if (bubble == null || bubble.Anchor == null)
                    {
                        group.Bubbles.RemoveAt(i);
                        continue;
                    }

                    bubble.Update(now, cam, stackOffset * i, _w3dSnap);
                    if (bubble.Expired)
                    {
                        RecycleWorld3DBubble(bubble);
                        group.Bubbles.RemoveAt(i);
                    }
                }

                if (group.Bubbles.Count == 0)
                    _world3dRemoveIds.Add(kv.Key);
            }

            for (int i = 0; i < _world3dRemoveIds.Count; i++)
            {
                int id = _world3dRemoveIds[i];
                World3DBubbleGroup group;
                if (_world3dBubbles.TryGetValue(id, out group) && group != null)
                    group.DestroyAll();
                _world3dBubbles.Remove(id);
            }
        }

        public void RefreshWorld3DStyles()
        {
            RefreshWorld3DSnapshot(); // 设置变更时同步刷新快照
            if (_world3dBubbles.Count == 0) return;
            foreach (var kv in _world3dBubbles)
            {
                var group = kv.Value;
                if (group == null) continue;
                for (int i = 0; i < group.Bubbles.Count; i++)
                {
                    var bubble = group.Bubbles[i];
                    if (bubble == null) continue;
                    bubble.ApplyStyle();
                    bubble.ApplyOffset(_w3dSnap.StackOffsetY * i, _w3dSnap.ExtraOffsetY);
                }
            }
        }

        private void OnDestroy()
        {
            // 池化气泡随管理器一并销毁
            while (_world3dPool.Count > 0)
            {
                var pooled = _world3dPool.Dequeue();
                if (pooled != null) pooled.Destroy();
            }
            _headTransformCache.Clear();

            if (_world3dBubbles.Count == 0) return;
            foreach (var kv in _world3dBubbles)
            {
                if (kv.Value != null)
                    kv.Value.DestroyAll();
            }
            _world3dBubbles.Clear();
        }

        private Camera GetWorld3DCamera()
        {
            if (_world3dCamera != null && _world3dCamera.isActiveAndEnabled)
                return _world3dCamera;

            float now = Time.unscaledTime;
            if (now < _world3dNextCamRefresh)
                return _world3dCamera;

            _world3dNextCamRefresh = now + 1f;
            var cam = Camera.main;
            if (cam == null)
                cam = Object.FindObjectOfType<Camera>();
            _world3dCamera = cam;
            return cam;
        }

        // 优先从对象池复用气泡，池空才新建
        private World3DBubble GetWorld3DBubble(Transform anchor, float baseYOffset)
        {
            while (_world3dPool.Count > 0)
            {
                var pooled = _world3dPool.Dequeue();
                if (pooled != null && pooled.IsAlive)
                {
                    pooled.Reattach(anchor, baseYOffset, _w3dSnap.ExtraOffsetY, GetWorld3DCamera());
                    return pooled;
                }
                // 已随锚点被 Unity 销毁的池化对象直接丢弃
            }
            return CreateWorld3DBubble(anchor, baseYOffset);
        }

        private World3DBubble CreateWorld3DBubble(Transform anchor, float baseYOffset)
        {
            var root = new GameObject("World3DBubble", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            root.transform.SetParent(anchor, true);
            root.transform.position = anchor.position + Vector3.up * (baseYOffset + _w3dSnap.ExtraOffsetY);
            root.transform.localRotation = Quaternion.identity;
            float scale = GetWorld3DScale();
            root.transform.localScale = new Vector3(-scale, scale, scale);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = GetWorld3DCamera();

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = GetWorld3DDynamicPixelsPerUnit();

            var rootRt = root.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(World3DMaxWidth, 100f);

            var bubbleGo = new GameObject("Bubble", typeof(RectTransform), typeof(Image));
            bubbleGo.transform.SetParent(root.transform, false);
            var bubbleRt = bubbleGo.GetComponent<RectTransform>();
            bubbleRt.anchorMin = new Vector2(0.5f, 0.5f);
            bubbleRt.anchorMax = new Vector2(0.5f, 0.5f);
            bubbleRt.pivot = new Vector2(0.5f, 0.5f);

            var bg = bubbleGo.GetComponent<Image>();
            bg.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(bubbleGo.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = new Vector2(0.5f, 0.5f);
            textRt.anchorMax = new Vector2(0.5f, 0.5f);
            textRt.pivot = new Vector2(0.5f, 0.5f);

            var text = textGo.GetComponent<Text>();
            Subtitle.Config.Settings.ApplyWorld3DTextOverrides(text);
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;

            var group = root.GetComponent<CanvasGroup>();

            var bubble = new World3DBubble(root, rootRt, bubbleRt, bg, textRt, text, group, scaler, canvas, baseYOffset);
            bubble.ApplyStyle();
            bubble.ApplyOffset(0f, _w3dSnap.ExtraOffsetY);
            return bubble;
        }

        private bool TryGetWorld3DAnchor(IPlayer speaker, out Transform anchor, out float baseYOffset)
        {
            anchor = TryGetHeadTransform(speaker);
            if (anchor != null)
            {
                baseYOffset = World3DHeadOffset;
                return true;
            }

            anchor = TryGetPlayerTransform(speaker);
            if (anchor != null)
            {
                baseYOffset = World3DBodyOffset;
                return true;
            }

            baseYOffset = 0f;
            return false;
        }

        // 头部 Transform 按玩家缓存，失效（Unity 假空）时自动重解析
        private Transform TryGetHeadTransform(IPlayer speaker)
        {
            if (speaker == null) return null;

            Transform cached;
            if (_headTransformCache.TryGetValue(speaker, out cached))
            {
                if (cached != null) return cached;
                _headTransformCache.Remove(speaker);
            }

            var head = ResolveHeadTransform(speaker);
            if (head != null) _headTransformCache[speaker] = head;
            return head;
        }

        // 强类型解析（编译期校验），失败时走极简反射兜底
        private static Transform ResolveHeadTransform(IPlayer speaker)
        {
            try
            {
                var bones = speaker.PlayerBones;
                if (bones != null)
                {
                    var head = bones.Head;
                    if (head != null && head.Original != null)
                        return head.Original;
                }
            }
            catch { }

            try
            {
                var ts = Traverse.Create(speaker);
                var headProp = ts.Property("HeadTransform");
                var head = ExtractTransform(headProp != null ? headProp.GetValue() : null);
                if (head != null) return head;

                var bodyProp = ts.Property("PlayerBody");
                var body = bodyProp != null ? bodyProp.GetValue() : null;
                if (body != null)
                {
                    var tb = Traverse.Create(body);
                    var hb = tb.Property("Head");
                    head = ExtractTransform(hb != null ? hb.GetValue() : null);
                    if (head != null) return head;
                }
            }
            catch { }

            return null;
        }

        private static Transform TryGetPlayerTransform(IPlayer speaker)
        {
            if (speaker == null) return null;
            try
            {
                var bt = speaker.Transform;
                if (bt != null && bt.Original != null)
                    return bt.Original;
            }
            catch { }

            try
            {
                var ts = Traverse.Create(speaker);
                var goProp = ts.Property("gameObject");
                var go = (goProp != null ? goProp.GetValue() : null) as GameObject;
                if (go != null) return go.transform;
            }
            catch { }

            return null;
        }

        private static Transform ExtractTransform(object obj)
        {
            if (obj == null) return null;

            var tr = obj as Transform;
            if (tr != null) return tr;

            try
            {
                var t = Traverse.Create(obj);
                object trObj =
                    (t.Property("Transform") != null ? t.Property("Transform").GetValue() : null) ??
                    (t.Field("Transform") != null ? t.Field("Transform").GetValue() : null) ??
                    (t.Property("Original") != null ? t.Property("Original").GetValue() : null) ??
                    (t.Field("Original") != null ? t.Field("Original").GetValue() : null) ??
                    (t.Property("Anchor") != null ? t.Property("Anchor").GetValue() : null) ??
                    (t.Field("Anchor") != null ? t.Field("Anchor").GetValue() : null);
                return trObj as Transform;
            }
            catch { }

            return null;
        }

        private static float GetWorld3DExtraOffsetY()
        {
            try
            {
                if (Subtitle.Config.Settings.World3DVerticalOffsetY != null)
                    return Subtitle.Config.Settings.World3DVerticalOffsetY.Value;
            }
            catch { }
            return 0f;
        }

        private static float GetWorld3DScale()
        {
            try
            {
                if (Subtitle.Config.Settings.World3DWorldScale != null)
                    return Mathf.Max(0.0005f, Subtitle.Config.Settings.World3DWorldScale.Value);
            }
            catch { }
            return World3DScaleDefault;
        }

        private static float GetWorld3DDynamicPixelsPerUnit()
        {
            try
            {
                if (Subtitle.Config.Settings.World3DDynamicPixelsPerUnit != null)
                    return Mathf.Max(1f, Subtitle.Config.Settings.World3DDynamicPixelsPerUnit.Value);
            }
            catch { }
            return World3DDynamicPixelsPerUnitDefault;
        }

        private static float GetWorld3DFaceUpdateInterval()
        {
            try
            {
                if (Subtitle.Config.Settings.World3DFaceUpdateIntervalSec != null)
                    return Mathf.Max(0f, Subtitle.Config.Settings.World3DFaceUpdateIntervalSec.Value);
            }
            catch { }
            return 0f;
        }

        private static float GetWorld3DExtraDurationSec()
        {
            try
            {
                if (Subtitle.Config.Settings.World3DDisplayDelaySec != null)
                    return Mathf.Clamp(Subtitle.Config.Settings.World3DDisplayDelaySec.Value, 0f, 3f);
            }
            catch { }
            return 0f;
        }

        private static int GetWorld3DStackMaxLines()
        {
            try
            {
                if (Subtitle.Config.Settings.World3DStackMaxLines != null)
                    return Mathf.Clamp(Subtitle.Config.Settings.World3DStackMaxLines.Value, 1, 6);
            }
            catch { }
            return World3DStackMaxLinesDefault;
        }

        private static float GetWorld3DStackOffsetY()
        {
            try
            {
                if (Subtitle.Config.Settings.World3DStackOffsetY != null)
                    return Mathf.Max(0.01f, Subtitle.Config.Settings.World3DStackOffsetY.Value);
            }
            catch { }
            return World3DStackOffsetYDefault;
        }

        private static float GetWorld3DFadeInSec()
        {
            try
            {
                if (Subtitle.Config.Settings.World3DFadeInSec != null)
                    return Mathf.Clamp(Subtitle.Config.Settings.World3DFadeInSec.Value, 0f, 1.0f);
            }
            catch { }
            return World3DFadeInSecDefault;
        }

        private static float GetWorld3DFadeOutSec()
        {
            try
            {
                if (Subtitle.Config.Settings.World3DFadeOutSec != null)
                    return Mathf.Clamp(Subtitle.Config.Settings.World3DFadeOutSec.Value, 0f, 1.5f);
            }
            catch { }
            return World3DFadeOutSecDefault;
        }

        private static string ApplyWorld3DWrap(string src)
        {
            bool wrapEnabled = Subtitle.Config.Settings.World3DWrap != null && Subtitle.Config.Settings.World3DWrap.Value;
            int limit = (Subtitle.Config.Settings.World3DWrapLength != null)
                ? Subtitle.Config.Settings.World3DWrapLength.Value
                : 0;
            return ApplyWrapBySetting(src, wrapEnabled, limit);
        }

        private static bool ShouldFacePlayer()
        {
            try
            {
                if (Subtitle.Config.Settings.World3DFacePlayer != null)
                    return Subtitle.Config.Settings.World3DFacePlayer.Value;
            }
            catch { }
            return true;
        }


        private sealed class World3DBubbleGroup
        {
            public readonly Transform Anchor;
            public readonly float BaseYOffset;
            public readonly List<World3DBubble> Bubbles = new List<World3DBubble>();

            public World3DBubbleGroup(Transform anchor, float baseYOffset)
            {
                Anchor = anchor;
                BaseYOffset = baseYOffset;
            }

            public void DestroyAll()
            {
                for (int i = 0; i < Bubbles.Count; i++)
                {
                    var b = Bubbles[i];
                    if (b != null) b.Destroy();
                }
                Bubbles.Clear();
            }
        }

        private sealed class World3DBubble
        {
            public Transform Anchor { get; private set; }
            private readonly GameObject _root;
            private readonly RectTransform _rootRt;
            private readonly RectTransform _bubbleRt;
            private readonly Image _bg;
            private readonly RectTransform _textRt;
            private readonly Text _text;
            private readonly CanvasGroup _group;
            private readonly CanvasScaler _scaler;
            private readonly Canvas _canvas; // 创建时缓存，避免每帧 GetComponent
            private float _baseYOffset;
            private float _endTime;
            private float _fadeInSec;
            private float _fadeOutSec;
            private float _fadeInEndTime;
            private float _fadeOutStartTime;
            private string _rawText;
            private float _nextFaceUpdateTime;
            private float _lastAlpha = -1f; // 上次写入的透明度，避免重复赋值弄脏 Canvas

            public bool Expired { get; private set; }
            public bool IsAlive { get { return _root != null; } } // Unity 假空检查

            public World3DBubble(GameObject root, RectTransform rootRt, RectTransform bubbleRt, Image bg,
                RectTransform textRt, Text text, CanvasGroup group, CanvasScaler scaler, Canvas canvas, float baseYOffset)
            {
                _root = root;
                _rootRt = rootRt;
                _bubbleRt = bubbleRt;
                _bg = bg;
                _textRt = textRt;
                _text = text;
                _group = group;
                _scaler = scaler;
                _canvas = canvas;
                _baseYOffset = baseYOffset;
                Anchor = root != null ? root.transform.parent : null;
                _nextFaceUpdateTime = 0f;
            }

            // 池化复用：重新挂到新锚点
            public void Reattach(Transform anchor, float baseYOffset, float extraOffsetY, Camera cam)
            {
                if (_root == null) return;
                Anchor = anchor;
                _baseYOffset = baseYOffset;
                _root.transform.SetParent(anchor, true);
                _root.transform.position = anchor.position + Vector3.up * (baseYOffset + extraOffsetY);
                _root.transform.localRotation = Quaternion.identity;
                if (_canvas != null && cam != null && _canvas.worldCamera != cam)
                    _canvas.worldCamera = cam;
            }

            // 回池前停用
            public void Deactivate()
            {
                if (_root != null) _root.SetActive(false);
            }

            public void Show(string text, Color color, float durationSec)
            {
                if (_root == null) return;

                _rawText = text;
                _text.text = ApplyWorld3DWrap(_rawText);
                _text.color = color;

                // 每次显示都重应用样式（字体/字号/描边/阴影/背景/换行/分辨率）。
                // 池化复用的气泡创建时才有样式设置，若不重应用会一直沿用回收前的旧样式
                // （曾表现为局内改字体后气泡仍是旧字体，直到重开战局清空对象池）。
                // ApplyStyle 不改文本颜色，上面的 color 赋值不受影响。
                ApplyStyle();

                float now = Time.unscaledTime;
                float dur = durationSec;
                if (float.IsNaN(dur) || float.IsInfinity(dur) || dur <= 0f)
                    dur = 2.5f;
                else if (dur > World3DMaxDurationSec)
                    dur = World3DMaxDurationSec;
                _endTime = now + dur;
                _fadeInSec = GetWorld3DFadeInSec();
                _fadeOutSec = GetWorld3DFadeOutSec();
                _fadeInEndTime = now + Mathf.Max(0f, _fadeInSec);
                _fadeOutStartTime = _endTime - Mathf.Max(0f, _fadeOutSec);
                if (_fadeOutStartTime < now) _fadeOutStartTime = now;

                _lastAlpha = _fadeInSec > 0f ? 0f : 1f;
                _group.alpha = _lastAlpha;
                Expired = false;
                if (!_root.activeSelf) _root.SetActive(true);
            }

            public void ApplyStyle()
            {
                if (_text == null) return;
                Subtitle.Config.Settings.ApplyWorld3DTextOverrides(_text);
                if (_rawText != null)
                    _text.text = ApplyWorld3DWrap(_rawText);
                ApplyResolution();
                ApplyBackground();
                UpdateLayout();
            }

            public void ApplyOffset(float stackOffsetY, float extraOffsetY)
            {
                if (_root == null) return;
                var anchor = Anchor != null ? Anchor : _root.transform.parent;
                if (anchor != null)
                    _root.transform.position = anchor.position + Vector3.up * (_baseYOffset + extraOffsetY + stackOffsetY);
            }

            public void Update(float now, Camera cam, float stackOffsetY, World3DSettingsSnapshot snap)
            {
                if (_root == null) { Expired = true; return; }

                ApplyOffset(stackOffsetY, snap.ExtraOffsetY);

                if (float.IsNaN(_endTime) || float.IsInfinity(_endTime))
                {
                    Expired = true;
                    return;
                }

                if (now >= _endTime)
                {
                    Expired = true;
                    return;
                }

                float alpha = 1f;
                if (_fadeInSec > 0f && now < _fadeInEndTime)
                {
                    float t = (now - (_fadeInEndTime - _fadeInSec)) / _fadeInSec;
                    alpha = Mathf.Clamp01(t);
                }
                if (_fadeOutSec > 0f && now >= _fadeOutStartTime)
                {
                    float t = (now - _fadeOutStartTime) / _fadeOutSec;
                    alpha = Mathf.Min(alpha, 1f - Mathf.Clamp01(t));
                }
                // 仅在值变化时写入，避免弄脏整个 Canvas
                if (alpha != _lastAlpha)
                {
                    _group.alpha = alpha;
                    _lastAlpha = alpha;
                }

                if (cam != null && snap.FacePlayer)
                {
                    float interval = snap.FaceUpdateInterval;
                    if (interval <= 0f || now >= _nextFaceUpdateTime)
                    {
                        var dir = cam.transform.position - _root.transform.position;
                        if (dir.sqrMagnitude > 0.0001f)
                            _root.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        _nextFaceUpdateTime = interval <= 0f ? now : now + interval;
                    }

                    if (_canvas != null && _canvas.worldCamera != cam)
                        _canvas.worldCamera = cam;
                }
            }

            private void ApplyResolution()
            {
                if (_root == null) return;
                float scale = GetWorld3DScale();
                _root.transform.localScale = new Vector3(-scale, scale, scale);
                if (_scaler != null)
                    _scaler.dynamicPixelsPerUnit = GetWorld3DDynamicPixelsPerUnit();
            }

            private void UpdateLayout()
            {
                float maxWidth = World3DMaxWidth;
                _textRt.sizeDelta = new Vector2(maxWidth, 0f);

                LayoutRebuilder.ForceRebuildLayoutImmediate(_textRt);
                float textWidth = Mathf.Min(_text.preferredWidth, maxWidth);
                float textHeight = _text.preferredHeight;

                textWidth = Mathf.Max(10f, textWidth);
                textHeight = Mathf.Max(10f, textHeight);

                _textRt.sizeDelta = new Vector2(textWidth, textHeight);

                float bubbleW = textWidth + World3DPaddingX * 2f;
                float bubbleH = textHeight + World3DPaddingY * 2f;

                _bubbleRt.sizeDelta = new Vector2(bubbleW, bubbleH);
                _rootRt.sizeDelta = new Vector2(bubbleW, bubbleH);
            }

            public void Destroy()
            {
                if (_root != null)
                    Object.Destroy(_root);
            }

            private void ApplyBackground()
            {
                if (_bg == null) return;
                bool enabled = true;
                try
                {
                    if (Subtitle.Config.Settings.World3DBGEnabled != null)
                        enabled = Subtitle.Config.Settings.World3DBGEnabled.Value;
                }
                catch { }
                _bg.enabled = enabled;
                if (!enabled) return;

                try
                {
                    if (Subtitle.Config.Settings.World3DBGColor != null)
                        _bg.color = Subtitle.Config.Settings.World3DBGColor.Value;
                }
                catch { }
            }
        }

        private void UpdateWorld3DStack(World3DBubbleGroup group)
        {
            if (group == null) return;
            float stackOffset = _w3dSnap.StackOffsetY;
            for (int i = 0; i < group.Bubbles.Count; i++)
            {
                var bubble = group.Bubbles[i];
                if (bubble != null)
                    bubble.ApplyOffset(stackOffset * i, _w3dSnap.ExtraOffsetY);
            }
        }
    }
}
