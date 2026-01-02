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
        private Camera _world3dCamera;
        private float _world3dNextCamRefresh;

        public void AddWorld3D(IPlayer speaker, string text, Color color, float durationSec)
        {
            if (speaker == null || string.IsNullOrEmpty(text)) return;
            if (Subtitle.Config.Settings.EnableWorld3D != null && !Subtitle.Config.Settings.EnableWorld3D.Value) return;

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

            var bubble = CreateWorld3DBubble(anchor, baseYOffset);
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
                    if (old != null) old.Destroy();
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
            foreach (var kv in _world3dBubbles)
            {
                var group = kv.Value;
                if (group == null || group.Anchor == null)
                {
                    _world3dRemoveIds.Add(kv.Key);
                    continue;
                }

                float stackOffset = GetWorld3DStackOffsetY();
                for (int i = group.Bubbles.Count - 1; i >= 0; i--)
                {
                    var bubble = group.Bubbles[i];
                    if (bubble == null || bubble.Anchor == null)
                    {
                        group.Bubbles.RemoveAt(i);
                        continue;
                    }

                    bubble.Update(now, cam, stackOffset * i);
                    if (bubble.Expired)
                    {
                        bubble.Destroy();
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
                    bubble.ApplyOffset(GetWorld3DStackOffsetY() * i);
                }
            }
        }

        private void OnDestroy()
        {
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

        private World3DBubble CreateWorld3DBubble(Transform anchor, float baseYOffset)
        {
            var root = new GameObject("World3DBubble", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            root.transform.SetParent(anchor, true);
            root.transform.position = anchor.position + Vector3.up * (baseYOffset + GetWorld3DExtraOffsetY());
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

            var bubble = new World3DBubble(root, rootRt, bubbleRt, bg, textRt, text, group, scaler, baseYOffset);
            bubble.ApplyStyle();
            bubble.ApplyOffset(0f);
            return bubble;
        }

        private static bool TryGetWorld3DAnchor(IPlayer speaker, out Transform anchor, out float baseYOffset)
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

        private static Transform TryGetHeadTransform(IPlayer speaker)
        {
            if (speaker == null) return null;

            try
            {
                var ts = Traverse.Create(speaker);

                object headObj =
                    (ts.Property("HeadTransform") != null ? ts.Property("HeadTransform").GetValue() : null) ??
                    (ts.Field("HeadTransform") != null ? ts.Field("HeadTransform").GetValue() : null);
                var head = ExtractTransform(headObj);
                if (head != null) return head;

                object bonesObj =
                    (ts.Property("PlayerBones") != null ? ts.Property("PlayerBones").GetValue() : null) ??
                    (ts.Field("PlayerBones") != null ? ts.Field("PlayerBones").GetValue() : null) ??
                    (ts.Property("Bones") != null ? ts.Property("Bones").GetValue() : null) ??
                    (ts.Field("Bones") != null ? ts.Field("Bones").GetValue() : null) ??
                    (ts.Property("PlayerBody") != null ? ts.Property("PlayerBody").GetValue() : null) ??
                    (ts.Field("PlayerBody") != null ? ts.Field("PlayerBody").GetValue() : null);

                if (bonesObj != null)
                {
                    var tb = Traverse.Create(bonesObj);
                    object head2 =
                        (tb.Property("Head") != null ? tb.Property("Head").GetValue() : null) ??
                        (tb.Field("Head") != null ? tb.Field("Head").GetValue() : null) ??
                        (tb.Property("HeadTransform") != null ? tb.Property("HeadTransform").GetValue() : null) ??
                        (tb.Field("HeadTransform") != null ? tb.Field("HeadTransform").GetValue() : null);
                    head = ExtractTransform(head2);
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
                var ts = Traverse.Create(speaker);
                var trObj = ts.Property("Transform") != null ? ts.Property("Transform").GetValue() : null;
                var tr = trObj as Transform;
                if (tr != null) return tr;

                var go =
                    (ts.Property("gameObject") != null ? ts.Property("gameObject").GetValue() : null) as GameObject ??
                    (ts.Field("gameObject") != null ? ts.Field("gameObject").GetValue() : null) as GameObject;
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
            if (string.IsNullOrEmpty(src)) return src;

            bool wrapEnabled = Subtitle.Config.Settings.World3DWrap != null && Subtitle.Config.Settings.World3DWrap.Value;
            int limit = (Subtitle.Config.Settings.World3DWrapLength != null)
                ? Subtitle.Config.Settings.World3DWrapLength.Value
                : 0;

            if (!wrapEnabled) return src;
            return (limit > 0) ? ForceWrapByLength(src, limit) : src;
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
            public readonly Transform Anchor;
            private readonly GameObject _root;
            private readonly RectTransform _rootRt;
            private readonly RectTransform _bubbleRt;
            private readonly Image _bg;
            private readonly RectTransform _textRt;
            private readonly Text _text;
            private readonly CanvasGroup _group;
            private readonly CanvasScaler _scaler;
            private readonly float _baseYOffset;
            private float _endTime;
            private float _fadeInSec;
            private float _fadeOutSec;
            private float _fadeInEndTime;
            private float _fadeOutStartTime;
            private string _rawText;
            private float _nextFaceUpdateTime;

            public bool Expired { get; private set; }

            public World3DBubble(GameObject root, RectTransform rootRt, RectTransform bubbleRt, Image bg,
                RectTransform textRt, Text text, CanvasGroup group, CanvasScaler scaler, float baseYOffset)
            {
                _root = root;
                _rootRt = rootRt;
                _bubbleRt = bubbleRt;
                _bg = bg;
                _textRt = textRt;
                _text = text;
                _group = group;
                _scaler = scaler;
                _baseYOffset = baseYOffset;
                Anchor = root != null ? root.transform.parent : null;
                _nextFaceUpdateTime = 0f;
            }

            public void Show(string text, Color color, float durationSec)
            {
                if (_root == null) return;

                _rawText = text;
                _text.text = ApplyWorld3DWrap(_rawText);
                _text.color = color;

                ApplyResolution();
                UpdateLayout();

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

                _group.alpha = _fadeInSec > 0f ? 0f : 1f;
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

            public void ApplyOffset(float stackOffsetY)
            {
                if (_root == null) return;
                var extra = GetWorld3DExtraOffsetY();
                var anchor = Anchor != null ? Anchor : _root.transform.parent;
                if (anchor != null)
                    _root.transform.position = anchor.position + Vector3.up * (_baseYOffset + extra + stackOffsetY);
            }

            public void Update(float now, Camera cam, float stackOffsetY)
            {
                if (_root == null) { Expired = true; return; }

                ApplyOffset(stackOffsetY);

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
                _group.alpha = alpha;

                if (cam != null && ShouldFacePlayer())
                {
                    float interval = GetWorld3DFaceUpdateInterval();
                    if (interval <= 0f || now >= _nextFaceUpdateTime)
                    {
                        var dir = cam.transform.position - _root.transform.position;
                        if (dir.sqrMagnitude > 0.0001f)
                            _root.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        _nextFaceUpdateTime = interval <= 0f ? now : now + interval;
                    }

                    var canvas = _root.GetComponent<Canvas>();
                    if (canvas != null && canvas.worldCamera != cam)
                        canvas.worldCamera = cam;
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
            float stackOffset = GetWorld3DStackOffsetY();
            for (int i = 0; i < group.Bubbles.Count; i++)
            {
                var bubble = group.Bubbles[i];
                if (bubble != null)
                    bubble.ApplyOffset(stackOffset * i);
            }
        }
    }
}
