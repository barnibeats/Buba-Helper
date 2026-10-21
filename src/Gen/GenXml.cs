// Мини-построитель XML для файлов ответов (порт core/xml.js): узлы, экранирование, CDATA, Base64-пароли.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinInstaller
{
    class XN
    {
        public string Name;
        public List<string[]> Attrs = new List<string[]>();   // пары {имя, значение} в порядке добавления
        public List<object> Kids = new List<object>();         // string | XN | XCData
    }

    class XCData { public string Text; }

    static class X
    {
        public const string NsWcm = "http://schemas.microsoft.com/WMIConfig/2002/State";
        public const string Scr = "C:\\Windows\\Setup\\Scripts";
        public static readonly string[] Add = { "wcm:action", "add" };

        public static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        /// <summary>Пары имя/значение атрибутов: A("pass", "specialize", ...).</summary>
        public static string[] A(params string[] kv) { return kv; }

        /// <summary>Узел. Дети: null, false, "" и 0 отбрасываются (как в JS), вложенные списки разворачиваются.</summary>
        public static XN E(string name, string[] attrs, params object[] kids)
        {
            XN n = new XN();
            n.Name = name;
            if (attrs != null) for (int i = 0; i + 1 < attrs.Length; i += 2) n.Attrs.Add(new string[] { attrs[i], attrs[i + 1] });
            Flatten(n.Kids, kids);
            return n;
        }

        static void Flatten(List<object> dst, object item)
        {
            if (item == null) return;
            if (item is bool) { return; }                       // false отбрасывается; true в детях не используется
            if (item is int && (int)item == 0) return;
            string s = item as string;
            if (s != null) { if (s.Length > 0) dst.Add(s); return; }
            if (item is XN || item is XCData) { dst.Add(item); return; }
            IEnumerable en = item as IEnumerable;
            if (en != null) { foreach (object o in en) Flatten(dst, o); return; }
            dst.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
        }

        public static string Str(object v) { return Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static XN S(string name, object val) { return E(name, null, Str(val)); }
        public static XN B(string name, bool val) { return E(name, null, val ? "true" : "false"); }
        public static XCData CData(string text) { XCData c = new XCData(); c.Text = text.Replace("]]>", "]]]]><![CDATA[>"); return c; }

        public static string Ser(XN n, int ind)
        {
            string pad = new string(' ', ind * 2);
            StringBuilder at = new StringBuilder();
            foreach (string[] a in n.Attrs) at.Append(' ').Append(a[0]).Append("=\"").Append(Esc(a[1])).Append('"');
            if (n.Kids.Count == 0) return pad + "<" + n.Name + at + " />";
            if (n.Kids.Count == 1 && n.Kids[0] is string) return pad + "<" + n.Name + at + ">" + Esc((string)n.Kids[0]) + "</" + n.Name + ">";
            if (n.Kids.Count == 1 && n.Kids[0] is XCData) return pad + "<" + n.Name + at + "><![CDATA[" + ((XCData)n.Kids[0]).Text + "]]></" + n.Name + ">";
            List<string> parts = new List<string>();
            foreach (object k in n.Kids)
            {
                XN kn = k as XN;
                if (kn != null) parts.Add(Ser(kn, ind + 1));
                else if (k is string) parts.Add(Esc((string)k));   // смешанное содержимое в генераторе не используется
            }
            return pad + "<" + n.Name + at + ">\n" + string.Join("\n", parts.ToArray()) + "\n" + pad + "</" + n.Name + ">";
        }

        /// <summary>Base64 от UTF-16LE (так Windows хранит пароли в файле ответов).</summary>
        public static string B64Utf16(string s)
        {
            byte[] b = new byte[s.Length * 2];
            for (int i = 0; i < s.Length; i++) { b[i * 2] = (byte)(s[i] & 255); b[i * 2 + 1] = (byte)(s[i] >> 8); }
            return Convert.ToBase64String(b);
        }

        public static XN PwNode(string tag, string pw, string suffix, bool obf)
        {
            return obf ? E(tag, null, S("Value", B64Utf16(pw + suffix)), S("PlainText", "false"))
                       : E(tag, null, S("Value", pw), S("PlainText", "true"));
        }
    }
}
