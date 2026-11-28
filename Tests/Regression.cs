using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Subtitle;
using Subtitle.Config;
using Subtitle.Utils;
using SubtitleSystem;

internal static class Regression
{
    private static int checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }
    private static void Broadcast(string name)
    {
        typeof(LabRadioPatch).GetMethod("QueuePlayer_Play_Postfix", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { new QueuePlayer(), new UnityEngine.AudioClip { name = name } });
    }
    public static void Main(string[] args)
    {
        string temp = Path.Combine(Path.GetTempPath(), "SubtitleRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        PhraseFilterManager.Root = temp;
        try
        {
            int suppressed;
            Check(LogThrottle.AllowAt("test", 5, 0, out suppressed), "first log emitted immediately");
            Check(!LogThrottle.AllowAt("test", 5, 1, out suppressed), "repeated log suppressed");
            Check(LogThrottle.AllowAt("independent", 5, 1, out suppressed), "log categories independent");
            Check(LogThrottle.AllowAt("test", 5, 5, out suppressed) && suppressed == 1,
                "log window reopens and reports suppressed count");
            var pathJson = "{\"path\":\"C:\\\\\",\n// comment\n\"url\":\"https://host/a/*b*/\"}";
            var parsed = JObject.Parse(JsoncUtils.StripJsonComments(pathJson));
            Check((string)parsed["path"] == "C:\\" && (string)parsed["url"] == "https://host/a/*b*/",
                "escaped trailing backslash and comment markers inside strings");
            var quoted = new JObject { ["text"] = "say \"hello\" // text \\" }.ToString();
            Check(JObject.Parse(JsoncUtils.StripJsonComments(quoted)).ToString() == quoted, "escaped quote retained");
            Check(JsoncUtils.StripJsonComments("1/* block\ncomment */2") == "1 \n2", "block comments preserve token separation and lines");
            bool rejected = false;
            try { JsoncUtils.StripJsonComments("{} /* unfinished"); }
            catch (FormatException) { rejected = true; }
            Check(rejected, "unterminated block comment rejected");

            File.WriteAllText(Path.Combine(temp, "Default_Voice.jsonc"), "{\"OnFight\":{\"General\":[\"fallback\"]}}");
            PhraseSubtitle.InvalidateCache();
            for (int i = 0; i < 3; i++)
                Check(PhraseSubtitle.GetSubtitleForChannel("Subtitle", "missing", "OnFight", "1") == "fallback", "missing voice uses fallback");
            Check(PhraseFilterManager.VoiceResolutions == 1, "missing voice resolved only once across calls");
            File.WriteAllText(Path.Combine(temp, "missing.jsonc"), "{\"OnFight\":{\"1\":[\"restored\"]}}");
            Check(PhraseSubtitle.GetSubtitleForChannel("Danmaku", "missing", "OnFight", "1") == "fallback", "negative cache shared across channels");
            PhraseSubtitle.InvalidateCache();
            Check(PhraseSubtitle.GetSubtitleForChannel("World3D", "missing", "OnFight", "1") == "restored", "reload discovers newly added voice");
            File.WriteAllText(Path.Combine(temp, "broken.jsonc"), "{");
            PhraseSubtitle.GetSubtitleForChannel("Subtitle", "broken", "OnFight", "1");
            File.WriteAllText(Path.Combine(temp, "broken.jsonc"), "{\"OnFight\":{\"1\":[\"repaired\"]}}");
            Check(PhraseSubtitle.GetSubtitleForChannel("Subtitle", "broken", "OnFight", "1") == "fallback", "invalid file cached until reload");
            PhraseSubtitle.InvalidateCache();
            Check(PhraseSubtitle.GetSubtitleForChannel("Subtitle", "broken", "OnFight", "1") == "repaired", "reload recovers repaired file");

            string message = "Attention \"staff\"\nC:\\Labs";
            var mapping = new JObject
            {
                ["announcer_single"] = message,
                ["sequences"] = new JArray(new JObject { ["text"] = message,
                    ["clips"] = new JArray("announcer_a", "announcer_b") })
            };
            File.WriteAllText(Path.Combine(temp, "LabBroadcast.jsonc"), mapping.ToString());
            LabRadioPatch.Bootstrap();
            var manager = SubtitleManager.Instance;
            // 总开关 × 广播开关全部组合，字幕和弹幕分别验证。
            for (int mask = 0; mask < 16; mask++)
            {
                Settings.EnableSubtitle.Value = (mask & 1) != 0;
                Settings.EnableMapBroadcastSubtitle.Value = (mask & 2) != 0;
                Settings.EnableDanmaku.Value = (mask & 4) != 0;
                Settings.EnableMapBroadcastDanmaku.Value = (mask & 8) != 0;
                manager.Subtitles.Clear(); manager.Danmaku.Clear();
                Broadcast("announcer_single");
                Check(manager.Subtitles.Count == ((mask & 3) == 3 ? 1 : 0) &&
                    manager.Danmaku.Count == ((mask & 12) == 12 ? 1 : 0), "broadcast switches " + mask);
            }
            Check(manager.Subtitles[0] == message, "broadcast JSON escapes decoded");
            manager.Subtitles.Clear();
            manager.Danmaku.Clear();
            manager.isActiveAndEnabled = false;
            Broadcast("announcer_single");
            Check(manager.Subtitles.Count == 0 && manager.Danmaku.Count == 0, "inactive manager receives no broadcast");
            manager.isActiveAndEnabled = true;
            Broadcast("announcer_a"); Broadcast("announcer_b");
            Check(manager.Subtitles.Count == 1 && manager.Subtitles[0] == message, "sequence supports text before clips");
            manager.Subtitles.Clear();
            Broadcast("announcer_a"); LabRadioPatch.ReloadLocaleResources(); Broadcast("announcer_b");
            Check(manager.Subtitles.Count == 0, "locale reload clears partial sequence");
            Broadcast("announcer_a"); LabRadioPatch.ResetPlaybackState(); Broadcast("announcer_b");
            Check(manager.Subtitles.Count == 0, "session reset clears partial sequence");

            string root = args.Length == 0 ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../")) : args[0];
            int files = 0;
            foreach (string dir in new[] { "locales", "presets" })
                foreach (string file in Directory.GetFiles(Path.Combine(root, dir), "*.jsonc", SearchOption.AllDirectories))
                {
                    JToken.Parse(JsoncUtils.StripJsonComments(File.ReadAllText(file)));
                    files++;
                }
            Console.WriteLine("Parsed " + files + " repository JSONC files; " + checks + " regression checks passed.");
            var tables = new Dictionary<string, JObject>();
            foreach (var lang in new[] { "ch", "en", "ru" })
                tables[lang] = JObject.Parse(JsoncUtils.StripJsonComments(File.ReadAllText(Path.Combine(root, "locales", lang, "UI.jsonc"))));
            foreach (var lang in new[] { "en", "ru" })
            {
                foreach (var section in new[] { "Common", "Categories", "Settings" })
                    Check(((JObject)tables["ch"][section]).Count == ((JObject)tables[lang][section]).Count &&
                        ((JObject)tables["ch"][section]).Properties().All(p => tables[lang][section][p.Name] != null), lang + " " + section + " keys match");
                foreach (var property in ((JObject)tables["ch"]["Common"]).Properties())
                {
                    var expected = Regex.Matches((string)property.Value, @"\{\d+[^}]*\}").Select(m => m.Value).OrderBy(v => v);
                    var actual = Regex.Matches((string)tables[lang]["Common"][property.Name], @"\{\d+[^}]*\}").Select(m => m.Value).OrderBy(v => v);
                    if (!expected.SequenceEqual(actual)) throw new Exception("Format placeholder mismatch: " + lang + "/" + property.Name);
                }
            }
            foreach (var dir in new[] { "Config", "Debug", "SettingsUI", "Patch", "Utils", "SubtitleUI", "World3D" })
                foreach (var source in Directory.GetFiles(Path.Combine(root, dir), "*.cs"))
                    foreach (Match match in Regex.Matches(File.ReadAllText(source), "I18n\\.(?:Text|Format)\\(\"([^\"\\r\\n]+)\""))
                    {
                        var key = match.Groups[1].Value;
                        if (key.EndsWith(".")) continue; // 动态枚举、预设及诊断前缀。
                        if (tables["ch"]["Common"][key] == null) throw new Exception("Missing UI key: " + source + ": " + key);
                    }
            Check(true, "all literal UI text keys exist");
            PhraseFilterManager.Root = Path.Combine(root, "locales");
            foreach (var lang in new[] { "ch", "en", "ru" })
            {
                I18n.Reload(lang);
                Check(I18n.Text("Toast.PresetApplied", "MISSING") != "MISSING", lang + " toast localized");
                Check(DiagnosticText.Localize("QUEUED | VOICE: usec_1.jsonc") != "QUEUED | VOICE: usec_1.jsonc", lang + " diagnostic localized");
                Check(I18n.Format("Toast.PresetApplied", "Applied: {0}", "test").Contains("test"), lang + " translated format accepts arguments");
            }
            Directory.CreateDirectory(Path.Combine(temp, "bad"));
            File.WriteAllText(Path.Combine(temp, "bad", "UI.jsonc"), "{\"Common\":{\"broken\":\"bad {\"}}");
            PhraseFilterManager.Root = temp;
            I18n.Reload("bad");
            Check(I18n.Format("broken", "Fallback {0}", "ok") == "Fallback ok", "malformed translation format falls back safely");
            Console.WriteLine("All " + checks + " regression checks passed; UI keys and format placeholders verified.");
        }
        finally { Directory.Delete(temp, true); }
    }
}
