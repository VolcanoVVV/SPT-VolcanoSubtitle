using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Subtitle.Utils
{
   public static class SpeakerIndex
   {
        private static readonly Dictionary<object, IPlayer> _bySpeaker = new Dictionary<object, IPlayer>();

        public static void IndexPlayer(IPlayer p)
        {
            if (p == null) return;

            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = p.GetType();
            object spk = null;

            // 1) 先试常见命名
            try { var pi = t.GetProperty("PhraseSpeaker", BF); if (pi != null && pi.CanRead) spk = pi.GetValue(p, null); } catch { }
            if (spk == null) { try { var pi = t.GetProperty("Speaker", BF); if (pi != null && pi.CanRead) spk = pi.GetValue(p, null); } catch { } }
            if (spk == null) { try { var fi = t.GetField("_phraseSpeaker", BF); if (fi != null) spk = fi.GetValue(p); } catch { } }
            if (spk == null) { try { var fi = t.GetField("_speaker", BF); if (fi != null) spk = fi.GetValue(p); } catch { } }

            // 2) 兜底：按“类型名”扫描成员，找含 PhraseSpeaker 的对象，或含 Speaker 且带 Play() 方法的对象
            if (spk == null)
            {
                try
                {
                    var members = t.GetMembers(BF);
                    for (int i = 0; i < members.Length; i++)
                    {
                        object v = null;
                        var pi = members[i] as PropertyInfo;
                        if (pi != null && pi.CanRead) { try { v = pi.GetValue(p, null); } catch { } }
                        else
                        {
                            var fi = members[i] as FieldInfo;
                            if (fi != null) { try { v = fi.GetValue(p); } catch { } }
                        }
                        if (v == null) continue;

                        var n = v.GetType().Name;
                        bool looksLikePhraseSpeaker =
                            (n.IndexOf("PhraseSpeaker", StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (n.IndexOf("Speaker", StringComparison.OrdinalIgnoreCase) >= 0 &&
                             HasPlayMethod(v));

                        if (looksLikePhraseSpeaker)
                        {
                            spk = v;
                            break;
                        }
                    }
                }
                catch { }
            }

            if (spk != null)
                _bySpeaker[spk] = p; // 建立 “PhraseSpeaker实例 → IPlayer” 的映射
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
                var keys = new List<object>();
                foreach (var kv in _bySpeaker) if (UnityEngine.Object.ReferenceEquals(kv.Value, p)) keys.Add(kv.Key);
                foreach (var k in keys) _bySpeaker.Remove(k);
        }

            public static IPlayer TryGetBySpeaker(object speakerObj)
            {
                if (speakerObj != null && _bySpeaker.TryGetValue(speakerObj, out var p)) return p;
                return null;
            }

        public static void Clear() => _bySpeaker.Clear();
   }
    
}
