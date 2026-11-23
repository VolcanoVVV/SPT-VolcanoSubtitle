using EFT.UI.Screens;
using EFT.UI;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace Subtitle.Patch
{
    [HarmonyPatch]
    internal static class BattleUIScreenPatches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var battleScreenType = typeof(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType>);
            yield return AccessTools.DeclaredMethod(typeof(EftBattleUIScreen.RaidBattleUIScreenController),
                nameof(EftBattleUIScreen.RaidBattleUIScreenController.ShowAction), new[] { battleScreenType });
            yield return AccessTools.DeclaredMethod(typeof(EftBattleUIScreen.HideoutBattleUIScreenController),
                nameof(EftBattleUIScreen.HideoutBattleUIScreenController.ShowAction), new[] { battleScreenType });
        }

        private static void Postfix(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType> __0)
        {
            var screen = __0?.TryCast<EftBattleUIScreen>();
            if (screen == null) return;

            Plugin.Instance.TryAttachToBattleUIScreen(screen);
            if (screen.GetComponent<UIStateListener>() == null)
                screen.gameObject.AddComponent<UIStateListener>();
        }
    }
}
