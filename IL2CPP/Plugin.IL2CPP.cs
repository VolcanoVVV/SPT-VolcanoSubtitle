using System;
using HarmonyLib;
using Subtitle.Patch;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace Subtitle
{
    public partial class Plugin
    {
        private static void RegisterUnityComponents()
        {
            ClassInjector.RegisterTypeInIl2Cpp<SubtitleSystem.SubtitleManager>();
            SubtitleSystem.SubtitleManager.RegisterLineComponents();
            ClassInjector.RegisterTypeInIl2Cpp<SettingsUI.SettingsWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<SettingsUI.SettingsPreviewPanel>();
            ClassInjector.RegisterTypeInIl2Cpp<Config.PhraseFilterPanel>();
            ClassInjector.RegisterTypeInIl2Cpp<DebugTools.DebugPhrasePanel>();
            DebugTools.DebugPhrasePanel.RegisterAudioCaptureComponent();
            ClassInjector.RegisterTypeInIl2Cpp<DebugTools.DebugDiagnosticsPanel>();
            ClassInjector.RegisterTypeInIl2Cpp<UIStateListener>();
        }

        private static void EnableIl2CppHarmonyPatches(Harmony harmony)
        {
            var patches = new[]
            {
                typeof(SubtitlePatch),
                typeof(SubtitlePatch.Subtitle_PlayDirectPatch),
                typeof(LabRadioPatch),
                typeof(BattleUIScreenPatches)
            };
            foreach (var patch in patches)
            {
                try
                {
                    harmony.CreateClassProcessor(patch).Patch();
                }
                catch (Exception exception)
                {
                    Log?.LogError("Could not apply " + patch.Name + ": " + exception);
                }
            }
        }

        public sealed class SubtitleUpdateBehaviour : MonoBehaviour
        {
            public SubtitleUpdateBehaviour(IntPtr pointer) : base(pointer) { }

            private void Update() => Instance?.Update();
        }
    }
}
