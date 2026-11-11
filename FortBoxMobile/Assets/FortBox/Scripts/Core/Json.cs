// Small JSON reader/writer (no dependencies). Objects are
// Dictionary<string, object>, arrays List<object>, numbers double.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FortBox
{
    public static class Json
    {
        public static object Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int i = 0;
            try { return ReadValue(s, ref i); }
            catch (Exception) { return null; }
        }

        public static Dictionary<string, object> Obj(string s) { return Parse(s) as Dictionary<string, object>; }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object ReadValue(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (c == 't' && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (c == 'f' && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (c == 'n' && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            return ReadNumber(s, ref i);
        }

        static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == '}') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                string key = ReadString(s, ref i);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                d[key] = ReadValue(s, ref i);
            }
            return d;
        }

        static List<object> ReadArray(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == ']') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                l.Add(ReadValue(s, ref i));
            }
            return l;
        }

        static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static object ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double v;
            double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        // ---- writer ----
        public static string Write(object o)
        {
            var sb = new StringBuilder();
            WriteValue(sb, o);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object o)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is string) { WriteString(sb, (string)o); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is IDictionary)
            {
                var d = (IDictionary)o;
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in d)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key.ToString());
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            if (o is IList)
            {
                sb.Append('[');
                bool first = true;
                foreach (var v in (IList)o)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, v);
                }
                sb.Append(']');
                return;
            }
            if (o is float || o is double || o is decimal)
            {
                sb.Append(Convert.ToDouble(o).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (o is int || o is long || o is short || o is byte || o is uint || o is ulong)
            {
                sb.Append(Convert.ToInt64(o).ToString(CultureInfo.InvariantCulture));
                return;
            }
            WriteString(sb, o.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- typed helpers ----
        public static string Str(this Dictionary<string, object> d, string k, string def = "")
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            if (v is double) return ((double)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString();
        }

        public static double Num(this Dictionary<string, object> d, string k, double def = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            if (v is double) return (double)v;
            if (v is bool) return (bool)v ? 1 : 0;
            double r;
            return double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        public static int Int(this Dictionary<string, object> d, string k, int def = 0) { return (int)Math.Round(d.Num(k, def)); }

        public static bool Bool(this Dictionary<string, object> d, string k, bool def = false)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            if (v is double) return (double)v != 0;
            var s = v.ToString().ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes";
        }

        public static Dictionary<string, object> O(this Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v)) return null;
            return v as Dictionary<string, object>;
        }

        public static List<object> L(this Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v)) return null;
            return v as List<object>;
        }

        public static float F(this List<object> l, int i, float def = 0)
        {
            if (l == null || i >= l.Count || l[i] == null) return def;
            return l[i] is double ? (float)(double)l[i] : def;
        }
    }
}
