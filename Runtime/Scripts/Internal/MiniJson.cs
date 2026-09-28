// MiniJson — tiny strict JSON parser/serializer used by Pollinations for Unity.
// Deliberately dependency-free (no UnityEngine) so it is unit-testable anywhere.
// Only builds/reads the shapes the Pollinations API uses; not a general JSON library.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pollinations.Unity.Internal
{
    public static class MiniJson
    {
        // ---------- Parse ----------

        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var p = new Parser(json);
            p.SkipWs();
            var value = p.ParseValue();
            p.SkipWs();
            if (!p.AtEnd) throw new FormatException($"Trailing characters at offset {p.Pos}.");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _s;
            public int Pos { get; private set; }

            public Parser(string s) { _s = s; }

            public bool AtEnd => Pos >= _s.Length;

            public void SkipWs()
            {
                while (Pos < _s.Length && (_s[Pos] == ' ' || _s[Pos] == '\t' || _s[Pos] == '\n' || _s[Pos] == '\r'))
                    Pos++;
            }

            private char Peek()
            {
                if (AtEnd) throw new FormatException("Unexpected end of JSON.");
                return _s[Pos];
            }

            private void Expect(char c)
            {
                if (AtEnd || _s[Pos] != c)
                    throw new FormatException($"Expected '{c}' at offset {Pos}.");
                Pos++;
            }

            public object ParseValue()
            {
                SkipWs();
                char c = Peek();
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': ExpectWord("true"); return true;
                    case 'f': ExpectWord("false"); return false;
                    case 'n': ExpectWord("null"); return null;
                    default: return ParseNumber();
                }
            }

            private void ExpectWord(string word)
            {
                if (Pos + word.Length > _s.Length || string.CompareOrdinal(_s, Pos, word, 0, word.Length) != 0)
                    throw new FormatException($"Invalid token at offset {Pos}.");
                Pos += word.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                Expect('{');
                var result = new Dictionary<string, object>();
                SkipWs();
                if (!AtEnd && Peek() == '}') { Pos++; return result; }
                while (true)
                {
                    SkipWs();
                    string key = ParseString();
                    SkipWs();
                    Expect(':');
                    object value = ParseValue();
                    result[key] = value;
                    SkipWs();
                    char c = Peek();
                    if (c == ',') { Pos++; continue; }
                    if (c == '}') { Pos++; return result; }
                    throw new FormatException($"Expected ',' or '}}' at offset {Pos}.");
                }
            }

            private List<object> ParseArray()
            {
                Expect('[');
                var result = new List<object>();
                SkipWs();
                if (!AtEnd && Peek() == ']') { Pos++; return result; }
                while (true)
                {
                    object value = ParseValue();
                    result.Add(value);
                    SkipWs();
                    char c = Peek();
                    if (c == ',') { Pos++; continue; }
                    if (c == ']') { Pos++; return result; }
                    throw new FormatException($"Expected ',' or ']' at offset {Pos}.");
                }
            }

            private string ParseString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new FormatException("Unterminated string.");
                    char c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (AtEnd) throw new FormatException("Unterminated escape.");
                        char e = _s[Pos++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                sb.Append(ReadUnicodeEscape());
                                break;
                            default:
                                throw new FormatException($"Invalid escape '\\{e}' at offset {Pos - 2}.");
                        }
                    }
                    else if (c < 0x20)
                    {
                        throw new FormatException($"Unescaped control character at offset {Pos - 1}.");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            private string ReadUnicodeEscape()
            {
                int first = ReadHex4();
                if (first >= 0xD800 && first <= 0xDBFF)
                {
                    // High surrogate: require an immediately following \uXXXX low surrogate.
                    if (Pos + 1 < _s.Length && _s[Pos] == '\\' && _s[Pos + 1] == 'u')
                    {
                        Pos += 2;
                        int second = ReadHex4();
                        if (second >= 0xDC00 && second <= 0xDFFF)
                        {
                            int codePoint = 0x10000 + ((first - 0xD800) << 10) + (second - 0xDC00);
                            return char.ConvertFromUtf32(codePoint);
                        }
                        throw new FormatException("Unpaired surrogate in \\u escape.");
                    }
                    throw new FormatException("High surrogate without low surrogate in \\u escape.");
                }
                if (first >= 0xDC00 && first <= 0xDFFF)
                    throw new FormatException("Unpaired low surrogate in \\u escape.");
                return ((char)first).ToString();
            }

            private int ReadHex4()
            {
                if (Pos + 4 > _s.Length) throw new FormatException("Incomplete \\u escape.");
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    char c = _s[Pos++];
                    int d;
                    if (c >= '0' && c <= '9') d = c - '0';
                    else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
                    else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
                    else throw new FormatException($"Invalid hex digit '{c}' in \\u escape.");
                    value = (value << 4) | d;
                }
                return value;
            }

            private double ParseNumber()
            {
                int start = Pos;
                if (!AtEnd && (Peek() == '-' || Peek() == '+')) Pos++;
                bool anyDigit = false;
                while (!AtEnd)
                {
                    char c = _s[Pos];
                    if (c >= '0' && c <= '9') { anyDigit = true; Pos++; }
                    else if (c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+') Pos++;
                    else break;
                }
                if (!anyDigit)
                    throw new FormatException($"Invalid number at offset {start}.");
                var text = _s.Substring(start, Pos - start);
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return d;
                throw new FormatException($"Unparseable number '{text}' at offset {start}.");
            }
        }

        // ---------- Serialize ----------

        public static string ToJson(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string s) { WriteString(sb, s); return; }
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (value is int i) { sb.Append(i.ToString(CultureInfo.InvariantCulture)); return; }
            if (value is long l) { sb.Append(l.ToString(CultureInfo.InvariantCulture)); return; }
            if (value is double d) { WriteDouble(sb, d); return; }
            if (value is float f) { WriteDouble(sb, f); return; }

            if (value is IDictionary<string, object> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    Write(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }

            if (value is IEnumerable<object> list)
            {
                sb.Append('[');
                bool firstItem = true;
                foreach (var item in list)
                {
                    if (!firstItem) sb.Append(',');
                    firstItem = false;
                    Write(sb, item);
                }
                sb.Append(']');
                return;
            }

            throw new ArgumentException($"MiniJson.ToJson does not support {value.GetType()}.");
        }

        private static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
                throw new ArgumentException("Cannot serialize NaN/Infinity to JSON.");
            // Whole numbers serialize without a decimal point so integer fields stay integers.
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
            {
                sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------- Convenience accessors ----------

        public static object Get(object obj, string key)
        {
            if (obj is IDictionary<string, object> dict && dict.TryGetValue(key, out var v)) return v;
            return null;
        }

        public static string GetString(object obj, string key)
        {
            var v = Get(obj, key);
            return v as string;
        }

        public static double? GetNumber(object obj, string key)
        {
            var v = Get(obj, key);
            if (v is double d) return d;
            if (v is long l) return l;
            if (v is int i) return i;
            return null;
        }
    }
}
