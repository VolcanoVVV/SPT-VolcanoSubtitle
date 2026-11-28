// 隔离游戏依赖；测试链接真实解析/缓存/广播源文件，替身只记录输入输出。
namespace UnityEngine
{
    public class Component { public Transform transform; public int GetInstanceID() => 1; }
    public class Transform { public string name; public Transform parent; }
    public class AudioClip { public string name; public float length = 1; }
    public struct Color { public static Color white => new Color(); }
    public static class ColorUtility { public static string ToHtmlStringRGB(Color c) => "FFFFFF"; }
    public static class Random { public static int Range(int min, int max) => min; }
    public static class Debug
    {
        public static void Log(object s) { }
        public static void LogWarning(object s) { }
        public static void LogError(object s) { }
    }
}
public class QueuePlayer : UnityEngine.Component { }
namespace HarmonyLib
{
    public class HarmonyPatch : System.Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(System.Type type, string name, System.Type[] args) { }
    }
    public class HarmonyPostfix : System.Attribute { }
}
namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public void LogInfo(object s) { }
        public void LogWarning(object s) { }
    }
    public static class Logger { public static ManualLogSource CreateLogSource(string s) => new ManualLogSource(); }
}
namespace Subtitle.Config
{
    public class Entry<T> { public T Value; public Entry(T value) { Value = value; } }
    public static class Settings
    {
        public enum RoleKind { Unknown }
        public static Entry<bool> EnableSubtitle = new Entry<bool>(true), EnableDanmaku = new Entry<bool>(true),
            EnableMapBroadcastSubtitle = new Entry<bool>(true), EnableMapBroadcastDanmaku = new Entry<bool>(true),
            MapBroadcastDebug = new Entry<bool>(false), SubtitleShowRoleTag = new Entry<bool>(false),
            DanmakuShowRoleTag = new Entry<bool>(false);
        public static Entry<UnityEngine.Color> SubRole_LabAnnouncer, SubText_LabAnnouncer,
            DmRole_LabAnnouncer, DmText_LabAnnouncer;
        public static string GetRoleLabel(string key, string fallback) => fallback;
    }
    public static class PhraseFilterManager
    {
        public static string Root;
        public static int VoiceResolutions;
        public static string LocaleRootDir => Root;
        public static string DefaultLocalesDir => System.IO.Path.Combine(Root, "ch");
        public static string ResolveLocaleFile(string name) => System.IO.Path.Combine(Root, name);
        public static string ResolveCurrentLocaleFile(string name) => System.IO.Path.Combine(Root, name);
        public static string ResolveCurrentLocaleVoiceFile(string voice)
        {
            VoiceResolutions++;
            return System.IO.Path.Combine(Root, voice + ".jsonc");
        }
        public static void GetAllowFlags(string channel, string voice, string phrase, string id,
            out bool net, out bool general) { net = general = true; }
    }
}
namespace Subtitle.Utils
{
    public static class StreamerFilter { public static string Apply(string text) => text; }
}
namespace SubtitleSystem
{
    public class SubtitleManager
    {
        public static SubtitleManager Instance = new SubtitleManager();
        public bool isActiveAndEnabled = true;
        public readonly System.Collections.Generic.List<string> Subtitles = new System.Collections.Generic.List<string>();
        public readonly System.Collections.Generic.List<string> Danmaku = new System.Collections.Generic.List<string>();
        public void AddSubtitle(string text, UnityEngine.Color color, float duration) => Subtitles.Add(text);
        public void AddDanmaku(string text, UnityEngine.Color color) => Danmaku.Add(text);
        public bool TryAddSubtitle(string text, UnityEngine.Color color, float duration, Subtitle.Config.Settings.RoleKind kind)
        { AddSubtitle(text, color, duration); return true; }
        public bool TryAddDanmaku(string text, UnityEngine.Color color)
        { AddDanmaku(text, color); return true; }
    }
}
