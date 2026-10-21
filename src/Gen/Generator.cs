// Генератор autounattend.xml / unattend.xml (порт core/generate.js оригинального генератора).
// Порядок блоков, тексты предупреждений и вшиваемые скрипты сохранены дословно: результат должен совпадать с оригиналом
// посимвольно (это проверяет devtools/gen-compare.js).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinInstaller
{
    class Warn { public string Text, Tab, Key; }
    class GenFile { public string Path, Content; }
    class GenResult
    {
        public string Xml;
        public List<Warn> Warnings = new List<Warn>();
        public List<GenFile> Files = new List<GenFile>();
        public int ExtractLen;
        public bool IsSys;
        public string Os;
    }

    static class Generator
    {
        const string AdminLoginName = "Administrator";
        const string PsTestTcp = "function Test-Tcp($h, $p) { try { $c = New-Object Net.Sockets.TcpClient; $r = $c.BeginConnect($h, $p, $null, $null); $ok = $r.AsyncWaitHandle.WaitOne(3000) -and $c.Connected; $c.Close(); return $ok } catch { return $false } }";
        const string PsWaitNet = "for ($i = 0; $i -lt 60 -and -not (Test-Tcp \"www.microsoft.com\" 80); $i++) { Start-Sleep -Seconds 10 }";
        // reg.exe add; пустое имя → значение по умолчанию (/ve), пустые данные → /d ""
        static readonly string PsRegSet = string.Join("\n", new string[] {
            "function RegSet($k, $n, $t, $d) {",
            "    $a = @('add', $k)",
            "    if ($n) { $a += '/v', $n } else { $a += '/ve' }",
            "    $a += '/t', $t, '/d', $(if ($d -eq '') { '\"\"' } else { $d }), '/f'",
            "    reg.exe @a | Out-Null",
            "}" });

        static readonly Regex IpRe = new Regex("^[0-9]{1,3}(\\.[0-9]{1,3}){3}\\z");
        static readonly Regex KeyRe = new Regex("^[A-Z0-9]{5}(-[A-Z0-9]{5}){4}\\z");

        // ------------------------------------------------------------ помощники PowerShell

        /// <summary>Строка PowerShell в одинарных кавычках (внутренние кавычки удваиваются).</summary>
        public static string Q(string s)
        {
            StringBuilder sb = new StringBuilder("'");
            foreach (char c in s)
            {
                sb.Append(c);
                if (c == '\'' || c == '\u2018' || c == '\u2019' || c == '\u201A' || c == '\u201B') sb.Append(c);
            }
            return sb.Append('\'').ToString();
        }

        static string R(string key, string name, string type, string data) { return "RegSet " + Q(key) + " " + Q(name) + " " + type + " " + Q(data); }

        static string Norm(string s) { return Regex.Replace(s ?? "", "\r\n?", "\n").Trim(); }

        static List<string> DfList(string t)
        {
            List<string> r = new List<string>();
            foreach (string p in Regex.Split(t ?? "", "[\r\n;]+")) { string x = p.Trim(); if (x.Length > 0) r.Add(x); }
            return r;
        }

        static int ParseInt(string s)
        {
            int v;
            return int.TryParse((s ?? "").Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        // ------------------------------------------------------------ разметка диска

        class Part { public string Type, Format, Label, Letter, TypeId; public int Size; public bool Extend, Active; }

        static XN MkDisk(int id, List<Part> parts)
        {
            List<XN> create = new List<XN>(), modify = new List<XN>();
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                create.Add(X.E("CreatePartition", X.Add, X.S("Order", i + 1), X.S("Type", p.Type), p.Extend ? X.B("Extend", true) : X.S("Size", p.Size)));
                modify.Add(X.E("ModifyPartition", X.Add,
                    X.S("Order", i + 1), X.S("PartitionID", i + 1),
                    p.Format != null ? X.S("Format", p.Format) : null,
                    !string.IsNullOrEmpty(p.Label) ? X.S("Label", p.Label) : null,
                    !string.IsNullOrEmpty(p.Letter) ? X.S("Letter", p.Letter) : null,
                    !string.IsNullOrEmpty(p.TypeId) ? X.S("TypeID", p.TypeId) : null,
                    p.Active ? X.B("Active", true) : null));
            }
            return X.E("Disk", X.Add, X.S("DiskID", id), X.B("WillWipeDisk", true), X.E("CreatePartitions", null, create), X.E("ModifyPartitions", null, modify));
        }

        class DiskResult { public int WinId; public List<int> Wiped = new List<int>(); public List<XN> Disks = new List<XN>(); }

        static DiskResult DiskNodes(JObj inst, Action<string, string, string> wn)
        {
            List<Part> parts = new List<Part>();
            bool rec = inst.B("recovery");
            if (inst.S("diskMode") == "uefi")
            {
                if (rec) parts.Add(new Part { Type = "Primary", Size = inst.I("recoverySize"), Label = "Recovery", Format = "NTFS", TypeId = "DE94BBA4-06D1-4D40-A16A-BFD50179D6AC" });
                parts.Add(new Part { Type = "EFI", Size = inst.I("efiSize"), Label = "System", Format = "FAT32" });
                parts.Add(new Part { Type = "MSR", Size = 16 });
            }
            else
            {
                if (rec) parts.Add(new Part { Type = "Primary", Size = inst.I("recoverySize"), Label = "Recovery", Format = "NTFS", TypeId = "0x27" });
                parts.Add(new Part { Type = "Primary", Size = 500, Label = "System Reserved", Format = "NTFS", Active = true });
            }
            int winSize = Math.Max(0, inst.I("winSize"));
            if (winSize > 0 && winSize < 20000) wn("Раздел Windows меньше 20 ГБ — для обновлений и программ будет тесно.", "install", "install.winSize");
            parts.Add(winSize > 0 ? new Part { Type = "Primary", Size = winSize, Label = "Windows", Format = "NTFS", Letter = "C" }
                                  : new Part { Type = "Primary", Extend = true, Label = "Windows", Format = "NTFS", Letter = "C" });
            int winId = parts.Count;

            string label = inst.S("dataLabel").Trim();
            if (label.Length > 32) label = label.Substring(0, 32);
            if (label.Length == 0) label = "Data";
            Func<string, string, string, string> letter = delegate(string v, string def, string key)
            {
                string x = (v ?? "").Trim().ToUpperInvariant();
                if (x.EndsWith(":")) x = x.Substring(0, x.Length - 1);
                if (Regex.IsMatch(x, "^[A-Z]\\z") && x != "C" && x != "X") return x;
                wn("Буква диска «" + v + "» недопустима (нужна одна латинская буква, кроме C и X) — использована " + def + ".", "install", key);
                return def;
            };
            List<string> used = new List<string>(); used.Add("C");
            if (inst.B("dataPart") && winSize > 0)
            {
                string L = letter(inst.S("dataLetter"), "D", "install.dataLetter");
                used.Add(L);
                parts.Add(new Part { Type = "Primary", Extend = true, Label = label, Format = "NTFS", Letter = L });
            }
            else if (inst.B("dataPart"))
            {
                wn("Раздел с данными на системном диске: задайте размер раздела Windows, иначе под данные не останется места.", "install", "install.winSize");
            }
            DiskResult res = new DiskResult();
            res.WinId = winId;
            res.Disks.Add(MkDisk(inst.I("diskId"), parts));
            res.Wiped.Add(inst.I("diskId"));
            if (inst.B("dataDisk"))
            {
                if (inst.I("dataDiskId") == inst.I("diskId")) wn("Диск с данными совпадает с системным диском — второй диск не настроен.", "install", "install.dataDiskId");
                else
                {
                    string L = letter(inst.S("dataDiskLetter"), "E", "install.dataDiskLetter");
                    if (used.Contains(L))
                    {
                        wn("Буква " + L + " уже занята другим разделом — буква для второго диска изменена.", "install", "install.dataDiskLetter");
                        L = null;
                        foreach (char c in "ZYWVUTSRQPONMLKJIHGFEDBA") if (!used.Contains(c.ToString()) && c != 'X') { L = c.ToString(); break; }
                    }
                    List<Part> dp = new List<Part>();
                    dp.Add(new Part { Type = "Primary", Extend = true, Label = label, Format = "NTFS", Letter = L });
                    res.Disks.Add(MkDisk(inst.I("dataDiskId"), dp));
                    res.Wiped.Add(inst.I("dataDiskId"));
                }
            }
            return res;
        }

        // ------------------------------------------------------------ основной метод

        class Cmd { public string Desc, Path, CmdLine; }

        public static GenResult Generate(JObj st)
        {
            GenResult result = new GenResult();
            List<Warn> warns = result.Warnings;
            Action<string, string, string> wn = delegate(string text, string tab, string key) { Warn w = new Warn(); w.Text = text; w.Tab = tab; w.Key = key; warns.Add(w); };
            Action<string> wn1 = delegate(string text) { wn(text, null, null); };
            string os = st.S("os") == "11" ? "11" : "10";
            string arch = st.S("arch");
            List<GenFile> files = result.Files;
            List<Cmd> spec = new List<Cmd>();
            List<Cmd> logon = new List<Cmd>();
            List<Cmd> customLogon = new List<Cmd>();
            List<string> defExcl = new List<string>();
            Func<string, string, string> addFile = delegate(string name, string content)
            {
                GenFile f = new GenFile(); f.Path = X.Scr + "\\" + name; f.Content = Norm(content);
                files.Add(f);
                return f.Path;
            };
            Func<string, string> psRun = delegate(string f) { return "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + f + "\""; };
            Func<string, object[], XN> comp = delegate(string name, object[] kids)
            {
                return X.E("component", X.A("name", name, "processorArchitecture", arch, "publicKeyToken", "31bf3856ad364e35", "language", "neutral", "versionScope", "nonSxS",
                    "xmlns:wcm", X.NsWcm, "xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance"), kids);
            };
            Func<string, string, string, string, Cmd> mkSpec = delegate(string d, string p, string c, string x) { Cmd cm = new Cmd(); cm.Desc = d; cm.Path = p; cm.CmdLine = c; return cm; };

            JObj L = st.O("locale"), I = st.O("install"), Y = st.O("sys"), SP = st.O("sysprep");
            bool isSys = st.S("mode") == "sysprep";
            List<string> kbdSel = new List<string>();
            List<object> kbdRaw = L.A("kbd");
            if (kbdRaw != null) foreach (object k in kbdRaw) { string ks = k as string; if (ks != null && Cat.Kbds.Exists(delegate(string[] x) { return x[0] == ks; })) kbdSel.Add(ks); }
            List<string> kbdOrdered = new List<string>();
            if (kbdSel.Contains(L.S("kbdDefault")))
            {
                kbdOrdered.Add(L.S("kbdDefault"));
                foreach (string k in kbdSel) if (k != L.S("kbdDefault")) kbdOrdered.Add(k);
            }
            else kbdOrdered = kbdSel;
            string inputLocale = kbdOrdered.Count > 0 ? string.Join(";", kbdOrdered.ToArray()) : "0409:00000409";
            if (kbdSel.Count == 0) wn("Не выбрана ни одна раскладка клавиатуры — будет использована английская.", "lang", "kbd");

            // ---------- Пользователи ----------
            List<JObj> allUsers = st.Objs("users");
            JObj admin = st.O("admin");
            List<JObj> users = new List<JObj>();
            foreach (JObj u in allUsers) if (u.S("name").Trim().Length > 0) users.Add(u);
            List<string> seen = new List<string>();
            for (int i = 0; i < allUsers.Count; i++)
            {
                string n = allUsers[i].S("name").Trim();
                if (n.Length == 0) continue;
                if (Regex.IsMatch(n, "[\\\\/\\[\\]:;|=,+*?<>\"@]") || n.Length > 20) wn("Имя пользователя «" + n + "»: недопустимые символы или длиннее 20 знаков.", "users", "users:" + i);
                if (seen.Contains(n.ToLowerInvariant())) wn("Имя пользователя «" + n + "» повторяется.", "users", "users:" + i);
                seen.Add(n.ToLowerInvariant());
                if (Regex.IsMatch(n, "^(administrator|администратор)\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) wn("«" + n + "» — имя встроенной учётной записи, выберите другое.", "users", "users:" + i);
            }
            if (users.Count == 0 && !(admin.B("enable") && admin.S("pass").Length > 0))
                wn("Нет ни одного пользователя — на этапе OOBE Windows попросит создать учётную запись вручную. Либо добавьте пользователя, либо включите встроенного «Администратора» с паролем.", "users", "users");

            // ---------- windowsPE (только чистая установка) ----------
            List<XN> settingsPE = new List<XN>();
            string activationKey = null;
            if (!isSys)
            {
                settingsPE.Add(comp("Microsoft-Windows-International-Core-WinPE", new object[] {
                    X.E("SetupUILanguage", null, X.S("UILanguage", L.S("ui"))),
                    X.S("InputLocale", inputLocale), X.S("SystemLocale", L.S("system")), X.S("UILanguage", L.S("ui")), X.S("UserLocale", L.S("user")) }));

                if (os == "11" && I.S("diskMode") == "bios") wn("Windows 11 устанавливается только в режиме UEFI/GPT — выберите UEFI.", "install", "install.diskMode");
                if (os == "11" && arch == "x86") wn("Windows 11 не существует в 32-битной версии — выберите x64 или ARM64.", "install", "arch");

                List<object> setupKids = new List<object>();
                int winPart = 3;
                List<int> wiped = new List<int>();
                if (I.S("diskMode") == "manual")
                {
                    if (I.S("editionMode") == "index") wn("Выбор образа по номеру работает только с автоматической разметкой диска — для ручной разметки выберите другой способ выбора редакции.", "install", "install.editionMode");
                }
                else
                {
                    if (!(I.I("diskId") >= 0)) wn1("Некорректный номер диска.");
                    DiskResult d = DiskNodes(I, wn);
                    winPart = d.WinId;
                    wiped = d.Wiped;
                    setupKids.Add(X.E("DiskConfiguration", null, d.Disks, X.S("WillShowUI", "OnError")));
                    List<object> osImage = new List<object>();
                    if (I.S("editionMode") == "index") osImage.Add(X.E("InstallFrom", null, X.E("MetaData", X.Add, X.S("Key", "/IMAGE/INDEX"), X.S("Value", I.I("index")))));
                    osImage.Add(X.E("InstallTo", null, X.S("DiskID", I.I("diskId")), X.S("PartitionID", winPart)));
                    osImage.Add(X.S("WillShowUI", "OnError"));
                    setupKids.Add(X.E("ImageInstall", null, X.E("OSImage", null, osImage)));
                }
                List<object> userData = new List<object>();
                userData.Add(X.B("AcceptEula", true));
                string em = I.S("editionMode");
                if (em == "generic")
                {
                    Ed ed = Cat.Editions.Find(delegate(Ed e) { return e.Name == I.S("edition") && Cat.Has(e.Os, os); });
                    if (ed == null)
                    {
                        ed = Cat.Editions.Find(delegate(Ed e) { return e.Name == "Pro"; });
                        wn("Редакция «" + I.S("edition") + "» недоступна для Windows " + os + " — использована Pro.", "install", "install.edition");
                    }
                    userData.Add(X.E("ProductKey", null, X.S("Key", ed.Key), X.S("WillShowUI", "OnError")));
                }
                else if (em == "custom")
                {
                    string k = I.S("key").Trim().ToUpperInvariant();
                    if (!KeyRe.IsMatch(k)) wn("Ключ продукта должен иметь вид XXXXX-XXXXX-XXXXX-XXXXX-XXXXX.", "install", "install.key");
                    activationKey = k;
                    userData.Add(X.E("ProductKey", null, X.S("Key", k), X.S("WillShowUI", "OnError")));
                }
                else if (em == "index")
                {
                    userData.Add(X.E("ProductKey", null, X.S("Key", "00000-00000-00000-00000-00000"), X.S("WillShowUI", "OnError")));
                }
                setupKids.Add(X.E("UserData", null, userData));

                // Windows 11: обход проверок оборудования через LabConfig (команды выполняются в среде установки)
                if (os == "11")
                {
                    string[][] bypass = { new string[] { "tpm", "BypassTPMCheck" }, new string[] { "sb", "BypassSecureBootCheck" }, new string[] { "ram", "BypassRAMCheck" },
                                          new string[] { "cpu", "BypassCPUCheck" }, new string[] { "storage", "BypassStorageCheck" } };
                    List<string> bp = new List<string>();
                    JObj bo = I.O("bypass");
                    foreach (string[] b in bypass) if (bo != null && bo.B(b[0])) bp.Add(b[1]);
                    if (bp.Count > 0)
                    {
                        List<XN> cmds = new List<XN>();
                        for (int i = 0; i < bp.Count; i++)
                            cmds.Add(X.E("RunSynchronousCommand", X.Add, X.S("Order", i + 1), X.S("Path", "reg.exe add HKLM\\SYSTEM\\Setup\\LabConfig /v " + bp[i] + " /t REG_DWORD /d 1 /f"), X.S("Description", bp[i])));
                        setupKids.Add(X.E("RunSynchronous", null, cmds));
                        wn("Обход проверок оборудования Windows 11 не поддерживается Microsoft: такая система может не получать часть обновлений.", "install", "install.bypass.tpm");
                    }
                }
                settingsPE.Add(comp("Microsoft-Windows-Setup", new object[] { setupKids }));
                for (int i = 0; i < wiped.Count; i++)
                {
                    Warn w = new Warn(); w.Text = "ВНИМАНИЕ: диск " + wiped[i] + " будет полностью очищен без подтверждения."; w.Tab = "install"; w.Key = i > 0 ? "install.dataDiskId" : "install.diskId";
                    warns.Insert(i, w);
                }
            }

            // ---------- Твики + удаление приложений → Tweaks.ps1 (specialize) ----------
            JObj tweaks = st.O("tweaks");
            Func<string, bool> twOn = delegate(string id) { return tweaks.B(id) && Cat.TwIndex.ContainsKey(id) && Cat.OsOk(Cat.TwIndex[id], os); };
            List<string> tl = new List<string>();
            foreach (TwGroup g in Cat.Tweaks)
                foreach (Tw it in g.Items)
                    if (twOn(it.Id)) { tl.Add("# " + it.Label); tl.AddRange(it.Lines); tl.Add(""); }
            Action<string, string, string> conflict = delegate(string a, string b, string text) { if (twOn(a) && twOn(b)) wn(text, "tweaks", "tw:" + b); };
            conflict("noautoupdate", "notifyupdate", "Конфликт: «Отключить автоматическую установку обновлений» и «Только уведомлять» взаимоисключают друг друга — сработает последняя из них.");
            conflict("uacquiet", "uacoff", "UAC полностью отключён — режим «не запрашивать подтверждение» не имеет значения.");
            conflict("wusvc", "notifyupdate", "Службы обновления отключены целиком — режим «только уведомлять» не будет работать.");

            List<JObj> regs = st.Objs("regs");
            for (int i = 0; i < regs.Count; i++)
            {
                JObj rg = regs[i];
                string key = rg.S("key").Trim();
                key = Regex.Replace(key, "^HKEY_LOCAL_MACHINE", "HKLM", RegexOptions.IgnoreCase);
                key = Regex.Replace(key, "^HKEY_CURRENT_USER", "HKCU", RegexOptions.IgnoreCase);
                key = Regex.Replace(key, "\\\\+\\z", "");
                if (key.Length == 0 && rg.S("name").Trim().Length == 0) continue;
                if (!Regex.IsMatch(key, "^(HKLM|HKCU|HKU|HKCR)\\\\.", RegexOptions.IgnoreCase) || rg.S("name").Trim().Length == 0)
                {
                    wn("Запись реестра №" + (i + 1) + ": укажите ключ (HKLM\\… или HKCU\\…) и имя параметра.", "tweaks", "regs:" + i);
                    continue;
                }
                if (Regex.IsMatch(key, "^HKCU\\\\", RegexOptions.IgnoreCase)) key = "HKU\\DefUser" + key.Substring(4);
                tl.Add("RegSet " + Q(key) + " " + Q(rg.S("name")) + " " + rg.S("type") + " " + Q(rg.S("data")));
            }

            // ---------- Удалённый доступ ----------
            JObj RM = st.O("remote");
            if (RM.B("rdp"))
            {
                int pt = RM.I("rdpPort");
                if (!(pt >= 1 && pt <= 65535)) { wn("Порт RDP должен быть от 1 до 65535 — использован 3389.", "access", "remote.rdpPort"); pt = 3389; }
                string ps = pt.ToString(CultureInfo.InvariantCulture);
                const string TS = "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server";
                tl.Add("# Удалённый рабочий стол (RDP)");
                tl.Add(R(TS, "fDenyTSConnections", "REG_DWORD", "0"));
                tl.Add(R(TS + "\\WinStations\\RDP-Tcp", "PortNumber", "REG_DWORD", ps));
                tl.Add(R(TS + "\\WinStations\\RDP-Tcp", "UserAuthentication", "REG_DWORD", RM.B("nla") ? "1" : "0"));
                tl.Add("netsh.exe advfirewall firewall add rule name=\"RDP TCP " + ps + "\" dir=in action=allow protocol=TCP localport=" + ps + " | Out-Null");
                tl.Add("netsh.exe advfirewall firewall add rule name=\"RDP UDP " + ps + "\" dir=in action=allow protocol=UDP localport=" + ps + " | Out-Null");
                tl.Add(pt == 3389 ? "netsh.exe advfirewall firewall set rule group=\"@FirewallAPI.dll,-28752\" new enable=Yes | Out-Null" : "# нестандартный порт RDP: " + ps);
                tl.Add("");
                if (RM.B("nla") && users.Exists(delegate(JObj x) { return x.S("pass").Length == 0; })) wn("RDP с NLA: у части пользователей нет пароля — удалённо войти под ними не получится.", "access", "remote.nla");
            }
            if (RM.B("ping"))
            {
                tl.Add("# Ответ на ping");
                tl.Add("netsh.exe advfirewall firewall add rule name=\"Allow ICMPv4 Echo\" dir=in action=allow protocol=icmpv4:8,any | Out-Null");
                tl.Add("netsh.exe advfirewall firewall add rule name=\"Allow ICMPv6 Echo\" dir=in action=allow protocol=icmpv6:128,any | Out-Null");
                tl.Add("");
            }
            if (RM.B("fps"))
            {
                tl.Add("# Общий доступ к файлам и принтерам");
                tl.Add("netsh.exe advfirewall firewall set rule group=\"@FirewallAPI.dll,-28502\" new enable=Yes | Out-Null");
                tl.Add("");
            }
            if (RM.B("discovery"))
            {
                tl.Add("# Сетевое обнаружение");
                tl.Add("netsh.exe advfirewall firewall set rule group=\"@FirewallAPI.dll,-32752\" new enable=Yes | Out-Null");
                tl.Add("");
            }
            if (RM.B("winrm"))
            {
                tl.Add("# Разрешить удалённое администрирование локальными учётными записями");
                tl.Add(R("HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System", "LocalAccountTokenFilterPolicy", "REG_DWORD", "1"));
                tl.Add("");
            }

            // ---------- Защитник Windows ----------
            JObj DF = st.O("def");
            const string WD = "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows Defender";
            if (DF.S("mode") != "keep")
            {
                tl.Add("# Защитник Windows: отключение");
                tl.Add(R(WD, "DisableAntiSpyware", "REG_DWORD", "1"));
                tl.Add(R(WD, "DisableAntiVirus", "REG_DWORD", "1"));
                foreach (string n in new string[] { "DisableRealtimeMonitoring", "DisableBehaviorMonitoring", "DisableOnAccessProtection", "DisableScanOnRealtimeEnable", "DisableIOAVProtection" })
                    tl.Add(R(WD + "\\Real-Time Protection", n, "REG_DWORD", "1"));
                tl.Add(R(WD + "\\Spynet", "SpynetReporting", "REG_DWORD", "0"));
                tl.Add(R(WD + "\\Spynet", "SubmitSamplesConsent", "REG_DWORD", "2"));
                tl.Add(R("HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows Defender Security Center\\Notifications", "DisableNotifications", "REG_DWORD", "1"));
                tl.Add("");
                if (DF.S("mode") == "hard")
                {
                    tl.Add("# Защитник Windows: отключение служб и драйверов");
                    tl.Add("foreach ($n in 'WinDefend','WdNisSvc','Sense','WdFilter','WdNisDrv','WdBoot') { if (Test-Path \"HKLM:\\SYSTEM\\CurrentControlSet\\Services\\$n\") { RegSet \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\$n\" 'Start' 'REG_DWORD' 4 } }");
                    tl.Add("");
                }
            }
            List<string> exPaths = new List<string>(), exExt = new List<string>(), exProc = DfList(DF.S("exclProc"));
            foreach (string p in DfList(DF.S("exclPaths"))) exPaths.Add(p.Length > 3 ? Regex.Replace(p, "\\\\+\\z", "") : p);
            foreach (string p in DfList(DF.S("exclExt"))) exExt.Add(p.StartsWith(".") ? p.Substring(1) : p);
            if (exPaths.Count > 0 || exExt.Count > 0 || exProc.Count > 0)
            {
                tl.Add("# Исключения Защитника Windows");
                foreach (string p in exPaths) tl.Add(R(WD + "\\Exclusions\\Paths", p, "REG_SZ", "0"));
                foreach (string p in exExt) tl.Add(R(WD + "\\Exclusions\\Extensions", p, "REG_SZ", "0"));
                foreach (string p in exProc) tl.Add(R(WD + "\\Exclusions\\Processes", p, "REG_SZ", "0"));
                tl.Add("");
                Func<List<string>, string> qj = delegate(List<string> l) { List<string> qs = new List<string>(); foreach (string s in l) qs.Add(Q(s)); return string.Join(",", qs.ToArray()); };
                if (exPaths.Count > 0) defExcl.Add("Add-MpPreference -ExclusionPath " + qj(exPaths));
                if (exExt.Count > 0) defExcl.Add("Add-MpPreference -ExclusionExtension " + qj(exExt));
                if (exProc.Count > 0) defExcl.Add("Add-MpPreference -ExclusionProcess " + qj(exProc));
            }
            if (DF.S("mode") != "keep") defExcl.Insert(0, "Set-MpPreference -DisableRealtimeMonitoring $true -DisableBehaviorMonitoring $true -DisableIOAVProtection $true -DisableScriptScanning $true -MAPSReporting 0 -SubmitSamplesConsent 2");

            // ---------- Права на папки (ACL) ----------
            List<string> aclList = DfList(st.S("aclPaths"));
            if (aclList.Count > 0)
            {
                tl.Add("# Ограничение прав на папки: запись только у администраторов и SYSTEM.");
                tl.Add("# Устраняет повышение прав, когда обычный пользователь может подменить файл, который запускается от SYSTEM (например, из планировщика).");
                foreach (string p in aclList)
                    tl.Add("if (Test-Path " + Q(p) + ") { icacls " + Q(p) + " /inheritance:r /grant:r \"*S-1-5-18:(OI)(CI)F\" \"*S-1-5-32-544:(OI)(CI)F\" \"*S-1-5-32-545:(OI)(CI)RX\" /T /C | Out-Null }");
                tl.Add("");
            }

            // ---------- Скрытые учётные записи ----------
            const string SPEC = "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon\\SpecialAccounts\\UserList";
            foreach (JObj u in users) if (u.B("hidden")) tl.Add(R(SPEC, u.S("name").Trim(), "REG_DWORD", "0"));
            if (admin.B("enable"))
            {
                tl.Add("# Включить встроенного администратора (до создания пользователей, чтобы под ним мог пройти автовход)");
                tl.Add("Get-LocalUser | Where-Object { $_.SID.Value -like 'S-1-5-21-*-500' } | Enable-LocalUser");
                tl.Add("");
            }
            if (admin.B("hide")) tl.Add("$adm = (Get-LocalUser | Where-Object { $_.SID.Value -like 'S-1-5-21-*-500' }).Name; if ($adm) { RegSet " + Q(SPEC) + " $adm 'REG_DWORD' 0 }");

            // ---------- Компоненты Windows ----------
            List<string[]> feats = Cat.Features.FindAll(delegate(string[] f) { return tweaks.B("ft_" + f[0]); });

            JObj bloat = st.O("bloat");
            List<BloatItem> bloatSel = Cat.Bloat.FindAll(delegate(BloatItem b) { return bloat.B(b.Id) && Cat.BloatOk(b, os); });
            List<string> bloatPats = new List<string>();
            foreach (BloatItem b in bloatSel) bloatPats.AddRange(b.Id.Split(';'));
            if (bloatPats.Count > 0)
            {
                List<string> qs = new List<string>();
                foreach (string s in bloatPats) qs.Add(Q(s));
                tl.Add("# Удаление предустановленных приложений");
                tl.Add("$remove = @(" + string.Join(", ", qs.ToArray()) + ")");
                tl.Add("Get-AppxProvisionedPackage -Online | Where-Object { $n = $_.DisplayName; $remove | Where-Object { $n -like $_ } } | ForEach-Object {");
                tl.Add("    Write-Output \"Удаляется: $($_.DisplayName)\"");
                tl.Add("    Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName | Out-Null");
                tl.Add("}");
                tl.Add("");
            }
            if (tl.Count > 0)
            {
                bool needUser = tl.Exists(delegate(string l) { return l.Contains("HKU\\DefUser"); });
                List<string> body = new List<string>();
                body.Add("$ErrorActionPreference = 'Continue'");
                body.Add("Start-Transcript -Path \"$env:SystemRoot\\Setup\\Scripts\\Tweaks.log\" -Force | Out-Null");
                body.Add(PsRegSet);
                if (needUser) body.Add("reg.exe load HKU\\DefUser C:\\Users\\Default\\NTUSER.DAT | Out-Null");
                body.Add("# куст Default обязательно выгружается, даже если одна из команд завершится ошибкой");
                body.Add("try {");
                foreach (string l in tl) body.Add(l.Length == 0 ? "" : "    " + l);
                body.Add("} finally {");
                if (needUser) { body.Add("    [GC]::Collect(); Start-Sleep -Seconds 1"); body.Add("    reg.exe unload HKU\\DefUser | Out-Null"); }
                body.Add("    Stop-Transcript | Out-Null");
                body.Add("}");
                spec.Add(mkSpec("Твики системы и удаление приложений", psRun(addFile("Tweaks.ps1", string.Join("\n", body.ToArray()))), null, null));
            }

            // ---------- Свои команды / скрипты ----------
            List<JObj> cmdList = st.Objs("cmds");
            for (int i = 0; i < cmdList.Count; i++)
            {
                JObj c = cmdList[i];
                string body = Norm(c.S("body"));
                string desc = c.S("desc").Trim();
                if (desc.Length == 0) desc = "Команда " + (i + 1);
                if (body.Length == 0) { wn("Скрипт «" + desc + "» пуст — пропущен.", "scripts", "cmds:" + i); continue; }
                string cmd;
                string kind = c.S("kind");
                if (kind == "exe") cmd = Regex.Replace(body, "\\s*\n\\s*", " ");
                else if (kind == "cmd" && !body.Contains("\n") && ("cmd.exe /c \"" + body + "\"").Length <= 259) cmd = "cmd.exe /c \"" + body + "\"";
                else if (kind == "cmd") cmd = "cmd.exe /c \"" + addFile("custom_" + (i + 1) + ".cmd", "REM script\n@echo off\n" + body) + "\"";
                else cmd = psRun(addFile("custom_" + (i + 1) + ".ps1", body));
                if (cmd.Length > 259) wn("Команда «" + desc + "» длиннее 259 символов — Windows может её не принять. Используйте тип «PowerShell-скрипт».", "scripts", "cmds:" + i);
                if (c.S("phase") == "specialize") spec.Add(mkSpec(desc, cmd, null, null));
                else customLogon.Add(mkSpec(desc, null, cmd, null));
            }

            // ---------- Аккаунты после первого входа ----------
            List<string> acc = new List<string>();
            if (admin.B("enable")) acc.Add("Get-LocalUser | Where-Object { $_.SID.Value -like 'S-1-5-21-*-500' } | Enable-LocalUser");
            foreach (JObj u in users) if (u.B("neverExp")) acc.Add("Set-LocalUser -Name " + Q(u.S("name").Trim()) + " -PasswordNeverExpires $true");
            if (acc.Count > 0) logon.Insert(0, mkSpec("Настройка учётных записей", null, psRun(addFile("Accounts.ps1", string.Join("\n", acc.ToArray()))), null));

            // ---------- Сеть: статический IP, NTP ----------
            JObj NT = st.O("net");
            Func<string, string[]> header = delegate(string log) { return new string[] { "$ErrorActionPreference = 'Continue'", "Start-Transcript -Path \"$env:SystemRoot\\Setup\\Scripts\\" + log + ".log\" -Force | Out-Null" }; };
            List<string> nl = new List<string>();
            if (NT.B("static"))
            {
                int pf = NT.I("prefix");
                if (!IpRe.IsMatch(NT.S("ip").Trim()) || !(pf >= 1 && pf <= 32)) wn("Статический IP: укажите корректный адрес и длину префикса (1–32).", "access", "net.ip");
                else
                {
                    if (NT.S("gw").Trim().Length > 0 && !IpRe.IsMatch(NT.S("gw").Trim())) wn("Статический IP: некорректный шлюз.", "access", "net.gw");
                    List<string> dns = new List<string>();
                    foreach (string x in new string[] { NT.S("dns1"), NT.S("dns2") }) if (x.Trim().Length > 0) dns.Add(x.Trim());
                    if (dns.Exists(delegate(string x) { return !IpRe.IsMatch(x); })) wn("Статический IP: некорректный адрес DNS.", "access", "net.dns1");
                    nl.Add("$a = Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' } | Select-Object -First 1");
                    nl.Add("if (-not $a) { $a = Get-NetAdapter -Physical | Select-Object -First 1 }");
                    nl.Add("if ($a) {");
                    nl.Add("    $i = $a.ifIndex");
                    nl.Add("    Set-NetIPInterface -InterfaceIndex $i -Dhcp Disabled");
                    nl.Add("    Remove-NetRoute -InterfaceIndex $i -DestinationPrefix 0.0.0.0/0 -Confirm:$false -ErrorAction SilentlyContinue");
                    nl.Add("    Get-NetIPAddress -InterfaceIndex $i -AddressFamily IPv4 -ErrorAction SilentlyContinue | Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue");
                    nl.Add("    New-NetIPAddress -InterfaceIndex $i -IPAddress " + Q(NT.S("ip").Trim()) + " -PrefixLength " + pf + (NT.S("gw").Trim().Length > 0 ? " -DefaultGateway " + Q(NT.S("gw").Trim()) : "") + " | Out-Null");
                    if (dns.Count > 0)
                    {
                        List<string> qd = new List<string>(); foreach (string x in dns) qd.Add(Q(x));
                        nl.Add("    Set-DnsClientServerAddress -InterfaceIndex $i -ServerAddresses " + string.Join(",", qd.ToArray()));
                    }
                    else nl.Add("");
                    nl.Add("}");
                }
            }
            if (NT.S("ntp").Trim().Length > 0)
            {
                nl.Add("w32tm.exe /config /manualpeerlist:" + Q(NT.S("ntp").Trim()) + " /syncfromflags:manual /reliable:yes /update | Out-Null");
                nl.Add("Set-Service w32time -StartupType Automatic");
                nl.Add("Start-Service w32time -ErrorAction SilentlyContinue");
                nl.Add("w32tm.exe /resync /force | Out-Null");
            }
            if (nl.Count > 0)
            {
                List<string> nb = new List<string>(header("Network"));
                foreach (string l in nl) if (l.Length > 0) nb.Add(l);
                nb.Add("Stop-Transcript | Out-Null");
                logon.Add(mkSpec("Настройка сети и времени", null, psRun(addFile("Network.ps1", string.Join("\n", nb.ToArray()))), null));
            }

            // ---------- Переименование компьютера после разворачивания образа (только Sysprep) ----------
            if (isSys && SP.S("rename") != "none")
            {
                string pref = Regex.Replace(SP.S("renamePrefix").Trim().Length == 0 ? "PC" : SP.S("renamePrefix").Trim(), "[^A-Za-z0-9-]", "");
                if (pref.Length > 10) pref = pref.Substring(0, 10);
                if (pref.Length == 0) pref = "PC";
                List<string> rn = new List<string>(header("Rename"));
                if (SP.S("rename") == "serial")
                {
                    rn.Add("$sn = (Get-CimInstance Win32_BIOS).SerialNumber -replace '[^A-Za-z0-9-]', ''");
                    rn.Add("$new = (" + Q(pref + "-") + " + $sn) -replace '^-+',''");
                }
                else
                {
                    rn.Add("$mac = (Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | Select-Object -First 1 -ExpandProperty MacAddress) -replace '[:-]', ''");
                    rn.Add("$new = " + Q(pref + "-") + " + $mac");
                }
                rn.Add("$new = $new.Substring(0, [Math]::Min(15, $new.Length))");
                rn.Add("if ($new -and $env:COMPUTERNAME -ne $new) { Rename-Computer -NewName $new -Force -ErrorAction Continue; Write-Output \"Renamed to $new, restart required\" }");
                rn.Add("Stop-Transcript | Out-Null");
                logon.Add(mkSpec("Переименование компьютера (" + (SP.S("rename") == "serial" ? "по серийному номеру" : "по MAC-адресу") + ")", null, psRun(addFile("Rename.ps1", string.Join("\n", rn.ToArray()))), null));
                if (!Y.B("reboot")) wn("Переименование вступит в силу только после перезагрузки — включите «Перезагрузить после выполнения всех команд» на вкладке «Система», иначе до перезагрузки вручную имя не изменится.", "system", "sys.reboot");
            }

            // ---------- WinRM ----------
            if (RM.B("winrm"))
            {
                List<string> wl = new List<string>(header("WinRM"));
                wl.Add("Enable-PSRemoting -Force -SkipNetworkProfileCheck | Out-Null");
                wl.Add("Stop-Transcript | Out-Null");
                logon.Add(mkSpec("Включение удалённого управления PowerShell (WinRM)", null, psRun(addFile("WinRM.ps1", string.Join("\n", wl.ToArray()))), null));
            }

            // ---------- Компоненты Windows и исключения Защитника ----------
            if (feats.Count > 0)
            {
                List<string> fl = new List<string>(header("Features"));
                foreach (string[] f in feats) fl.Add("Enable-WindowsOptionalFeature -Online -FeatureName " + Q(f[0]) + " -All -NoRestart -ErrorAction Continue | Out-Null");
                fl.Add("Stop-Transcript | Out-Null");
                logon.Add(mkSpec("Включение компонентов Windows", null, psRun(addFile("Features.ps1", string.Join("\n", fl.ToArray()))), null));
            }
            if (defExcl.Count > 0)
            {
                List<string> dl = new List<string>(header("DefenderExclusions"));
                foreach (string l in defExcl) dl.Add("try { " + l + " } catch { Write-Warning $_ }");
                dl.Add("Stop-Transcript | Out-Null");
                logon.Add(mkSpec("Настройка Защитника Windows", null, psRun(addFile("DefenderExclusions.ps1", string.Join("\n", dl.ToArray()))), null));
            }

            // ---------- Wi-Fi ----------
            if (Y.B("wifiOn"))
            {
                if (Y.S("ssid").Trim().Length == 0) wn("Wi-Fi: не указано имя сети (SSID).", "system", "sys.ssid");
                if (Y.S("wifiAuth") != "open" && Y.S("wifiPass").Length < 8) wn("Wi-Fi: пароль WPA2 должен содержать не менее 8 символов.", "system", "sys.wifiPass");
                bool open = Y.S("wifiAuth") == "open";
                List<string> wl = new List<string>();
                wl.Add("$ssid = [Security.SecurityElement]::Escape(" + Q(Y.S("ssid")) + ")");
                if (!open) wl.Add("$key  = [Security.SecurityElement]::Escape(" + Q(Y.S("wifiPass")) + ")");
                wl.Add("$xml = @\"");
                wl.Add("<?xml version=\"1.0\"?>");
                wl.Add("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">");
                wl.Add("  <name>$ssid</name>");
                wl.Add("  <SSIDConfig><SSID><name>$ssid</name></SSID></SSIDConfig>");
                wl.Add("  <connectionType>ESS</connectionType>");
                wl.Add("  <connectionMode>auto</connectionMode>");
                wl.Add("  <MSM><security>");
                wl.Add(open ? "    <authEncryption><authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption>"
                            : "    <authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>");
                if (!open) wl.Add("    <sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>$key</keyMaterial></sharedKey>");
                wl.Add("  </security></MSM>");
                wl.Add("</WLANProfile>");
                wl.Add("\"@");
                wl.Add("$f = Join-Path $env:TEMP \"wifi-profile.xml\"");
                wl.Add("$xml | Set-Content -Path $f -Encoding UTF8");
                wl.Add("netsh.exe wlan add profile filename=\"$f\" user=all");
                wl.Add("Remove-Item $f -Force");
                logon.Add(mkSpec("Подключение к Wi-Fi", null, psRun(addFile("Wifi.ps1", string.Join("\n", wl.ToArray()))), null));
            }

            // ---------- Активация Windows ----------
            JObj AC = st.O("act");
            if (AC.S("mode") != "none")
            {
                List<string> al2 = new List<string>(header("Activation"));
                al2.Add(PsTestTcp);
                al2.Add("$slmgr = Join-Path $env:SystemRoot \"System32\\slmgr.vbs\"");
                string key = "";
                if (AC.B("custom") || AC.S("mode") == "key") key = AC.S("key").Trim().ToUpperInvariant();
                else
                {
                    string edName = AC.S("edition");
                    if (edName == "auto")
                    {
                        if (isSys) { wn("Активация KMS: в режиме образа Sysprep редакцию нужно выбрать вручную.", "activation", "act.edition"); edName = null; }
                        else if (I.S("editionMode") == "generic") edName = I.S("edition");
                        else { wn("Активация KMS: редакция установки не выбрана из списка — выберите редакцию для KMS-ключа или укажите свой ключ.", "activation", "act.edition"); edName = null; }
                    }
                    else if (!isSys && I.S("editionMode") == "generic" && I.S("edition") != edName)
                    {
                        wn("Редакция для KMS-ключа («" + edName + "») не совпадает с редакцией установки («" + I.S("edition") + "») — ключ не подойдёт.", "activation", "act.edition");
                    }
                    string en2 = edName;
                    Ed g = en2 == null ? null : Cat.Gvlk.Find(delegate(Ed x) { return x.Name == en2 && Cat.Has(x.Os, os); });
                    key = g != null ? g.Key : "";
                    if (edName != null && edName.Length > 0 && g == null) wn("Активация: для выбранной редакции нет общего ключа KMS — укажите свой ключ.", "activation", "act.edition");
                }
                if (key.Length > 0 && !KeyRe.IsMatch(key)) wn("Активация: ключ должен иметь вид XXXXX-XXXXX-XXXXX-XXXXX-XXXXX.", "activation", "act.key");
                if (AC.S("mode") == "key" && key.Length == 0) wn("Активация: не указан ключ продукта.", "activation", "act.key");
                if (AC.S("mode") == "kms")
                {
                    string host = AC.S("kmsHost").Trim();
                    int port = AC.I("kmsPort") != 0 ? AC.I("kmsPort") : 1688;
                    if (host.Length == 0) wn("Активация KMS: не указан адрес сервера.", "activation", "act.kmsHost");
                    al2.Add("$kms = " + Q(host) + "; $port = " + port);
                    al2.Add("# ждём доступности KMS-сервера (до 10 минут)");
                    al2.Add("for ($i = 0; $i -lt 60 -and -not (Test-Tcp $kms $port); $i++) { Start-Sleep -Seconds 10 }");
                    if (key.Length > 0) al2.Add("cscript.exe //nologo $slmgr /ipk " + key);
                    al2.Add("cscript.exe //nologo $slmgr /skms \"${kms}:${port}\"");
                    al2.Add("for ($i = 0; $i -lt 5; $i++) { $out = cscript.exe //nologo $slmgr /ato | Out-String; Write-Output $out; if ($out -match \"(?i)successfully|успешно\") { break }; Start-Sleep -Seconds 30 }");
                }
                else
                {
                    al2.Add("# ждём подключения к интернету (до 10 минут)");
                    al2.Add(PsWaitNet);
                    al2.Add("cscript.exe //nologo $slmgr /ipk " + key);
                    al2.Add("for ($i = 0; $i -lt 5; $i++) { $out = cscript.exe //nologo $slmgr /ato | Out-String; Write-Output $out; if ($out -match \"(?i)successfully|успешно\") { break }; Start-Sleep -Seconds 30 }");
                }
                al2.Add("Stop-Transcript | Out-Null");
                List<string> al3 = al2.FindAll(delegate(string s) { return s.Length > 0; });
                logon.Add(mkSpec("Активация Windows", null, psRun(addFile("Activation.ps1", string.Join("\n", al3.ToArray()))), null));
            }

            // ---------- Установка программ (winget / Chocolatey) ----------
            JObj AP = st.O("apps");
            bool useChoco = AP.S("mgr") == "choco";
            List<string> pkgIds = new List<string>();
            JObj pk = AP.O("pkgs");
            foreach (Pkg p in Cat.Packages) if (pk.B(p.Id)) pkgIds.Add(useChoco ? p.Choco : p.Winget);
            foreach (string id in DfList(AP.S("custom")))
            {
                if (!Cat.PkgIdRe.IsMatch(id)) wn("Программа «" + id + "»: недопустимый идентификатор пакета — пропущена.", "programs", "apps.custom");
                else pkgIds.Add(id);
            }
            List<string> pkgs = new List<string>();
            foreach (string id in pkgIds) if (!pkgs.Contains(id)) pkgs.Add(id);
            if (pkgs.Count > 0)
            {
                List<string> qp = new List<string>(); foreach (string s in pkgs) qp.Add(Q(s));
                List<string> pl = new List<string>(header("Packages"));
                pl.Add(PsTestTcp);
                pl.Add("# ждём подключения к интернету (до 10 минут)");
                pl.Add(PsWaitNet);
                pl.Add("$ids = @(" + string.Join(", ", qp.ToArray()) + ")");
                if (!useChoco)
                {
                    pl.Add("$w = $null");
                    pl.Add("for ($i = 0; $i -lt 30 -and -not $w; $i++) { $c = Get-Command winget.exe -ErrorAction SilentlyContinue; if ($c) { $w = $c.Source } else { Start-Sleep -Seconds 10 } }");
                    pl.Add("if (-not $w) { try { Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe -ErrorAction Stop; Start-Sleep -Seconds 5; $c = Get-Command winget.exe -ErrorAction SilentlyContinue; if ($c) { $w = $c.Source } } catch { Write-Warning $_ } }");
                    pl.Add("if (-not $w) { Write-Warning \"winget не найден (приложение «Установщик приложений» отсутствует), программы не установлены\"; Stop-Transcript | Out-Null; exit 1 }");
                    pl.Add("foreach ($id in $ids) {");
                    pl.Add("    Write-Output \"== $id\"");
                    pl.Add("    & $w install --id $id --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity");
                    pl.Add("    Write-Output \"код выхода: $LASTEXITCODE\"");
                    pl.Add("}");
                }
                else
                {
                    pl.Add("if (-not (Get-Command choco.exe -ErrorAction SilentlyContinue)) {");
                    pl.Add("    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072");
                    pl.Add("    Set-ExecutionPolicy Bypass -Scope Process -Force");
                    pl.Add("    Invoke-Expression ((New-Object Net.WebClient).DownloadString(\"https://community.chocolatey.org/install.ps1\"))");
                    pl.Add("    $env:Path += \";$env:ALLUSERSPROFILE\\chocolatey\\bin\"");
                    pl.Add("}");
                    pl.Add("foreach ($id in $ids) {");
                    pl.Add("    Write-Output \"== $id\"");
                    pl.Add("    choco install $id -y --no-progress");
                    pl.Add("    Write-Output \"код выхода: $LASTEXITCODE\"");
                    pl.Add("}");
                }
                pl.Add("Stop-Transcript | Out-Null");
                logon.Add(mkSpec("Установка программ (" + (useChoco ? "Chocolatey" : "winget") + ")", null, psRun(addFile("Packages.ps1", string.Join("\n", pl.ToArray()))), null));
                bool storeGone = twOn("storeoff") || bloatSel.Exists(delegate(BloatItem b) { return b.Id.Contains("WindowsStore"); });
                if (!useChoco && storeGone) wn("Microsoft Store запрещён или удалён — winget (входит в «Установщик приложений») может быть недоступен. Надёжнее выбрать Chocolatey.", "programs", "apps.mgr");
                if (!useChoco && os == "10") wn("В Windows 10 winget есть не во всех сборках: если его нет, скрипт попробует зарегистрировать «Установщик приложений», но без Магазина это может не получиться.", "programs", "apps.mgr");
            }

            // ---------- Планировщик ----------
            List<JObj> tasks = st.Objs("tasks");
            if (tasks.Count > 0)
            {
                List<string> tsl = new List<string>();
                tsl.Add("$ErrorActionPreference = 'Continue'");
                tsl.Add("Start-Transcript -Path \"$env:SystemRoot\\Setup\\Scripts\\Tasks.log\" -Force | Out-Null");
                tsl.Add("");
                int tcount = 0;
                for (int i = 0; i < tasks.Count; i++)
                {
                    JObj t = tasks[i];
                    string name = Regex.Replace(t.S("name").Trim(), "[\\\\/:*?\"<>|]", "_");
                    if (name.Length == 0 || t.S("program").Trim().Length == 0) { wn("Задача планировщика №" + (i + 1) + ": укажите имя и программу.", "tasks", "tasks:" + i); continue; }
                    string runAs = t.S("runAs");
                    if (runAs == "user" && t.S("user").Trim().Length == 0) { wn("Задача «" + name + "»: не указан пользователь.", "tasks", "tasks:" + i); continue; }
                    tcount++;
                    string runLevel = t.B("highest") ? "Highest" : "Limited";
                    tsl.Add("# " + name);
                    tsl.Add("$action = New-ScheduledTaskAction -Execute " + Q(t.S("program").Trim())
                        + (t.S("args").Trim().Length > 0 ? " -Argument " + Q(t.S("args").Trim()) : "")
                        + (t.S("workdir").Trim().Length > 0 ? " -WorkingDirectory " + Q(t.S("workdir").Trim()) : ""));
                    int n = Math.Max(1, t.I("n") != 0 ? t.I("n") : 1);
                    string tm = Regex.IsMatch(t.S("time"), "^[0-9]{1,2}:[0-9]{2}\\z") ? t.S("time") : "03:00";
                    switch (t.S("trigger"))
                    {
                        case "logon":
                            tsl.Add("$trigger = New-ScheduledTaskTrigger -AtLogOn" + (runAs == "user" ? " -User " + Q(t.S("user").Trim()) : runAs == "current" ? " -User $env:USERNAME" : ""));
                            break;
                        case "startup": tsl.Add("$trigger = New-ScheduledTaskTrigger -AtStartup"); break;
                        case "daily": tsl.Add("$trigger = New-ScheduledTaskTrigger -Daily -At " + Q(tm)); break;
                        case "weekly":
                            {
                                List<string> days = new List<string>();
                                List<object> da = t.A("days");
                                if (da != null) foreach (object o in da) days.Add((string)o);
                                if (days.Count == 0) days.Add("Monday");
                                tsl.Add("$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek " + string.Join(",", days.ToArray()) + " -At " + Q(tm));
                            }
                            break;
                        case "once": tsl.Add("$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(" + n + ")"); break;
                        case "interval": tsl.Add("$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes " + n + ") -RepetitionDuration (New-TimeSpan -Days 3650)"); break;
                    }
                    tsl.Add("$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)");
                    if (runAs == "system")
                    {
                        tsl.Add("$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel " + runLevel);
                        tsl.Add("Register-ScheduledTask -TaskName " + Q(name) + " -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null");
                    }
                    else if (runAs == "users")
                    {
                        tsl.Add("$principal = New-ScheduledTaskPrincipal -GroupId 'S-1-5-32-545' -RunLevel " + runLevel);
                        tsl.Add("Register-ScheduledTask -TaskName " + Q(name) + " -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null");
                    }
                    else if (runAs == "current")
                    {
                        tsl.Add("$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel " + runLevel);
                        tsl.Add("Register-ScheduledTask -TaskName " + Q(name) + " -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null");
                    }
                    else
                    {
                        tsl.Add("Register-ScheduledTask -TaskName " + Q(name) + " -Action $action -Trigger $trigger -Settings $settings -User " + Q(t.S("user").Trim()) + " -Password " + Q(t.S("pass")) + " -RunLevel " + runLevel + " -Force | Out-Null");
                    }
                    if (t.B("runNow")) tsl.Add("Start-ScheduledTask -TaskName " + Q(name));
                    tsl.Add("");
                }
                tsl.Add("Stop-Transcript | Out-Null");
                if (tcount > 0) logon.Add(mkSpec("Создание задач планировщика", null, psRun(addFile("ScheduledTasks.ps1", string.Join("\n", tsl.ToArray()))), null));
            }

            logon.AddRange(customLogon);
            if (Y.B("reboot")) logon.Add(mkSpec("Перезагрузка", null, "shutdown.exe /r /t 15 /c \"Setup finished, restarting\"", null));

            // ---------- Извлечение зашитых файлов (первая команда specialize) ----------
            if (files.Count > 0)
            {
                string extract = "powershell -NoProfile -Command \"$x=New-Object Xml.XmlDocument;$x.Load('C:\\Windows\\Panther\\unattend.xml');$x.SelectNodes('//*[@path]')|%{md (Split-Path $_.path) -Force|Out-Null;($_.InnerText.Trim()-split'\\n')|sc $_.path -Enc UTF8}\"";
                if (extract.Length > 259) wn1("Внутренняя команда извлечения скриптов длиннее 259 символов.");
                spec.Insert(0, mkSpec("Извлечение скриптов из файла ответов", extract, null, null));
            }

            // ---------- specialize ----------
            List<XN> settingsSpec = new List<XN>();
            string cn = Y.S("name").Trim();
            if (cn.Length > 0 && (cn.Length > 15 || !Regex.IsMatch(cn, "^[A-Za-z0-9-]+\\z") || Regex.IsMatch(cn, "^[0-9]+\\z")))
                wn("Имя компьютера: до 15 знаков, только латиница, цифры и дефис, не только цифры.", "system", "sys.name");
            settingsSpec.Add(comp("Microsoft-Windows-Shell-Setup", new object[] {
                X.S("ComputerName", cn.Length > 0 ? cn : "*"),
                Y.S("org").Trim().Length > 0 ? X.S("RegisteredOrganization", Y.S("org").Trim()) : null,
                Y.S("owner").Trim().Length > 0 ? X.S("RegisteredOwner", Y.S("owner").Trim()) : null,
                X.S("TimeZone", L.S("tz")),
                !string.IsNullOrEmpty(activationKey) ? X.E("ProductKey", null, activationKey) : null,
                isSys ? X.B("CopyProfile", SP.B("copyProfile")) : null }));
            if (isSys && SP.B("dnsOn"))
            {
                List<string> dnsIps = new List<string>();
                foreach (string x in new string[] { SP.S("dns1"), SP.S("dns2") }) if (x.Trim().Length > 0) dnsIps.Add(x.Trim());
                if (dnsIps.Count == 0) wn("DNS (Sysprep): включите хотя бы один адрес, иначе настройка не применится.", "install", "sysprep.dns1");
                if (dnsIps.Exists(delegate(string x) { return !IpRe.IsMatch(x); })) wn("DNS (Sysprep): некорректный адрес.", "install", "sysprep.dns1");
                if (SP.S("dnsIface").Trim().Length == 0) wn("DNS (Sysprep): укажите имя сетевого адаптера (обычно Ethernet).", "install", "sysprep.dnsIface");
                List<XN> ips = new List<XN>();
                for (int idx = 0; idx < dnsIps.Count; idx++) ips.Add(X.E("IpAddress", X.A("wcm:action", "add", "wcm:keyValue", (idx + 1).ToString(CultureInfo.InvariantCulture)), dnsIps[idx]));
                settingsSpec.Add(comp("Microsoft-Windows-DNS-Client", new object[] { X.E("Interfaces", null, X.E("Interface", X.Add,
                    X.E("DNSServerSearchOrder", null, ips),
                    X.S("Identifier", SP.S("dnsIface").Trim().Length > 0 ? SP.S("dnsIface").Trim() : "Ethernet"))) }));
            }
            if (Y.S("netMode") == "domain")
            {
                if (Y.S("domain").Trim().Length == 0 || Y.S("domUser").Trim().Length == 0) wn("Домен: укажите имя домена и учётную запись с правом ввода в домен.", "system", "sys.domain");
                settingsSpec.Add(comp("Microsoft-Windows-UnattendedJoin", new object[] { X.E("Identification", null,
                    X.E("Credentials", null, X.S("Domain", Y.S("domain").Trim()), X.S("Password", Y.S("domPass")), X.S("Username", Y.S("domUser").Trim())),
                    X.S("JoinDomain", Y.S("domain").Trim()), Y.S("domOU").Trim().Length > 0 ? X.S("MachineObjectOU", Y.S("domOU").Trim()) : null) }));
            }
            else
            {
                settingsSpec.Add(comp("Microsoft-Windows-UnattendedJoin", new object[] { X.E("Identification", null, X.S("JoinWorkgroup", Y.S("workgroup").Trim().Length > 0 ? Y.S("workgroup").Trim() : "WORKGROUP")) }));
            }
            if (spec.Count > 0)
            {
                List<XN> rc = new List<XN>();
                for (int i = 0; i < spec.Count; i++) rc.Add(X.E("RunSynchronousCommand", X.Add, X.S("Order", i + 1), X.S("Path", spec[i].Path), X.S("Description", spec[i].Desc)));
                settingsSpec.Add(comp("Microsoft-Windows-Deployment", new object[] { X.E("RunSynchronous", null, rc) }));
            }

            // ---------- oobeSystem ----------
            bool obf = st.B("obfuscate");
            List<object> oobeKids = new List<object>();
            JObj al = st.O("autologon");
            bool adminLogon = al.B("on") && al.I("user") == -1;
            int logonCount = al.B("always") ? 999999999 : Math.Max(1, al.I("count") != 0 ? al.I("count") : 1);
            if (adminLogon)
            {
                if (admin.S("pass").Length == 0) wn("Автовход под встроенным «Администратором» без пароля: Windows остановится на экране смены пароля. Задайте пароль администратора.", "users", "admin.pass");
                oobeKids.Add(X.E("AutoLogon", null, admin.S("pass").Length > 0 ? X.PwNode("Password", admin.S("pass"), "Password", obf) : null, X.B("Enabled", true),
                    X.S("LogonCount", logonCount), X.S("Username", AdminLoginName)));
            }
            else if (al.B("on"))
            {
                JObj pick = al.I("user") >= 0 && al.I("user") < allUsers.Count ? allUsers[al.I("user")] : null;
                JObj au = pick != null && pick.S("name").Trim().Length > 0 ? pick : (users.Count > 0 ? users[0] : null);
                if (au == null) wn("Автовход включён, но нет пользователей.", "users", "autologon.on");
                else
                {
                    if (au.S("pass").Length == 0)
                        wn("Автовход под «" + au.S("name").Trim() + "» без пароля: проверено на Windows 10 LTSC 2021 — вход не выполняется и система останавливается на экране смены пароля. Задайте пароль пользователю или отключите автовход.", "users", "users:" + allUsers.IndexOf(au));
                    oobeKids.Add(X.E("AutoLogon", null, au.S("pass").Length > 0 ? X.PwNode("Password", au.S("pass"), "Password", obf) : null, X.B("Enabled", true),
                        X.S("LogonCount", logonCount), X.S("Username", au.S("name").Trim())));
                }
            }
            if (logon.Count > 0)
            {
                List<XN> lc = new List<XN>();
                for (int i = 0; i < logon.Count; i++)
                    lc.Add(X.E("SynchronousCommand", X.Add, X.S("Order", i + 1), X.S("CommandLine", logon[i].CmdLine), X.S("Description", logon[i].Desc), X.B("RequiresUserInput", false)));
                oobeKids.Add(X.E("FirstLogonCommands", null, lc));
            }
            List<XN> oobe = new List<XN>();
            if (Y.B("hideEula")) oobe.Add(X.B("HideEULAPage", true));
            if (Y.B("hideOEM")) oobe.Add(X.B("HideOEMRegistrationScreen", true));
            if (Y.B("hideOnline")) oobe.Add(X.B("HideOnlineAccountScreens", true));
            if (Y.B("hideWireless")) oobe.Add(X.B("HideWirelessSetupInOOBE", true));
            if (users.Count > 0 || (admin.B("enable") && admin.S("pass").Length > 0)) oobe.Add(X.B("HideLocalAccountScreen", true));
            if (Y.S("netLoc").Length > 0) oobe.Add(X.S("NetworkLocation", Y.S("netLoc")));
            if (Y.B("express")) oobe.Add(X.S("ProtectYourPC", 3));
            oobeKids.Add(X.E("OOBE", null, oobe));
            List<object> ua = new List<object>();
            if (admin.S("pass").Length > 0) ua.Add(X.PwNode("AdministratorPassword", admin.S("pass"), "AdministratorPassword", obf));
            if (users.Count > 0)
            {
                List<XN> accts = new List<XN>();
                foreach (JObj x in users)
                    accts.Add(X.E("LocalAccount", X.Add,
                        x.S("pass").Length > 0 ? X.PwNode("Password", x.S("pass"), "Password", obf) : null,
                        X.S("Name", x.S("name").Trim()), x.S("display").Trim().Length > 0 ? X.S("DisplayName", x.S("display").Trim()) : null,
                        X.S("Group", x.S("group")), x.S("desc").Trim().Length > 0 ? X.S("Description", x.S("desc").Trim()) : null));
                ua.Add(X.E("LocalAccounts", null, accts));
            }
            if (ua.Count > 0) oobeKids.Add(X.E("UserAccounts", null, ua));
            if (admin.B("enable") && admin.S("pass").Length == 0) wn("Встроенный «Администратор» будет включён без пароля.", "users", "admin.pass");

            List<XN> settingsOobe = new List<XN>();
            settingsOobe.Add(comp("Microsoft-Windows-International-Core", new object[] { X.S("InputLocale", inputLocale), X.S("SystemLocale", L.S("system")), X.S("UILanguage", L.S("ui")), X.S("UserLocale", L.S("user")) }));
            settingsOobe.Add(comp("Microsoft-Windows-Shell-Setup", new object[] { oobeKids }));

            // ---------- generalize (только Sysprep) ----------
            List<XN> settingsGen = new List<XN>();
            if (isSys) settingsGen.Add(comp("Microsoft-Windows-Security-SPP", new object[] { X.S("SkipRearm", SP.B("skipRearm") ? 1 : 0) }));

            // ---------- Сборка ----------
            List<object> extKids = new List<object>();
            foreach (GenFile f in files) extKids.Add(X.E("ScriptFile", X.A("path", f.Path), X.CData(f.Content)));
            XN root = X.E("unattend", X.A("xmlns", "urn:schemas-microsoft-com:unattend"),
                !isSys ? X.E("settings", X.A("pass", "windowsPE"), settingsPE) : null,
                isSys ? X.E("settings", X.A("pass", "generalize"), settingsGen) : null,
                X.E("settings", X.A("pass", "specialize"), settingsSpec),
                X.E("settings", X.A("pass", "oobeSystem"), settingsOobe),
                files.Count > 0 ? X.E("Extensions", X.A("xmlns", "urn:autounattend-generator:files"), extKids) : null);
            string forWhat = (isSys ? "образа (Sysprep)" : "чистой установки") + ", Windows " + os;
            result.Xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- Создано генератором файлов ответов Windows 10/11 (версия " + Cat.Version + "): файл для " + forWhat + " -->\n" + X.Ser(root, 0) + "\n";

            if (isSys && files.Count == 0 && logon.Count == 0 && spec.Count == 0)
                wn1("Файл почти пуст: выберите хотя бы какие-то настройки — иначе результат ничем не отличается от обычного ответа Sysprep без файла.");
            result.ExtractLen = files.Count > 0 ? spec[0].Path.Length : 0;
            result.IsSys = isSys;
            result.Os = os;
            return result;
        }
    }
}
