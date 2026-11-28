using System.Text;

namespace Subtitle.Utils
{
   // 去掉 JSONC 注释（// 和 /* */），避免依赖外部库
   // 注意：逐字符扫描，字符串字面量内的 // 不会被误删
   public static class JsoncUtils
   {
        public static string StripJsonComments(string src)
        {
            if (string.IsNullOrEmpty(src)) return src;
            var sb = new StringBuilder(src.Length);
            bool inStr = false;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '"')
                {
                    // 处理转义引号
                    int backslashes = 0;
                    for (int j = i - 1; j >= 0 && src[j] == '\\'; j--) backslashes++;
                    bool escaped = (backslashes % 2) != 0;
                    if (!escaped) inStr = !inStr;
                    sb.Append(c);
                }
                else if (!inStr && c == '/' && i + 1 < src.Length)
                {
                    char n = src[i + 1];
                    // 行注释 //
                    if (n == '/')
                    {
                        i += 2;
                        while (i < src.Length && src[i] != '\n') i++;
                        sb.Append('\n');
                    }
                    // 块注释 /* ... */
                    else if (n == '*')
                    {
                        // 保留空白和行号，避免把相邻 token 拼接成另一个合法值。
                        sb.Append(' ');
                        i += 2;
                        while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/'))
                        {
                            if (src[i] == '\r' || src[i] == '\n') sb.Append(src[i]);
                            i++;
                        }
                        if (i + 1 >= src.Length)
                            throw new System.FormatException("Unterminated JSONC block comment.");
                        i++; // 跳过 '/'
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
   }
}
