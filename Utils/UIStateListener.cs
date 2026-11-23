using UnityEngine;

namespace Subtitle
{
    public class UIStateListener : MonoBehaviour
    {
#if GAME_IL2CPP
        public UIStateListener(System.IntPtr pointer) : base(pointer) { }
#endif
        // OnDestroy 一定紧跟 OnDisable，DestroySubtitle 是幂等的，只保留 OnDisable 即可
        private void OnDisable()
        {
#if GAME_IL2CPP
            var manager = SubtitleSystem.SubtitleManager.Instance;
            if (manager == null || manager.transform.parent != transform) return;
#endif
            Plugin.Instance?.DestroySubtitle();
        }
    }
}