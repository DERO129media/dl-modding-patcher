// Kleiner JSON-Helfer: Lesen ueber JavaScriptSerializer, Schreiben selbst (sortierte Schluessel,
// damit gleiche Einstellungen immer denselben Text und damit dieselbe Pak ergeben).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace Dero
{
    static class Json
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static object Parse(string text)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
            return js.DeserializeObject(text);
        }

        public static Dictionary<string, object> ParseObject(string text)
        {
            var o = Parse(text) as Dictionary<string, object>;
            if (o == null) throw new FormatException("JSON-Objekt erwartet");
            return o;
        }

        public static string Write(object value, bool sortKeys = false)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, sortKeys);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v, bool sort)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is float || v is decimal) { sb.Append(Num(Convert.ToDouble(v, Inv))); return; }
            if (v is int || v is long || v is short || v is byte) { sb.Append(Convert.ToInt64(v, Inv).ToString(Inv)); return; }
            var dict = v as IDictionary;
            if (dict != null)
            {
                var keys = new List<string>();
                foreach (object k in dict.Keys) keys.Add(Convert.ToString(k, Inv));
                if (sort) keys.Sort(StringComparer.Ordinal);
                sb.Append('{');
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteString(sb, keys[i]);
                    sb.Append(':');
                    WriteValue(sb, dict[keys[i]], sort);
                }
                sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object o in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, o, sort);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, v.ToString());
        }

        public static string Num(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(Inv);
            return d.ToString("R", Inv);
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
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------- Bequemes Lesen ----------

        public static Dictionary<string, object> Obj(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static object[] Arr(object o)
        {
            var a = o as object[];
            if (a != null) return a;
            var l = o as IList;
            if (l == null) return null;
            var res = new object[l.Count];
            l.CopyTo(res, 0);
            return res;
        }

        public static bool IsNumber(object o)
        {
            return o is int || o is long || o is decimal || o is double;
        }

        public static double D(object o)
        {
            return Convert.ToDouble(o, Inv);
        }

        public static Dictionary<string, object> O(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2)
                if (kv[i + 1] != null) d[(string)kv[i]] = kv[i + 1];
            return d;
        }
    }
}
