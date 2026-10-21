// Состояние генератора: значения по умолчанию и проверка загружаемых настроек (порт core/state.js).
// Состояние - это JObj того же вида, что JSON из кнопки «Сохранить настройки» оригинального генератора.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WinInstaller
{
    static class GenState
    {
        /// <summary>Собирает JObj из пар «ключ, значение».</summary>
        public static JObj J(params object[] kv)
        {
            JObj o = new JObj();
            for (int i = 0; i < kv.Length; i += 2) o[(string)kv[i]] = kv[i + 1];
            return o;
        }
        public static List<object> L(params object[] items) { return new List<object>(items); }

        public static JObj DefaultTask() { return J("name", "", "program", "", "args", "", "workdir", "", "trigger", "logon", "time", "03:00", "days", L("Monday"), "n", 15, "runAs", "system", "user", "", "pass", "", "highest", true, "runNow", false); }
        public static JObj DefaultCmd() { return J("desc", "", "phase", "firstlogon", "kind", "ps", "body", ""); }
        public static JObj DefaultUser() { return J("name", "", "display", "", "pass", "", "group", "Administrators", "desc", "", "neverExp", true, "hidden", false); }
        public static JObj DefaultReg() { return J("key", "HKLM\\SOFTWARE\\", "name", "", "type", "REG_DWORD", "data", ""); }

        public static JObj Defaults()
        {
            JObj u = DefaultUser(); u["name"] = "User";
            return J(
                "mode", "clean",
                "os", "10",
                "sysprep", J("copyProfile", false, "skipRearm", true, "dnsOn", false, "dnsIface", "Ethernet", "dns1", "", "dns2", "", "rename", "none", "renamePrefix", "PC",
                    "cleanup", true, "clearEvt", true, "services", "", "after", "shutdown", "preScript", ""),
                "aclPaths", "",
                "arch", "amd64",
                "locale", J("ui", "ru-RU", "user", "ru-RU", "system", "ru-RU", "kbd", L("0409:00000409", "0419:00000419"), "kbdDefault", "0409:00000409", "tz", "Russian Standard Time"),
                "install", J("editionMode", "generic", "edition", "Pro", "key", "", "index", 1,
                    "bypass", J("tpm", false, "sb", false, "ram", false, "cpu", false, "storage", false),
                    "diskMode", "uefi", "diskId", 0, "recovery", true, "recoverySize", 1000, "efiSize", 300,
                    "winSize", 0, "dataPart", false, "dataLabel", "Data", "dataLetter", "D", "dataDisk", false, "dataDiskId", 1, "dataDiskLetter", "E"),
                "sys", J("name", "", "owner", "", "org", "", "netMode", "workgroup", "workgroup", "WORKGROUP", "domain", "", "domUser", "", "domPass", "", "domOU", "",
                    "hideEula", true, "hideOEM", true, "hideOnline", true, "hideWireless", true, "express", true, "netLoc", "Work", "reboot", false,
                    "wifiOn", false, "ssid", "", "wifiPass", "", "wifiAuth", "WPA2PSK"),
                "users", L(u),
                "autologon", J("on", false, "user", 0, "count", 1, "always", false),
                "admin", J("enable", false, "pass", "", "hide", false),
                "obfuscate", false,
                "remote", J("rdp", false, "rdpPort", 3389, "nla", true, "winrm", false, "ping", false, "fps", false, "discovery", false),
                "net", J("static", false, "ip", "", "prefix", 24, "gw", "", "dns1", "", "dns2", "", "ntp", ""),
                "act", J("mode", "none", "edition", "auto", "key", "", "kmsHost", "", "kmsPort", 1688, "custom", false),
                "def", J("mode", "keep", "exclPaths", "", "exclExt", "", "exclProc", ""),
                "tweaks", J("fileext", true, "hidden", false, "thispc", true, "pwdexp", true, "nolockout", true),
                "bloat", J(),
                "apps", J("mgr", "winget", "pkgs", J(), "custom", ""),
                "regs", L(),
                "cmds", L(),
                "tasks", L());
        }

        // ------------------------------------------------------------ допустимые значения

        static readonly Dictionary<string, List<string>> Enums = BuildEnums();
        static readonly Dictionary<string, int[]> Ranges = BuildRanges();
        static readonly string[] ListPaths = { "users", "regs", "cmds", "tasks" };
        static readonly string[] BoolMaps = { "tweaks", "bloat", "apps.pkgs" };
        static readonly object Undef = new object();

        static List<string> Firsts(List<string[]> l) { List<string> r = new List<string>(); foreach (string[] x in l) r.Add(x[0]); return r; }
        static List<string> Names(List<Ed> l) { List<string> r = new List<string>(); foreach (Ed e in l) r.Add(e.Name); return r; }
        static List<string> S(params string[] a) { return new List<string>(a); }

        static Dictionary<string, List<string>> BuildEnums()
        {
            Dictionary<string, List<string>> e = new Dictionary<string, List<string>>();
            e["mode"] = S("clean", "sysprep");
            e["os"] = S("10", "11");
            e["arch"] = S("amd64", "x86", "arm64");
            e["locale.ui"] = Firsts(Cat.Langs); e["locale.user"] = Firsts(Cat.Langs); e["locale.system"] = Firsts(Cat.Langs);
            e["locale.kbdDefault"] = Firsts(Cat.Kbds);
            e["locale.tz"] = Firsts(Cat.Timezones);
            e["install.editionMode"] = S("generic", "custom", "index", "ask");
            e["install.edition"] = Names(Cat.Editions);
            e["install.diskMode"] = S("uefi", "bios", "manual");
            e["sys.netMode"] = S("workgroup", "domain");
            e["sys.netLoc"] = S("Work", "Home", "Other", "");
            e["sys.wifiAuth"] = S("WPA2PSK", "open");
            e["sysprep.rename"] = S("none", "serial", "mac");
            e["sysprep.after"] = S("shutdown", "reboot", "quit");
            e["def.mode"] = S("keep", "off", "hard");
            e["act.mode"] = S("none", "kms", "key");
            List<string> ae = S("auto"); ae.AddRange(Names(Cat.Gvlk)); e["act.edition"] = ae;
            e["apps.mgr"] = S("winget", "choco");
            e["users[].group"] = S("Administrators", "Users");
            e["regs[].type"] = Cat.RegTypes;
            e["cmds[].phase"] = S("specialize", "firstlogon");
            e["cmds[].kind"] = S("ps", "cmd", "exe");
            e["tasks[].trigger"] = S("logon", "startup", "daily", "weekly", "once", "interval");
            e["tasks[].runAs"] = S("system", "users", "current", "user");
            return e;
        }

        static Dictionary<string, int[]> BuildRanges()
        {
            Dictionary<string, int[]> r = new Dictionary<string, int[]>();
            r["install.index"] = new int[] { 1, 99 }; r["install.diskId"] = new int[] { 0, 99 }; r["install.dataDiskId"] = new int[] { 0, 99 };
            r["install.recoverySize"] = new int[] { 100, 1000000 }; r["install.efiSize"] = new int[] { 50, 100000 }; r["install.winSize"] = new int[] { 0, 100000000 };
            r["remote.rdpPort"] = new int[] { 1, 65535 }; r["net.prefix"] = new int[] { 1, 32 }; r["act.kmsPort"] = new int[] { 1, 65535 };
            r["autologon.count"] = new int[] { 1, 999999999 }; r["autologon.user"] = new int[] { -1, 999 }; r["tasks[].n"] = new int[] { 1, 1000000 };
            return r;
        }

        static JObj MakeItem(string path)
        {
            switch (path)
            {
                case "users": return DefaultUser();
                case "regs": return DefaultReg();
                case "cmds": return DefaultCmd();
                default: return DefaultTask();
            }
        }

        static object Get(object v, string k)
        {
            JObj o = v as JObj;
            return o != null && o.Has(k) ? o[k] : Undef;
        }

        static string Plain(object v) { return v is string ? (string)v : ""; }

        /// <summary>Приводит произвольные данные к форме шаблона tpl; что не подошло - берётся из шаблона, причина пишется в P.</summary>
        static object Coerce(object tpl, object v, string path, List<string> P)
        {
            string where = path.Length == 0 ? "корень" : path;
            Action<string> bad = delegate(string why) { P.Add(where + ": " + why); };

            List<object> tl = tpl as List<object>;
            if (tl != null)
            {
                if (path == "locale.kbd")
                {
                    List<object> va = v as List<object>;
                    if (va == null) { if (v != Undef) bad("ожидался список"); return new List<object>(tl); }
                    List<object> kr = new List<object>();
                    foreach (object x in va) if (x is string && Enums["locale.kbdDefault"].Contains((string)x)) kr.Add(x);
                    return kr;
                }
                if (path == "tasks[].days")
                {
                    List<object> days = new List<object>();
                    List<object> va = v as List<object>;
                    if (va != null) foreach (string[] d in Cat.Days) if (va.Contains(d[0])) days.Add(d[0]);
                    if (v != Undef && va == null) bad("ожидался список дней");
                    return days.Count > 0 ? days : new List<object>(tl);
                }
                if (Array.IndexOf(ListPaths, path) >= 0)
                {
                    List<object> va = v as List<object>;
                    if (va == null) { if (v != Undef) bad("ожидался список"); return Json.Clone(tpl); }
                    List<object> res = new List<object>();
                    for (int i = 0; i < va.Count && i < 500; i++)
                    {
                        JObj it = va[i] as JObj;
                        if (it != null) res.Add(Coerce(MakeItem(path), it, path + "[]", P));
                        else bad("элемент " + (i + 1) + " пропущен (не объект)");
                    }
                    return res;
                }
                return new List<object>(tl);
            }

            JObj to = tpl as JObj;
            if (to != null)
            {
                if (v != Undef && !(v is JObj)) { bad("ожидался объект"); v = Undef; }
                JObj vo = v as JObj;
                if (Array.IndexOf(BoolMaps, path) >= 0)
                {
                    JObj outm = to.Clone();
                    if (vo != null)
                        foreach (string k in vo.Keys)
                        {
                            if (vo[k] is bool) outm[k] = vo[k];
                            else bad("«" + k + "» — не true/false");
                        }
                    return outm;
                }
                JObj res = new JObj();
                foreach (string k in to.Keys) res[k] = Coerce(to[k], vo != null ? Get(vo, k) : Undef, path.Length > 0 ? path + "." + k : k, P);
                return res;
            }

            if (v == Undef) return tpl;
            if (tpl is bool)
            {
                if (!(v is bool)) { bad("ожидалось true/false"); return tpl; }
                return v;
            }
            if (tpl is int)
            {
                double n = double.NaN;
                if (v is double) n = (double)v;
                else if (v is int) n = (int)v;
                else if (v is string && ((string)v).Trim().Length > 0)
                {
                    double pd;
                    n = double.TryParse(((string)v).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out pd) ? pd : double.NaN;
                }
                if (double.IsNaN(n) || double.IsInfinity(n)) { bad("ожидалось число"); return tpl; }
                double t = Math.Truncate(n);
                int[] rg;
                if (Ranges.TryGetValue(path, out rg))
                {
                    if (t < rg[0] || t > rg[1])
                    {
                        bad("число " + t.ToString("0", CultureInfo.InvariantCulture) + " вне диапазона " + rg[0] + "…" + rg[1]);
                        t = Math.Min(Math.Max(t, rg[0]), rg[1]);
                    }
                }
                else t = Math.Min(Math.Max(t, int.MinValue), int.MaxValue);
                return (int)t;
            }
            // строка
            if (!(v is string)) { bad("ожидалась строка"); return tpl; }
            string sv = (string)v;
            List<string> en;
            if (Enums.TryGetValue(path, out en) && !en.Contains(sv)) { bad("недопустимое значение «" + sv + "»"); return tpl; }
            if (path == "tasks[].time" && !Regex.IsMatch(sv, "^[0-9]{1,2}:[0-9]{2}\\z")) { bad("время «" + sv + "» не в формате ЧЧ:ММ"); return tpl; }
            return sv;
        }

        /// <summary>Приводит загруженные настройки (файл, в том числе от старых версий) к безопасному виду.</summary>
        public static JObj Sanitize(object raw, List<string> P)
        {
            JObj ro = raw as JObj;
            if (ro == null) { P.Add("файл не содержит объект с настройками"); return Defaults(); }
            JObj r = ro.Clone();
            // старые версии хранили название вместе с версией Windows: «Windows 10 Pro» → «Pro»
            Action<JObj> strip = delegate(JObj o)
            {
                if (o != null && o.Has("edition") && o["edition"] is string) o["edition"] = Regex.Replace((string)o["edition"], "^Windows (10|11) ", "");
            };
            strip(r.O("install")); strip(r.O("act"));
            JObj res = (JObj)Coerce(Defaults(), r, "", P);
            if (res.O("autologon").I("user") >= res.Objs("users").Count) res.O("autologon")["user"] = 0;   // -1 = встроенный администратор
            return res;
        }
    }
}
