using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Subtitle.Utils
{
    // 每类日志独立限频，状态有界；不保留台词、昵称或异常对象。
    internal static class LogThrottle
    {
        private sealed class Window { internal double Next; internal int Skipped; }
        private static readonly Dictionary<string, Window> Windows = new Dictionary<string, Window>();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly object Sync = new object();
        private static long s_Suppressed;
        internal static long SuppressedCount { get { lock (Sync) return s_Suppressed; } }
        internal static bool Allow(string key, double seconds = 5)
        {
            int ignored;
            return AllowAt(key, seconds, Clock.Elapsed.TotalSeconds, out ignored);
        }
        internal static bool AllowAt(string key, double seconds, double now, out int suppressed)
        {
            lock (Sync)
            {
                Window window;
                if (!Windows.TryGetValue(key, out window))
                {
                    // 调用方使用固定类别键；额外保护防止未来误传动态文本无限增长。
                    if (Windows.Count >= 128) Windows.Clear();
                    Windows[key] = window = new Window { Next = now };
                }
                if (now < window.Next)
                {
                    window.Skipped++;
                    s_Suppressed++;
                    suppressed = 0;
                    return false;
                }
                suppressed = window.Skipped;
                window.Skipped = 0;
                window.Next = now + Math.Max(0, seconds);
                return true;
            }
        }
    }
}
