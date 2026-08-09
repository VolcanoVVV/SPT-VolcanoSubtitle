using UnityEngine;

namespace Subtitle
{
    public class UIStateListener : MonoBehaviour
    {
        // OnDestroy 一定紧跟 OnDisable，DestroySubtitle 是幂等的，只保留 OnDisable 即可
        private void OnDisable() => Plugin.Instance?.DestroySubtitle();
    }
}