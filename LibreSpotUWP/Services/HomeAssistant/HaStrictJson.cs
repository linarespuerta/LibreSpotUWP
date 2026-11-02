namespace LibreSpotUWP.Services.HomeAssistant
{
    // RFC 8259 syntax check, run before Newtonsoft parses a frame. Newtonsoft is lenient (comments,
    // single quotes, unquoted names, NaN/Infinity, trailing commas); the Python side uses the strict
    // standard parser, so both ends must reject exactly the same non-standard input.
    internal static class HaStrictJson
    {
        public static bool IsValid(string text, int maxDepth)
        {
            if (text == null) return false;
            var position = 0;
            SkipWhitespace(text, ref position);
            if (!Value(text, ref position, 0, maxDepth)) return false;
            SkipWhitespace(text, ref position);
            return position == text.Length;
        }

        private static bool Value(string text, ref int position, int depth, int maxDepth)
        {
            if (position >= text.Length) return false;
            switch (text[position])
            {
                case '{': return depth < maxDepth && Object(text, ref position, depth + 1, maxDepth);
                case '[': return depth < maxDepth && Array(text, ref position, depth + 1, maxDepth);
                case '"': return String(text, ref position);
                case 't': return Literal(text, ref position, "true");
                case 'f': return Literal(text, ref position, "false");
                case 'n': return Literal(text, ref position, "null");
                default: return Number(text, ref position);
            }
        }

        private static bool Object(string text, ref int position, int depth, int maxDepth)
        {
            position++; // {
            SkipWhitespace(text, ref position);
            if (position < text.Length && text[position] == '}') { position++; return true; }
            while (true)
            {
                if (position >= text.Length || text[position] != '"' || !String(text, ref position)) return false;
                SkipWhitespace(text, ref position);
                if (position >= text.Length || text[position] != ':') return false;
                position++;
                SkipWhitespace(text, ref position);
                if (!Value(text, ref position, depth, maxDepth)) return false;
                SkipWhitespace(text, ref position);
                if (position >= text.Length) return false;
                if (text[position] == '}') { position++; return true; }
                if (text[position] != ',') return false;
                position++;
                SkipWhitespace(text, ref position);
            }
        }

        private static bool Array(string text, ref int position, int depth, int maxDepth)
        {
            position++; // [
            SkipWhitespace(text, ref position);
            if (position < text.Length && text[position] == ']') { position++; return true; }
            while (true)
            {
                if (!Value(text, ref position, depth, maxDepth)) return false;
                SkipWhitespace(text, ref position);
                if (position >= text.Length) return false;
                if (text[position] == ']') { position++; return true; }
                if (text[position] != ',') return false;
                position++;
                SkipWhitespace(text, ref position);
            }
        }

        private static bool String(string text, ref int position)
        {
            position++; // opening quote
            while (position < text.Length)
            {
                var c = text[position++];
                if (c == '"') return true;
                if (c < 0x20) return false;
                if (c != '\\') continue;
                if (position >= text.Length) return false;
                var escape = text[position++];
                if (escape == 'u')
                {
                    if (position + 4 > text.Length) return false;
                    for (var i = 0; i < 4; i++)
                        if (!IsHex(text[position + i])) return false;
                    position += 4;
                }
                else if ("\"\\/bfnrt".IndexOf(escape) < 0)
                {
                    return false;
                }
            }
            return false;
        }

        private static bool Number(string text, ref int position)
        {
            var start = position;
            if (position < text.Length && text[position] == '-') position++;
            if (position >= text.Length) return false;
            if (text[position] == '0') position++;
            else if (text[position] >= '1' && text[position] <= '9') Digits(text, ref position);
            else return false;
            if (position < text.Length && text[position] == '.')
            {
                position++;
                if (!Digits(text, ref position)) return false;
            }
            if (position < text.Length && (text[position] == 'e' || text[position] == 'E'))
            {
                position++;
                if (position < text.Length && (text[position] == '+' || text[position] == '-')) position++;
                if (!Digits(text, ref position)) return false;
            }
            return position > start;
        }

        private static bool Digits(string text, ref int position)
        {
            var start = position;
            while (position < text.Length && text[position] >= '0' && text[position] <= '9') position++;
            return position > start;
        }

        private static bool Literal(string text, ref int position, string literal)
        {
            if (string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0) return false;
            position += literal.Length;
            return true;
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static void SkipWhitespace(string text, ref int position)
        {
            while (position < text.Length)
            {
                var c = text[position];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return;
                position++;
            }
        }
    }
}
