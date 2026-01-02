using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using Subtitle.Utils;
using System.Reflection;

namespace Subtitle.Patch
{
    internal class GameWorldRegisterPlayerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            //截取玩家加载游戏场景事件
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.RegisterPlayer));
        }

        [PatchPostfix]
        public static void PatchPostfix(IPlayer iPlayer)
        {
            Subtitle.Utils.SpeakerIndex.IndexPlayer(iPlayer);
        }

    }

    internal class GameWorldUnregisterPlayerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            //截取玩家退出游戏场景事件
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.UnregisterPlayer));
        }

        [PatchPostfix]
        public static void PatchPostfix(IPlayer iPlayer)
        {
            Subtitle.Utils.SpeakerIndex.RemovePlayer(iPlayer);
            if (iPlayer.IsYourPlayer)
            {
                Plugin.Instance.DestroySubtitle();
                Logger.LogDebug("Player unregistered. Subtitle system cleaned up.");
            }
        }
    }

    
}