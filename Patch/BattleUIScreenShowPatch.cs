using System;
using System.Reflection;
using EFT;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Subtitle.Patch
{
    internal class BattleUIScreenShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(EftBattleUIScreen), nameof(EftBattleUIScreen.Show), new[] { typeof(GamePlayerOwner) });

        [PatchPostfix]
        public static void PatchPostfix(EftBattleUIScreen __instance)
        {
            if (__instance == null)
            {
                Logger.LogDebug("EftBattleUIScreen is null. Cannot attach SubtitleManager.");
                return;
            }

            Plugin.Instance.TryAttachToBattleUIScreen(__instance);

            // 监听界面切换事件（假设界面有 OnDisable 或 OnDestroy 事件）
            __instance.gameObject.AddComponent<UIStateListener>();
        }
    }
}