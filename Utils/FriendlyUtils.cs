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

        // —— 本地玩家 GroupId 缓存：每条语音都会判定多次友军，避免反复 Singleton+反射 ——
        private static IPlayer s_CachedMainPlayer;
        private static string s_CachedMainGroupId;

        // 对局结束（本地玩家注销）时调用，清空缓存
        public static void InvalidateMainPlayerCache()
        {
            s_CachedMainPlayer = null;
            s_CachedMainGroupId = null;
        }

        // ★ 友军判定：与本地玩家 GroupId 一致，且不是本地玩家本人
        public static bool IsFriendlyToMain(this IPlayer player)
        {
            try
            {
                var main = GetMainPlayer();
                if (player == null || main == null) return false;

                // 本地玩家引用不变时直接复用缓存的 GroupId
                if (!object.ReferenceEquals(main, s_CachedMainPlayer))
                {
                    s_CachedMainPlayer = main;
                    s_CachedMainGroupId = GetGroupIdSafe(main);
                }

                var mg = s_CachedMainGroupId;
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
