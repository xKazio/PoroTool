using System.Text;

namespace PoroTool
{
    /// <summary>
    /// Minimal JSON pretty-printer for the API console. The bundled
    /// SimpleJson can only serialize compact, so indentation is done by
    /// walking the raw text while tracking string literals.
    /// </summary>
    static class JsonPretty
    {
        public static string Format(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json ?? "";

            string trimmed = json.TrimStart();
            if (trimmed[0] != '{' && trimmed[0] != '[') return json;

            var result = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inString = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    result.Append(c);
                    if (c == '\\' && i + 1 < json.Length) result.Append(json[++i]);
                    else if (c == '"') inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        result.Append(c);
                        break;
                    case '{':
                    case '[':
                        result.Append(c);
                        int next = NextNonWhitespace(json, i + 1);
                        if (next >= 0 && (json[next] == '}' || json[next] == ']'))
                        {
                            // Keep empty objects/arrays on one line.
                            result.Append(json[next]);
                            i = next;
                        }
                        else
                        {
                            indent++;
                            AppendNewLine(result, indent);
                        }
                        break;
                    case '}':
                    case ']':
                        indent--;
                        AppendNewLine(result, indent);
                        result.Append(c);
                        break;
                    case ',':
                        result.Append(c);
                        AppendNewLine(result, indent);
                        break;
                    case ':':
                        result.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(c)) result.Append(c);
                        break;
                }
            }

            return result.ToString();
        }

        private static int NextNonWhitespace(string json, int start)
        {
            for (int i = start; i < json.Length; i++)
                if (!char.IsWhiteSpace(json[i])) return i;
            return -1;
        }

        private static void AppendNewLine(StringBuilder result, int indent)
        {
            // \r\n so the output reads correctly inside a WinForms TextBox.
            result.Append("\r\n");
            result.Append(' ', indent * 4);
        }
    }
}
