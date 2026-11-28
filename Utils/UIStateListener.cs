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
            var plugin = Plugin.Instance;
            // 管理器可能已迁移到新的 BattleUI；旧界面关闭时不能销毁新界面的字幕。
            if (plugin != null && plugin.SubtitleGO != null &&
                plugin.SubtitleGO.transform.IsChildOf(transform))
                plugin.DestroySubtitle();
        }
    }
}
