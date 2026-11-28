using EFT;
using HarmonyLib;
#if GAME_IL2CPP
using SPTushonka.Reflection.Patching;
#else
using SPT.Reflection.Patching;
#endif
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
            if (iPlayer == null) return;

            SpeakerIndex.RemovePlayer(iPlayer);
            if (iPlayer.IsYourPlayer)
            {
                // 本地玩家离场 = 对局结束，清掉友军判定的 GroupId 缓存
                Subtitle.Utils.FriendlyUtils.InvalidateMainPlayerCache();
#if GAME_IL2CPP
                SpeakerIndex.Clear();
#endif
                Plugin.Instance?.DestroySubtitle();
                Logger.LogDebug("Player unregistered. Subtitle system cleaned up.");
            }
        }
    }

    
}