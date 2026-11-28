using System;

namespace Subtitle.Utils
{
    internal static class DiagnosticText
    {
        // 事件内部保留稳定状态码；显示和复制报告时才按当前语言翻译。
        internal static string Localize(string status)
        {
            if (string.IsNullOrEmpty(status)) return "-";
            int separator = status.IndexOf(" | ", StringComparison.Ordinal);
            if (separator >= 0)
                return Localize(status.Substring(0, separator)) + " | " + Localize(status.Substring(separator + 3));
            string[] prefixes = { "FILTER: distance ", "VOICE GENERAL: ", "VOICE: ", "DEFAULT GENERAL: ",
                "DEFAULT CH GENERAL: ", "DEFAULT CH: ", "DEFAULT: " };
            string[] keys = { "Distance", "VoiceGeneral", "Voice", "DefaultGeneral", "DefaultChGeneral", "DefaultCh", "Default" };
            for (int i = 0; i < prefixes.Length; i++)
                if (status.StartsWith(prefixes[i], StringComparison.Ordinal))
                    return I18n.Text("Diagnostic." + keys[i], prefixes[i]) + status.Substring(prefixes[i].Length);
            return I18n.Text("Diagnostic." + status, status);
        }
    }
}
