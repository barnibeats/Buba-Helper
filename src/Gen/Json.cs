// Мини-JSON для генератора файлов ответов: без внешних библиотек (в WinPE нет System.Web.Extensions).
// Объекты хранятся с порядком ключей - он важен для проверки настроек и для читаемого файла.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinInstaller
{
    /// <summary>JSON-объект с сохранением порядка ключей и удобными типизированными геттерами.</summary>
    class JObj
    {
        public readonly List<string> Keys = new List<string>();
        readonly Dictionary<string, object> map = new Dictionary<string, object>();

        public object this[string k]
        {
            get { object v; return map.TryGetValue(k, out v) ? v : null; }
            set { if (!map.ContainsKey(k)) Keys.Add(k); map[k] = value; }
        }
        public bool Has(string k) { return map.ContainsKey(k); }
        public void Remove(string k) { if (map.Remove(k)) Keys.Remove(k); }

        public string S(string k) { object v = this[k]; return v == null ? "" : v is string ? (string)v : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public bool B(string k) { object v = this[k]; return v is bool && (bool)v; }
        public int I(string k)
        {
            object v = this[k];
            if (v is int) return (int)v;
            if (v is double) return (int)Math.Truncate((double)v);
            if (v is long) return (int)(long)v;
            return 0;
        }
        public JObj O(string k) { return this[k] as JObj; }
        public List<object> A(string k) { return this[k] as List<object>; }
        public List<JObj> Objs(string k)
        {
            List<JObj> r = new List<JObj>();
            List<object> a = A(k);
            if (a != null) foreach (object o in a) { JObj j = o as JObj; if (j != null) r.Add(j); }
            return r;
        }

        public JObj Clone() { return (JObj)Json.Clone(this); }
    }

    static class Json
    {
        public static object Clone(object v)
        {
            JObj o = v as JObj;
            if (o != null)
            {
                JObj c = new JObj();
                foreach (string k in o.Keys) c[k] = Clone(o[k]);
                return c;
            }
            List<object> a = v as List<object>;
            if (a != null)
            {
                List<object> c = new List<object>();
                foreach (object x in a) c.Add(Clone(x));
                return c;
            }
            return v;
        }

        // ------------------------------------------------------------ разбор

        public static object Parse(string s)
        {
            int i = 0;
            if (s.Length > 0 && s[0] == '﻿') i = 1;
            object v = Value(s, ref i);
            Ws(s, ref i);
            if (i < s.Length) throw new FormatException("лишние данные после JSON в позиции " + i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("неожиданный конец JSON");
            char c = s[i];
            if (c == '{')
            {
                i++;
                JObj o = new JObj();
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new FormatException("ожидалось имя поля в позиции " + i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("ожидалось «:» в позиции " + i);
                    i++;
                    o[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw new FormatException("ожидалось «,» или «}» в позиции " + i);
                }
            }
            if (c == '[')
            {
                i++;
                List<object> a = new List<object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new FormatException("ожидалось «,» или «]» в позиции " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double d;
            if (i == st || !double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("неожиданный символ «" + c + "» в позиции " + st);
            return d;
        }

        static string Str(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++;   // открывающая кавычка
            while (true)
            {
                if (i >= s.Length) throw new FormatException("строка не закрыта");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("строка не закрыта");
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("обрыв \\u-последовательности");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(e); break;   // " \ /
                }
            }
        }

        // ------------------------------------------------------------ запись (как JSON.stringify(x, null, 2))

        public static string Stringify(object v)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, v, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int ind)
        {
            string pad = new string(' ', ind * 2), pad2 = new string(' ', ind * 2 + 2);
            JObj o = v as JObj;
            List<object> a = v as List<object>;
            if (v == null) sb.Append("null");
            else if (v is bool) sb.Append((bool)v ? "true" : "false");
            else if (v is string) Quote(sb, (string)v);
            else if (v is int) sb.Append(((int)v).ToString(CultureInfo.InvariantCulture));
            else if (v is double)
            {
                double d = (double)v;
                sb.Append(d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture));
            }
            else if (o != null)
            {
                if (o.Keys.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                for (int i = 0; i < o.Keys.Count; i++)
                {
                    sb.Append(pad2); Quote(sb, o.Keys[i]); sb.Append(": ");
                    Write(sb, o[o.Keys[i]], ind + 1);
                    sb.Append(i < o.Keys.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(pad).Append('}');
            }
            else if (a != null)
            {
                if (a.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int i = 0; i < a.Count; i++)
                {
                    sb.Append(pad2);
                    Write(sb, a[i], ind + 1);
                    sb.Append(i < a.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(pad).Append(']');
            }
            else sb.Append("null");
        }

        static void Quote(StringBuilder sb, string s)
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
