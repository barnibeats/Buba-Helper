// Каталоги генератора: языки, раскладки, часовые пояса, редакции и ключи, твики, приложения, программы.
// Данные выгружены из исходников генератора (devtools/export-gen-data.js) и вшиты в exe как gendata.json.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace WinInstaller
{
    class Ed { public string Name, Key; public string[] Os; }
    class BloatItem { public string Id, Label, Os = ""; }
    class Pkg { public string Id, Name, Winget, Choco; }
    class Tw
    {
        public string Id, Label, Hint, Warn, Os; public bool Rec; public string[] Lines;
        // Дополнения Buba-Helper (см. GenExtras.cs). Строки - PowerShell; в UserLines/AdminLines/OtherLines доступна функция SetReg(путь, имя, значение, тип) для HKCU:\
        public string[] UserLines = new string[0];      // для каждого пользователя при его первом входе (Active Setup)
        public string[] AdminLines = new string[0];     // только для встроенного Администратора (SID …-500)
        public string[] OtherLines = new string[0];     // для всех, кроме встроенного Администратора
        public string[] HidePages = new string[0];      // страницы «Параметров» (ms-settings:), скрытые у всех
        public string[] HidePagesOther = new string[0]; // страницы, скрытые у всех, кроме встроенного Администратора
        public string[] RunAtLogon = new string[0];     // скрипты из Files, запускаемые без окна при каждом входе
        public Dictionary<string, string> Files = new Dictionary<string, string>();   // имя файла → содержимое (кладётся в C:\Windows\Setup\Scripts)
        public string Special;                          // особая обработка в Generator.cs
    }
    class TwGroup { public string Name; public List<Tw> Items = new List<Tw>(); }
    class PresetInfo { public string Id, Title, Desc; }

    static class Cat
    {
        public static string Version;
        public static List<string[]> Langs, Kbds, Timezones, Features, Days;   // [код, подпись]
        public static List<Ed> Editions, Gvlk;
        public static List<string> RegTypes, StdBloat;
        public static List<BloatItem> Bloat;
        public static List<Pkg> Packages;
        public static Regex PkgIdRe;
        public static List<TwGroup> Tweaks;
        public static Dictionary<string, Tw> TwIndex = new Dictionary<string, Tw>();
        public static List<PresetInfo> Presets;

        static Cat()
        {
            string text;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinInstaller.gendata.json"))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
            JObj d = (JObj)Json.Parse(text);
            Version = d.S("version");
            Langs = Pairs(d.A("langs")); Kbds = Pairs(d.A("kbds")); Timezones = Pairs(d.A("timezones"));
            Features = Pairs(d.A("features")); Days = Pairs(d.A("days"));
            Editions = Eds(d.A("editions")); Gvlk = Eds(d.A("gvlk"));
            RegTypes = Strs(d.A("regTypes")); StdBloat = Strs(d.A("stdBloat"));
            Bloat = new List<BloatItem>();
            foreach (object o in d.A("bloat"))
            {
                List<object> a = (List<object>)o;
                BloatItem b = new BloatItem(); b.Id = (string)a[0]; b.Label = (string)a[1]; b.Os = a.Count > 2 ? (string)a[2] : "";
                Bloat.Add(b);
            }
            Packages = new List<Pkg>();
            foreach (object o in d.A("packages"))
            {
                List<object> a = (List<object>)o;
                Pkg p = new Pkg(); p.Id = (string)a[0]; p.Name = (string)a[1]; p.Winget = (string)a[2]; p.Choco = (string)a[3];
                Packages.Add(p);
            }
            string re = d.S("pkgIdRe");
            if (re.EndsWith("$")) re = re.Substring(0, re.Length - 1) + "\\z";   // как в JS: «$» - только конец строки
            PkgIdRe = new Regex(re);
            Tweaks = new List<TwGroup>();
            foreach (JObj g in d.Objs("tweaks"))
            {
                TwGroup tg = new TwGroup(); tg.Name = g.S("g");
                foreach (JObj it in g.Objs("items"))
                {
                    Tw t = new Tw();
                    t.Id = it.S("id"); t.Label = it.S("label"); t.Hint = it.S("hint"); t.Warn = it.S("warn"); t.Os = it.S("os"); t.Rec = it.B("rec");
                    t.Lines = Strs(it.A("lines")).ToArray();
                    tg.Items.Add(t);
                    TwIndex[t.Id] = t;
                }
                Tweaks.Add(tg);
            }
            Presets = new List<PresetInfo>();
            foreach (JObj p in d.Objs("presets"))
            {
                PresetInfo pi = new PresetInfo(); pi.Id = p.S("id"); pi.Title = p.S("title"); pi.Desc = p.S("desc");
                Presets.Add(pi);
            }
            GenExtras.Patch();   // твики Buba-Helper поверх выгрузки оригинала (gendata.json перезаписывается devtools/export-gen-data.js)
        }

        static List<string> Strs(List<object> a)
        {
            List<string> r = new List<string>();
            foreach (object o in a) r.Add((string)o);
            return r;
        }

        static List<string[]> Pairs(List<object> a)
        {
            List<string[]> r = new List<string[]>();
            foreach (object o in a)
            {
                List<object> p = (List<object>)o;
                r.Add(new string[] { (string)p[0], (string)p[1] });
            }
            return r;
        }

        static List<Ed> Eds(List<object> a)
        {
            List<Ed> r = new List<Ed>();
            foreach (object o in a)
            {
                List<object> p = (List<object>)o;
                Ed e = new Ed(); e.Name = (string)p[0]; e.Key = (string)p[1];
                e.Os = Strs((List<object>)p[2]).ToArray();
                r.Add(e);
            }
            return r;
        }

        public static bool OsOk(Tw t, string os) { return t.Os.Length == 0 || t.Os == os; }
        public static bool BloatOk(BloatItem b, string os) { return b.Os.Length == 0 || b.Os == os; }
        public static bool Has(string[] arr, string v) { return Array.IndexOf(arr, v) >= 0; }
    }
}
