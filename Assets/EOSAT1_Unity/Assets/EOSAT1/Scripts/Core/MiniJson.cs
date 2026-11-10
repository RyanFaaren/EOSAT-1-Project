// EOSAT-1 VR - small, strict, allocation-light JSON reader.
// Objects become Dictionary<string, object>, arrays List<object>, numbers double,
// plus string, bool and null. Anything malformed throws inside and TryParse
// returns false, so a bad line is skipped instead of crashing the headset.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eosat1.Core
{
    public static class MiniJson
    {
        public const int MaxDepth = 32;

        public static bool TryParse(string text, out object value, out string error)
        {
            value = null;
            error = null;
            if (text == null)
            {
                error = "null text";
                return false;
            }
            try
            {
                var p = new Parser(text);
                value = p.ParseDocument();
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                value = null;
                return false;
            }
        }

        // ---------------------------------------------------------------- helpers for reading parsed trees
        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as Dictionary<string, object>;
            return null;
        }

        public static bool TryString(Dictionary<string, object> d, string key, out string s)
        {
            object v;
            s = null;
            if (d == null || !d.TryGetValue(key, out v)) return false;
            s = v as string;
            return s != null;
        }

        public static bool TryNumber(Dictionary<string, object> d, string key, out double x)
        {
            object v;
            x = 0;
            if (d == null || !d.TryGetValue(key, out v) || !(v is double)) return false;
            x = (double)v;
            return MathUtil.IsFinite(x);
        }

        public static bool TryBool(Dictionary<string, object> d, string key, out bool b)
        {
            object v;
            b = false;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return false;
            if (v is bool)
            {
                b = (bool)v;
                return true;
            }
            if (v is double)
            {
                b = (double)v != 0.0;
                return true;
            }
            var s = v as string;
            if (s != null)
            {
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1") { b = true; return true; }
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) || s == "0") { b = false; return true; }
            }
            return false;
        }

        /// <summary>Reads a numeric array of exactly n finite numbers.</summary>
        public static bool TryNumbers(Dictionary<string, object> d, string key, int n, double[] into)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return false;
            var list = v as List<object>;
            if (list == null || list.Count != n || into == null || into.Length < n) return false;
            for (int i = 0; i < n; i++)
            {
                if (!(list[i] is double)) return false;
                double x = (double)list[i];
                if (!MathUtil.IsFinite(x)) return false;
                into[i] = x;
            }
            return true;
        }

        sealed class Parser
        {
            readonly string s;
            int i;
            int depth;

            public Parser(string s)
            {
                this.s = s;
            }

            public object ParseDocument()
            {
                if (s.Length > 0 && s[0] == '﻿') i = 1;   // byte-order mark
                SkipWs();
                object v = ParseValue();
                SkipWs();
                if (i != s.Length) throw new FormatException("trailing characters at " + i);
                return v;
            }

            char Peek()
            {
                return i < s.Length ? s[i] : '\0';
            }

            void SkipWs()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                    else break;
                }
            }

            object ParseValue()
            {
                if (i >= s.Length) throw new FormatException("unexpected end of line");
                char c = s[i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw new FormatException("unexpected character at " + i);
                }
            }

            void Expect(string literal)
            {
                if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                    throw new FormatException("bad literal at " + i);
                i += literal.Length;
            }

            Dictionary<string, object> ParseObject()
            {
                if (++depth > MaxDepth) throw new FormatException("nesting too deep");
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                i++; // '{'
                SkipWs();
                if (Peek() == '}')
                {
                    i++;
                    depth--;
                    return d;
                }
                while (true)
                {
                    SkipWs();
                    if (Peek() != '"') throw new FormatException("expected a key at " + i);
                    string key = ParseString();
                    SkipWs();
                    if (Peek() != ':') throw new FormatException("expected ':' at " + i);
                    i++;
                    SkipWs();
                    d[key] = ParseValue();
                    SkipWs();
                    char c = Peek();
                    if (c == ',')
                    {
                        i++;
                        continue;
                    }
                    if (c == '}')
                    {
                        i++;
                        break;
                    }
                    throw new FormatException("expected ',' or '}' at " + i);
                }
                depth--;
                return d;
            }

            List<object> ParseArray()
            {
                if (++depth > MaxDepth) throw new FormatException("nesting too deep");
                var list = new List<object>();
                i++; // '['
                SkipWs();
                if (Peek() == ']')
                {
                    i++;
                    depth--;
                    return list;
                }
                while (true)
                {
                    SkipWs();
                    list.Add(ParseValue());
                    SkipWs();
                    char c = Peek();
                    if (c == ',')
                    {
                        i++;
                        continue;
                    }
                    if (c == ']')
                    {
                        i++;
                        break;
                    }
                    throw new FormatException("expected ',' or ']' at " + i);
                }
                depth--;
                return list;
            }

            string ParseString()
            {
                i++; // opening quote
                StringBuilder sb = null;
                int start = i;
                while (true)
                {
                    if (i >= s.Length) throw new FormatException("unterminated string");
                    char c = s[i];
                    if (c == '"')
                    {
                        string result;
                        if (sb == null) result = s.Substring(start, i - start);
                        else result = sb.Append(s, start, i - start).ToString();
                        i++;
                        return result;
                    }
                    if (c == '\\')
                    {
                        if (sb == null) sb = new StringBuilder();
                        sb.Append(s, start, i - start);
                        i++;
                        if (i >= s.Length) throw new FormatException("bad escape");
                        char e = s[i];
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
                                if (i + 4 >= s.Length) throw new FormatException("bad unicode escape");
                                int code = 0;
                                for (int k = 1; k <= 4; k++)
                                {
                                    char h = s[i + k];
                                    int digit;
                                    if (h >= '0' && h <= '9') digit = h - '0';
                                    else if (h >= 'a' && h <= 'f') digit = h - 'a' + 10;
                                    else if (h >= 'A' && h <= 'F') digit = h - 'A' + 10;
                                    else throw new FormatException("bad unicode escape");
                                    code = code * 16 + digit;
                                }
                                sb.Append((char)code);
                                i += 4;
                                break;
                            default:
                                throw new FormatException("bad escape");
                        }
                        i++;
                        start = i;
                        continue;
                    }
                    if (c < ' ') throw new FormatException("control character in string");
                    i++;
                }
            }

            double ParseNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length)
                {
                    char c = s[i];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') i++;
                    else break;
                }
                double v;
                if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    throw new FormatException("bad number at " + start);
                return v;
            }
        }
    }
}
