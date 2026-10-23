// Пресеты: готовые наборы настроек одним кликом (порт data/presets.js). Список и описания берутся из gendata.json,
// сама логика (какие твики включить) написана здесь и сверяется с оригиналом скриптом devtools/gen-compare.js.
using System;
using System.Collections.Generic;

namespace WinInstaller
{
    static class GenPresets
    {
        static void On(JObj map, params string[] ids) { foreach (string id in ids) map[id] = true; }

        static void Base(JObj s)
        {
            JObj d = GenState.Defaults();
            s["tweaks"] = new JObj(); s["bloat"] = new JObj();
            s["def"] = d["def"]; s["remote"] = d["remote"]; s["apps"] = d["apps"];
            s.O("autologon")["always"] = false;
        }

        /// <summary>«Windows 10 Pro без OOBE»: Pro по универсальному ключу, ручная разметка диска (ничего не стирается), все экраны OOBE пропущены, один локальный пользователь.</summary>
        public static void Oobe(JObj s, string user, string pass, string pc)
        {
            s["os"] = "10"; s["mode"] = "clean";
            JObj inst = s.O("install");
            inst["editionMode"] = "generic"; inst["edition"] = "Pro"; inst["diskMode"] = "manual";
            JObj sys = s.O("sys");
            foreach (string k in new string[] { "hideEula", "hideOEM", "hideOnline", "hideWireless", "express" }) sys[k] = true;
            sys["name"] = pc;
            JObj u = GenState.DefaultUser();
            u["name"] = user.Length > 0 ? user : "User";
            u["pass"] = pass;
            s["users"] = GenState.L(u);
            JObj al = s.O("autologon");
            al["on"] = pass.Length > 0;   // автовход без пароля на Windows 10 не работает - включаем только с паролем
            al["user"] = 0; al["count"] = 1; al["always"] = false;
        }

        /// <summary>Заменяет твики, удаляемые приложения, Защитник, удалённый доступ и программы; остальное не трогает.</summary>
        public static void Apply(string id, JObj s)
        {
            Base(s);
            JObj tw = s.O("tweaks"), bl = s.O("bloat");
            Action stdBloat = delegate() { foreach (string b in Cat.StdBloat) bl[b] = true; };
            switch (id)
            {
                case "home":
                    On(tw, "telemetry", "adid", "consumer", "activity", "edgefre", "feeds", "copilot", "chat", "feedback", "autoplay", "longpaths", "nettls", "fileext", "thispc", "pwdexp", "noedgelnk", "w11widgets", "w11recs", "w11recall");
                    stdBloat();
                    break;
                case "office":
                    On(tw, "telemetry", "adid", "consumer", "wer", "edgefre", "feeds", "copilot", "chat", "dopt", "remoteassist", "teredo", "autoplay", "longpaths", "nettls", "fileext", "thispc", "pwdexp", "noreboot", "w11widgets", "w11recs", "w11recall");
                    stdBloat();
                    s["remote"] = GenState.J("rdp", true, "rdpPort", 3389, "nla", true, "winrm", false, "ping", true, "fps", true, "discovery", true);
                    break;
                case "privacy":
                    On(tw, "telemetry", "cortana", "adid", "consumer", "activity", "wer", "feeds", "copilot", "chat", "edgefre", "edgebg", "edgesidebar", "gamedvr", "location", "feedback", "autoplay", "fileext", "w11recall", "w11searchsug", "w11widgets", "w11recs");
                    stdBloat();
                    break;
                case "dev":
                    On(tw, "telemetry", "consumer", "edgefre", "longpaths", "nettls", "fileext", "hidden", "thispc", "dark", "pwdexp", "ft_Microsoft-Windows-Subsystem-Linux", "ft_VirtualMachinePlatform");
                    On(bl, "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.MicrosoftSolitaireCollection", "Microsoft.GetHelp", "Microsoft.Getstarted", "Clipchamp.Clipchamp");
                    s["apps"] = GenState.J("mgr", "winget", "pkgs", new JObj(), "custom", "");
                    On(s.O("apps").O("pkgs"), "git", "vscode", "python", "terminal", "powertoys", "7zip", "notepadpp");
                    break;
                case "kiosk":
                    On(tw, "telemetry", "consumer", "wer", "nolock", "nopwlogon", "highperf", "nosleep", "hibernate", "faststart", "noautoupdate", "noreboot", "sticky", "autoplay", "notaskview", "nosearchbox", "feeds", "copilot", "chat", "edgefre", "edgebg", "gamedvr", "w11widgets");
                    foreach (BloatItem b in Cat.Bloat) if (!b.Id.Contains("WindowsStore")) bl[b.Id] = true;
                    s.O("autologon")["on"] = true; s.O("autologon")["always"] = true;
                    s.O("sys")["reboot"] = true;
                    break;
                case "clean":
                    s["tweaks"] = GenState.Defaults()["tweaks"];
                    break;
                default:
                    throw new ArgumentException("неизвестный пресет: " + id);
            }
        }
    }
}
