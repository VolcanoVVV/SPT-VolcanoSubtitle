using UnityEngine;

namespace Subtitle
{
    public class UIStateListener : MonoBehaviour
    {
        private void OnDisable() => Plugin.Instance?.DestroySubtitle();

        private void OnDestroy() => Plugin.Instance?.DestroySubtitle();
    }
}