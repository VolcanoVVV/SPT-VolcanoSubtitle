using Comfort.Common;
using EFT;
using HarmonyLib;

namespace Subtitle.Utils
{
    public static class FriendlyUtils
    {
        // 取本地玩家
        public static IPlayer GetMainPlayer()
        {
            var gw = Singleton<GameWorld>.Instance;
            return gw != null ? gw.MainPlayer as IPlayer : null;
        }

        // 兼容性获取 GroupId（避免部分版本/观察者对象取不到）
        public static string GetGroupIdSafe(IPlayer p)
        {
            if (p == null) return null;
            try
            {
                // 直取 IPlayer.GroupId
                var direct = p.GroupId;
                if (!string.IsNullOrEmpty(direct)) return direct;
            }
            catch { }

            // 反射兜底：player.GroupId / profile.info.GroupId
            try
            {
                var v = Traverse.Create(p).Property("GroupId")?.GetValue();
                if (v != null)
                {
                    var s = v.ToString();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }

            try
            {
                var prof = p.Profile;
                if (prof != null)
                {
                    var info = Traverse.Create(prof).Property("Info")?.GetValue()
                             ?? Traverse.Create(prof).Field("Info")?.GetValue();
                    if (info != null)
                    {
                        var gid = Traverse.Create(info).Property("GroupId")?.GetValue()
                                ?? Traverse.Create(info).Field("GroupId")?.GetValue();
                        if (gid != null)
                        {
                            var s = gid.ToString();
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        // ★ 友军判定：与本地玩家 GroupId 一致，且不是本地玩家本人
        public static bool IsFriendlyToMain(this IPlayer player)
        {
            try
            {
                var main = GetMainPlayer();
                if (player == null || main == null) return false;

                var mg = GetGroupIdSafe(main);
                var og = GetGroupIdSafe(player);

                return !string.IsNullOrEmpty(mg)
                    && !string.IsNullOrEmpty(og)
                    && mg == og
                    && !player.IsYourPlayer;
            }
            catch { return false; }
        }
    }
}
