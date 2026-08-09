using EFT;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Subtitle.Utils
{
   public static class SpeakerIndex
   {
        private static readonly Dictionary<object, IPlayer> _bySpeaker = new Dictionary<object, IPlayer>();
        // 反向索引：玩家 → 其 Speaker，用于 O(1) 移除
        private static readonly Dictionary<IPlayer, object> _byPlayer = new Dictionary<IPlayer, object>();

        // 全成员扫描兜底命中后缓存成员信息，避免每次注册都全量反射
        private static readonly Dictionary<Type, MemberInfo> s_SpeakerMemberCache = new Dictionary<Type, MemberInfo>();

        public static void IndexPlayer(IPlayer p)
        {
            if (p == null) return;

            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = p.GetType();
            object spk = null;

            // 0) 强类型快路径：Player.Speaker 是公开字段（4.1.x）
            var pl = p as Player;
            if (pl != null)
            {
                try { spk = pl.Speaker; } catch { }
            }

            // 1) 先试常见命名
            if (spk == null) { try { var pi = t.GetProperty("PhraseSpeaker", BF); if (pi != null && pi.CanRead) spk = pi.GetValue(p, null); } catch { } }
            if (spk == null) { try { var pi = t.GetProperty("Speaker", BF); if (pi != null && pi.CanRead) spk = pi.GetValue(p, null); } catch { } }
            if (spk == null) { try { var fi = t.GetField("_phraseSpeaker", BF); if (fi != null) spk = fi.GetValue(p); } catch { } }
            if (spk == null) { try { var fi = t.GetField("_speaker", BF); if (fi != null) spk = fi.GetValue(p); } catch { } }

            // 1.5) 上次全成员扫描命中的成员，同类型直接复用
            if (spk == null)
            {
                MemberInfo cached;
                if (s_SpeakerMemberCache.TryGetValue(t, out cached) && cached != null)
                {
                    spk = ReadMemberValue(cached, p);
                }
            }

            // 2) 兜底：按“类型名”扫描成员，找含 PhraseSpeaker 的对象，或含 Speaker 且带 Play() 方法的对象
            if (spk == null)
            {
                try
                {
                    var members = t.GetMembers(BF);
                    for (int i = 0; i < members.Length; i++)
                    {
                        object v = ReadMemberValue(members[i], p);
                        if (v == null) continue;

                        var n = v.GetType().Name;
                        bool looksLikePhraseSpeaker =
                            (n.IndexOf("PhraseSpeaker", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (n.IndexOf("Speaker", StringComparison.OrdinalIgnoreCase) >= 0 &&
                             HasPlayMethod(v));

                        if (looksLikePhraseSpeaker)
                        {
                            spk = v;
                            s_SpeakerMemberCache[t] = members[i]; // 缓存命中成员
                            break;
                        }
                    }
                }
                catch { }
            }

            if (spk != null)
            {
                // 同一玩家重复注册时，先摘掉旧的正向映射
                object oldSpk;
                if (_byPlayer.TryGetValue(p, out oldSpk) && !object.ReferenceEquals(oldSpk, spk))
                    _bySpeaker.Remove(oldSpk);

                _bySpeaker[spk] = p; // 建立 “PhraseSpeaker实例 → IPlayer” 的映射
                _byPlayer[p] = spk;
            }
        }

        // 辅助：读取属性/字段成员的值
        private static object ReadMemberValue(MemberInfo m, object instance)
        {
            try
            {
                var pi = m as PropertyInfo;
                if (pi != null && pi.CanRead) return pi.GetValue(instance, null);
                var fi = m as FieldInfo;
                if (fi != null) return fi.GetValue(instance);
            }
            catch { }
            return null;
        }

        // 辅助：判断该对象是否有 Play() 实例方法
        private static bool HasPlayMethod(object o)
        {
            try
            {
                return o.GetType().GetMethod("Play",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
            }
            catch { return false; }
        }

        public static void RemovePlayer(IPlayer p)
        {
            if (p == null) return;
            object spk;
            if (_byPlayer.TryGetValue(p, out spk))
            {
                _byPlayer.Remove(p);
                _bySpeaker.Remove(spk);
            }
        }

        public static IPlayer TryGetBySpeaker(object speakerObj)
        {
            if (speakerObj != null && _bySpeaker.TryGetValue(speakerObj, out var p)) return p;
            return null;
        }
   }
}
