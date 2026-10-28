// Главная форма: вкладка «Драйверы» - добавление драйверов из WinPE (или папки) в установленную Windows офлайн и починка «драйвер есть, а устройства нет».
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinInstaller
{
    /// <summary>INF-пакет драйвера, найденный в хранилище WinPE.</summary>
    class DrvPack
    {
        public string Inf;          // полный путь к .inf
        public string Class = "";   // Net, SCSIAdapter, HDC
        public string Provider = "";
        public bool Match;          // в INF есть ID устройства, которое сейчас стоит в этом ПК
        public string MatchId = "";
    }

    class DrvPlan
    {
        public SysInfo Sys;
        public List<DrvPack> Packs = new List<DrvPack>();
        public string Folder;       // папка с драйверами или null
        public bool ForceUnsigned, FastBootOff, DryRun, Builtin;
    }

    partial class MainForm
    {
        static readonly string[] DriversSteps = { "Система", "Драйверы", "Запуск" };

        RadioButton rbModeDrivers;
        TabControl tabDrivers;
        ListView lvDrvSys, lvDrvFound;
        Label lblDrvSysHint, lblDrvFoundHint, lblDrvSummary;
        TextBox txtDrvFolder;
        CheckBox chkDrvFolder, chkDrvUnsigned, chkDrvFastBoot, chkDrvBuiltin;
        Button btnDrvSysRefresh, btnDrvScan, btnDrvAll, btnDrvFolder, btnDrvList, btnDrvLog;
        bool drvScanning;
        ListView lvDrvNoDrv;
        Button btnDrvNoScan, btnDrvOnline;
        Label lblDrvOnlineHint;

        // ------------------------------------------------------------ интерфейс

        void BuildDriversUi()
        {
            TabPage pgSys = AddPage(tabDrivers, "Система");
            TabPage pgDrv = AddPage(tabDrivers, "Драйверы");
            TabPage pgRun = AddPage(tabDrivers, "Запуск");

            // 1. система
            Head(pgSys, "В какую Windows добавить драйверы", "Драйверы ставятся офлайн, Windows при этом не запускается.");
            UiCard c1 = NewCard(pgSys, "Найденные системы", 252);
            lvDrvSys = Add(c1, new UiList(), 18, 52, 600, 136);
            lvDrvSys.Columns.Add("Раздел", 70);
            lvDrvSys.Columns.Add("Windows", 130);
            lvDrvSys.Columns.Add("Бит", 50);
            lvDrvSys.Columns.Add("Размер", 90);
            lvDrvSys.Columns.Add("Метка", 140);
            lvDrvSys.Columns.Add("Примечание", 150);
            Fluid.Stretch(lvDrvSys, 18);
            btnDrvSysRefresh = Btn(c1, "Обновить", "refresh", 18, 200, 122, 34, -1);
            lblDrvSysHint = Hint(c1, "", 154, 206, 460, 24, 18);
            FinishPage(pgSys);

            // 2. драйверы
            Head(pgDrv, "Какие драйверы добавить", "Из хранилища WinPE (то, что работает сейчас в этом ПК) и/или из папки. Подходящие по устройствам выделены сами.");
            UiCard c2 = NewCard(pgDrv, "Драйверы, найденные в WinPE (сеть и дисковые контроллеры)", 372);
            lvDrvFound = Add(c2, new UiList(), 18, 52, 600, 220);
            lvDrvFound.MultiSelect = true;
            lvDrvFound.Columns.Add("Драйвер (INF)", 190);
            lvDrvFound.Columns.Add("Класс", 90);
            lvDrvFound.Columns.Add("Поставщик", 170);
            lvDrvFound.Columns.Add("Устройство в этом ПК", 240);
            Fluid.Stretch(lvDrvFound, 18);
            btnDrvScan = Btn(c2, "Найти в WinPE", "search", 18, 284, 150, 34, -1);
            btnDrvAll = Btn(c2, "Выделить все", "check", 178, 284, 150, 34, -1);
            lblDrvFoundHint = Hint(c2, "Выделенные строки будут добавлены в систему. Выделяются драйверы, в INF которых есть устройство из этого ПК.", 18, 326, 600, 40, 18);
            Gap(pgDrv, 14);
            UiCard c2b = NewCard(pgDrv, "Устройства без драйвера: поиск в интернете (каталог Microsoft Update)", 346);
            lvDrvNoDrv = Add(c2b, new UiList(), 18, 52, 600, 150);
            lvDrvNoDrv.Columns.Add("Устройство", 330);
            lvDrvNoDrv.Columns.Add("ID оборудования", 300);
            Fluid.Stretch(lvDrvNoDrv, 18);
            btnDrvNoScan = Btn(c2b, "Найти устройства без драйвера", "search", 18, 214, 268, 34, -1);
            btnDrvOnline = Btn(c2b, "Найти и скачать драйвер", "net", 296, 214, 230, 34, -1);
            lblDrvOnlineHint = Hint(c2b, "Нужен интернет в WinPE и верное системное время. Скачанный драйвер появится в списке выше и будет добавлен в выбранную Windows (а не в WinPE).", 18, 260, 600, 70, 18);
            Gap(pgDrv, 14);
            UiCard c3 = NewCard(pgDrv, "Дополнительно", 300);
            chkDrvBuiltin = Add(c3, new UiCheck { Text = "Добавить встроенные сетевые драйверы: Intel I217-I219, I210/I211, Realtek 8168/8125" }, 18, 52, 760, 24);
            chkDrvFolder = Add(c3, new UiCheck { Text = "Добавить драйверы из папки (с вложенными)" }, 18, 88, 520, 24);
            txtDrvFolder = new TextBox();
            Input(c3, txtDrvFolder, 18, 116, 500, 18 + 112 + 8);
            btnDrvFolder = Btn(c3, "Обзор…", "folder", 0, 116, 112, 32, 18);
            chkDrvUnsigned = Add(c3, new UiCheck { Text = "Разрешить неподписанные драйверы (/ForceUnsigned)" }, 18, 162, 640, 24);
            chkDrvFastBoot = Add(c3, new UiCheck { Text = "Отключить быстрый запуск и гибернацию (иначе устройства могут не инициализироваться)", Checked = true }, 18, 196, 700, 24);
            btnDrvList = Btn(c3, "Что уже есть в системе", "log", 18, 242, 214, 34, -1);
            btnDrvLog = Btn(c3, "Журнал установки устройств", "log", 244, 242, 250, 34, -1);
            FinishPage(pgDrv);

            // 3. запуск
            Head(pgRun, "Параметры и запуск", "Проверьте, что будет сделано. Перед стартом программа попросит подтверждение.");
            UiCard c4 = NewCard(pgRun, "Что будет сделано", 360);
            lblDrvSummary = Add(c4, new Label(), 18, 52, 600, 292);
            lblDrvSummary.Font = Theme.Mono; lblDrvSummary.ForeColor = Theme.Text;
            Fluid.Stretch(lblDrvSummary, 18);
            FinishPage(pgRun);

            btnDrvSysRefresh.Click += delegate { RefreshDrvSystems(); };
            lvDrvSys.SelectedIndexChanged += delegate { UpdateNav(); };
            lvDrvFound.SelectedIndexChanged += delegate { UpdateNav(); };
            btnDrvScan.Click += delegate { ScanDrvPacks(); };
            btnDrvAll.Click += delegate { foreach (ListViewItem it in lvDrvFound.Items) it.Selected = true; };
            chkDrvFolder.CheckedChanged += delegate { UpdateNav(); };
            chkDrvBuiltin.CheckedChanged += delegate { UpdateNav(); };
            txtDrvFolder.TextChanged += delegate { UpdateNav(); };
            chkDrvUnsigned.CheckedChanged += delegate { UpdateNav(); };
            chkDrvFastBoot.CheckedChanged += delegate { UpdateNav(); };
            btnDrvFolder.Click += delegate
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                    if (fb.ShowDialog(this) == DialogResult.OK) { txtDrvFolder.Text = fb.SelectedPath; chkDrvFolder.Checked = true; }
            };
            btnDrvNoScan.Click += delegate { ScanNoDriver(); };
            btnDrvOnline.Click += delegate { OnlineDriver(); };
            btnDrvList.Click += delegate { DrvShowInstalled(); };
            btnDrvLog.Click += delegate { DrvShowSetupLog(); };
        }

        // ------------------------------------------------------------ системы

        void RefreshDrvSystems()
        {
            if (lvDrvSys == null) return;
            SysInfo prev = SelectedDrvSys();
            lvDrvSys.Items.Clear();
            char sysL = char.ToUpper((Environment.GetEnvironmentVariable("SystemDrive") ?? "X:")[0]);
            int count = 0;
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                char c = char.ToUpper(d.Name[0]);
                if (c == 'A' || c == 'B' || (c == 'X' && sysL == 'X')) continue;
                try
                {
                    string krn = c + ":\\Windows\\System32\\ntoskrnl.exe";
                    if (!d.IsReady || !File.Exists(krn)) continue;
                    FileVersionInfo fv = FileVersionInfo.GetVersionInfo(krn);
                    if (fv.FileBuildPart < 6000) continue;
                    SysInfo s = new SysInfo();
                    s.Letter = c; s.Size = d.TotalSize; s.Label = d.VolumeLabel;
                    s.Build = fv.FileBuildPart;
                    s.Name = WinName(s.Build);
                    s.X64 = Directory.Exists(c + ":\\Windows\\SysWOW64");
                    s.Current = c == sysL && sysL != 'X';
                    ListViewItem it = new ListViewItem(new string[] { c + ":", s.Name, s.X64 ? "x64" : "x86", Util.FmtSize(s.Size), s.Label, s.Current ? "текущая система" : "" });
                    it.Tag = s;
                    lvDrvSys.Items.Add(it);
                    count++;
                }
                catch { }
            }
            lblDrvSysHint.Text = count == 0 ? "Установленных Windows не найдено." : "";
            foreach (ListViewItem it in lvDrvSys.Items)
                if (count == 1 || (prev != null && ((SysInfo)it.Tag).Letter == prev.Letter)) { it.Selected = true; break; }
            UpdateNav();
        }

        SysInfo SelectedDrvSys()
        {
            if (lvDrvSys == null || lvDrvSys.SelectedItems.Count == 0) return null;
            return (SysInfo)lvDrvSys.SelectedItems[0].Tag;
        }

        // ------------------------------------------------------------ поиск драйверов в WinPE

        static string ReadInf(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length > 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length > 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            return Encoding.GetEncoding(1252).GetString(b);
        }

        /// <summary>Идентификаторы PCI/USB-устройств этого ПК, обрезанные до VEN_xxxx&amp;DEV_xxxx / VID_xxxx&amp;PID_xxxx.</summary>
        static Dictionary<string, string> PresentHwIds()
        {
            Dictionary<string, string> res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string bus in new string[] { "PCI", "USB" })
            {
                try
                {
                    using (RegistryKey busKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + bus))
                    {
                        if (busKey == null) continue;
                        foreach (string dev in busKey.GetSubKeyNames())
                        {
                            using (RegistryKey dk = busKey.OpenSubKey(dev))
                            {
                                if (dk == null) continue;
                                foreach (string inst in dk.GetSubKeyNames())
                                {
                                    using (RegistryKey ik = dk.OpenSubKey(inst))
                                    {
                                        if (ik == null) continue;
                                        string desc = Convert.ToString(ik.GetValue("DeviceDesc"));
                                        int sc = desc.LastIndexOf(';'); if (sc >= 0) desc = desc.Substring(sc + 1);
                                        foreach (string valName in new string[] { "HardwareID", "CompatibleIDs" })
                                        {
                                            string[] ids = ik.GetValue(valName) as string[];
                                            if (ids == null) continue;
                                            foreach (string id in ids)
                                            {
                                                Match m = Regex.Match(id, @"^(PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}|USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4})", RegexOptions.IgnoreCase);
                                                if (m.Success && !res.ContainsKey(m.Groups[1].Value)) res[m.Groups[1].Value] = desc.Trim();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            return res;
        }

        static string InfValue(string text, string key)
        {
            Match m = Regex.Match(text, @"^\s*" + Regex.Escape(key) + @"\s*=\s*(.+?)\s*(;.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            string v = m.Groups[1].Value.Trim().Trim('"');
            Match tok = Regex.Match(v, @"^%(.+)%$");
            if (tok.Success)
            {
                Match s = Regex.Match(text, @"^\s*" + Regex.Escape(tok.Groups[1].Value) + @"\s*=\s*""?(.*?)""?\s*(;.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (s.Success) v = s.Groups[1].Value.Trim();
            }
            return v;
        }

        void ScanDrvPacks()
        {
            if (drvScanning) return;
            drvScanning = true;
            btnDrvScan.Enabled = false;
            lvDrvFound.Items.Clear();
            lblDrvFoundHint.Text = "Поиск...";
            Thread t = new Thread(delegate()
            {
                List<DrvPack> found = new List<DrvPack>();
                try
                {
                    Dictionary<string, string> present = PresentHwIds();
                    string repo = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"X:\Windows", @"System32\DriverStore\FileRepository");
                    if (Directory.Exists(repo))
                    {
                        foreach (string dir in Directory.GetDirectories(repo))
                        {
                            foreach (string inf in Directory.GetFiles(dir, "*.inf"))
                            {
                                try
                                {
                                    string text = ReadInf(inf);
                                    string cls = InfValue(text, "Class");
                                    if (!(cls.Equals("Net", StringComparison.OrdinalIgnoreCase) || cls.Equals("SCSIAdapter", StringComparison.OrdinalIgnoreCase) || cls.Equals("HDC", StringComparison.OrdinalIgnoreCase))) continue;
                                    DrvPack p = new DrvPack();
                                    p.Inf = inf; p.Class = cls; p.Provider = InfValue(text, "Provider");
                                    foreach (KeyValuePair<string, string> kv in present)
                                        if (text.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) { p.Match = true; p.MatchId = kv.Value.Length > 0 ? kv.Value : kv.Key; break; }
                                    found.Add(p);
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch (Exception ex) { Log("Поиск драйверов: " + ex.Message); }
                found.Sort(delegate(DrvPack a, DrvPack b)
                {
                    if (a.Match != b.Match) return a.Match ? -1 : 1;
                    return string.Compare(Path.GetFileName(a.Inf), Path.GetFileName(b.Inf), StringComparison.OrdinalIgnoreCase);
                });
                UI(delegate
                {
                    drvScanning = false;
                    btnDrvScan.Enabled = true;
                    lvDrvFound.BeginUpdate();
                    int matches = 0;
                    foreach (DrvPack p in found)
                    {
                        ListViewItem it = new ListViewItem(new string[] { Path.GetFileName(p.Inf), p.Class, p.Provider, p.Match ? p.MatchId : "" });
                        it.Tag = p;
                        if (!p.Match) it.ForeColor = Theme.Muted;
                        lvDrvFound.Items.Add(it);
                        if (p.Match) { it.Selected = true; matches++; }
                    }
                    lvDrvFound.EndUpdate();
                    lblDrvFoundHint.Text = found.Count == 0
                        ? "В хранилище WinPE драйверов сети и дисков не найдено. Укажите папку с драйверами ниже."
                        : "Найдено: " + found.Count + ", подходят устройствам этого ПК: " + matches + " (выделены). Выделенные строки будут добавлены в систему.";
                    UpdateNav();
                });
            });
            t.IsBackground = true; t.Start();
        }

        /// <summary>Ищет в каталоге драйвер для ID и распаковывает первый подходящий (с этим ID и x64) в outDir. Возвращает путь к .inf или null.</summary>
        internal static string FetchDriver(string id, int build, bool x64, string outDir, Action<string> log)
        {
            List<CatalogHit> hits = DriverCatalog.Search(id, build);
            log("  " + id + ": записей в каталоге " + hits.Count);
            int tried = 0;
            foreach (CatalogHit h in hits)
            {
                if (tried++ >= 8) break;
                foreach (string url in DriverCatalog.DownloadUrls(h.Guid))
                {
                    string dir = Path.Combine(outDir, "t" + tried + "_" + (Math.Abs(url.GetHashCode()) % 100000));
                    Directory.CreateDirectory(dir);
                    string cab = Path.Combine(dir, "pkg.cab");
                    DriverCatalog.Download(url, cab);
                    string x = Path.Combine(dir, "x");
                    if (!DriverCatalog.Expand(cab, x)) continue;
                    foreach (string inf in Directory.GetFiles(x, "*.inf", SearchOption.AllDirectories))
                    {
                        string text = ReadInf(inf);
                        if (text.IndexOf(id, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (x64 && text.IndexOf("NTamd64", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        log("  " + id + ": " + h + " -> " + Path.GetFileName(inf));
                        return inf;
                    }
                }
            }
            return null;
        }

        // ------------------------------------------------------------ устройства без драйвера и онлайн-поиск

        class NoDrvDev { public string Desc, Id; }

        /// <summary>Устройства PCI/USB, у которых в реестре нет ни службы, ни класса: драйвер не привязан.</summary>
        void ScanNoDriver()
        {
            lvDrvNoDrv.Items.Clear();
            List<string> seen = new List<string>();
            foreach (string bus in new string[] { "PCI", "USB" })
            {
                try
                {
                    using (RegistryKey busKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + bus))
                    {
                        if (busKey == null) continue;
                        foreach (string dev in busKey.GetSubKeyNames())
                            using (RegistryKey dk = busKey.OpenSubKey(dev))
                            {
                                if (dk == null) continue;
                                foreach (string inst in dk.GetSubKeyNames())
                                    using (RegistryKey ik = dk.OpenSubKey(inst))
                                    {
                                        if (ik == null || ik.GetValue("Service") != null || ik.GetValue("ClassGUID") != null) continue;
                                        string[] ids = ik.GetValue("HardwareID") as string[];
                                        string sid = ids == null || ids.Length == 0 ? null : DriverCatalog.ShortId(ids[0]);
                                        if (sid == null || seen.Contains(sid)) continue;
                                        seen.Add(sid);
                                        string desc = Convert.ToString(ik.GetValue("DeviceDesc"));
                                        int sc = desc.LastIndexOf(';'); if (sc >= 0) desc = desc.Substring(sc + 1);
                                        NoDrvDev d = new NoDrvDev(); d.Desc = desc.Trim().Length > 0 ? desc.Trim() : "Неизвестное устройство"; d.Id = sid;
                                        ListViewItem it = new ListViewItem(new string[] { d.Desc, d.Id });
                                        it.Tag = d;
                                        lvDrvNoDrv.Items.Add(it);
                                    }
                            }
                    }
                }
                catch { }
            }
            lblDrvOnlineHint.Text = lvDrvNoDrv.Items.Count == 0
                ? "Устройств без драйвера не найдено. Если нужное не видно, драйвер мог быть привязан только в WinPE."
                : "Найдено устройств без драйвера: " + lvDrvNoDrv.Items.Count + ". Выберите нужное и нажмите «Найти и скачать драйвер». Нужен интернет в WinPE.";
        }

        void OnlineDriver()
        {
            if (installing || busy) return;
            if (lvDrvNoDrv.SelectedItems.Count == 0) { MessageBox.Show(this, "Выберите устройство в списке (сначала нажмите «Найти устройства без драйвера»).", "Драйверы"); return; }
            NoDrvDev dev = (NoDrvDev)lvDrvNoDrv.SelectedItems[0].Tag;
            SysInfo sys = SelectedDrvSys();
            int build = sys == null ? 19045 : sys.Build;
            bool x64 = sys == null || sys.X64;
            ShowLog(true);
            btnDrvOnline.Enabled = false;
            Thread t = new Thread(delegate()
            {
                List<DrvPack> got = new List<DrvPack>();
                try
                {
                    SetStatus("Каталог Microsoft Update: поиск драйвера для " + dev.Id + "...");
                    List<CatalogHit> hits = DriverCatalog.Search(dev.Id, build);
                    Log("Найдено записей: " + hits.Count);
                    string root = Path.Combine(Path.GetTempPath(), "Buba-Helper", "drivers", Regex.Replace(dev.Id, @"[^A-Za-z0-9_&]", "_"));
                    int tried = 0;
                    foreach (CatalogHit h in hits)
                    {
                        if (tried++ >= 6 || got.Count > 0) break;
                        SetStatus("Скачивание: " + h);
                        List<string> urls = DriverCatalog.DownloadUrls(h.Guid);
                        foreach (string url in urls)
                        {
                            string dir = Path.Combine(root, tried + "_" + (Math.Abs(url.GetHashCode()) % 100000));
                            Directory.CreateDirectory(dir);
                            string cab = Path.Combine(dir, "pkg.cab");
                            DriverCatalog.Download(url, cab);
                            if (!DriverCatalog.Expand(cab, Path.Combine(dir, "x"))) { Log("  не удалось распаковать " + url); continue; }
                            foreach (string inf in Directory.GetFiles(Path.Combine(dir, "x"), "*.inf", SearchOption.AllDirectories))
                            {
                                string text = ReadInf(inf);
                                if (text.IndexOf(dev.Id, StringComparison.OrdinalIgnoreCase) < 0) continue;
                                if (x64 && text.IndexOf("NTamd64", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                DrvPack p = new DrvPack();
                                p.Inf = inf; p.Class = InfValue(text, "Class"); p.Provider = InfValue(text, "Provider");
                                p.Match = true; p.MatchId = dev.Desc + "  (из интернета)";
                                got.Add(p);
                            }
                            if (got.Count > 0) break;
                        }
                    }
                }
                catch (Exception ex) { Log("ОШИБКА онлайн-поиска: " + (ex.InnerException ?? ex).Message + "  (проверьте интернет и системное время в WinPE)"); }
                UI(delegate
                {
                    btnDrvOnline.Enabled = true;
                    foreach (DrvPack p in got)
                    {
                        ListViewItem it = new ListViewItem(new string[] { Path.GetFileName(p.Inf), p.Class, p.Provider, p.MatchId });
                        it.Tag = p;
                        lvDrvFound.Items.Insert(0, it);
                        it.Selected = true;
                    }
                    string msg = got.Count > 0 ? "Драйвер скачан и выбран: " + Path.GetFileName(got[0].Inf) + ". Он будет добавлен в выбранную Windows на шаге «Запуск»."
                        : "Подходящий драйвер в каталоге не найден (см. журнал).";
                    lblDrvOnlineHint.Text = msg; lblStatus.Text = msg; Log(msg);
                    UpdateNav();
                });
            });
            t.IsBackground = true; t.Start();
        }

        // ------------------------------------------------------------ проверка и сводка

        List<DrvPack> SelectedPacks()
        {
            List<DrvPack> l = new List<DrvPack>();
            if (lvDrvFound == null) return l;
            foreach (ListViewItem it in lvDrvFound.SelectedItems) l.Add((DrvPack)it.Tag);
            return l;
        }

        bool DrvFolderWanted() { return chkDrvFolder != null && chkDrvFolder.Checked && txtDrvFolder.Text.Trim().Length > 0; }

        string DriversProblem(int step)
        {
            if (step == 0 && SelectedDrvSys() == null) return "Выберите установленную Windows.";
            if (step == 1)
            {
                if (SelectedPacks().Count == 0 && !DrvFolderWanted() && !chkDrvFastBoot.Checked && !chkDrvBuiltin.Checked)
                    return "Выберите драйверы из списка, укажите папку или включите отключение быстрого запуска.";
                if (DrvFolderWanted() && !Directory.Exists(txtDrvFolder.Text.Trim())) return "Папка с драйверами не найдена.";
            }
            return null;
        }

        string BuildDriversSummary()
        {
            StringBuilder sb = new StringBuilder();
            SysInfo s = SelectedDrvSys();
            sb.AppendLine(KV("Система", s == null ? "-" : s.Letter + ":  " + s.Name + (s.X64 ? " x64" : " x86") + "  (сборка " + s.Build + ")"));
            sb.AppendLine();
            List<DrvPack> packs = SelectedPacks();
            sb.AppendLine("Драйверы из WinPE: " + packs.Count);
            foreach (DrvPack p in packs) sb.AppendLine("    " + Path.GetFileName(p.Inf) + "  [" + p.Class + "]  " + p.Provider);
            sb.AppendLine(KV("Встроенные", chkDrvBuiltin.Checked ? "сетевые Intel и Realtek" : "нет"));
            sb.AppendLine(KV("Папка", DrvFolderWanted() ? txtDrvFolder.Text.Trim() : "нет"));
            sb.AppendLine(KV("Неподписанные", chkDrvUnsigned.Checked ? "разрешены" : "нет"));
            sb.AppendLine(KV("Быстрый запуск", chkDrvFastBoot.Checked ? "отключить, гибернацию удалить" : "не трогать"));
            sb.AppendLine();
            sb.Append("Windows не запускается: драйверы кладутся в её хранилище через dism /Add-Driver. Данные не удаляются.");
            return sb.ToString();
        }

        // ------------------------------------------------------------ диагностика

        void DrvShowInstalled()
        {
            SysInfo s = SelectedDrvSys();
            if (s == null) { MessageBox.Show(this, "Сначала выберите Windows на первом шаге.", "Драйверы"); return; }
            ShowLog(true);
            string dism = Util.FindTool("dism.exe") ?? "dism.exe";
            Thread t = new Thread(delegate()
            {
                UI(delegate { SetBusy(true); });
                List<string> cap = new List<string>();
                RunProc(dism, "/Image:" + s.Letter + ":\\ /Get-Drivers", null, cap, true);
                UI(delegate { SetBusy(false); });
            });
            t.IsBackground = true; t.Start();
        }

        void DrvShowSetupLog()
        {
            SysInfo s = SelectedDrvSys();
            if (s == null) { MessageBox.Show(this, "Сначала выберите Windows на первом шаге.", "Драйверы"); return; }
            ShowLog(true);
            string path = s.Letter + ":\\Windows\\INF\\setupapi.dev.log";
            Thread t = new Thread(delegate()
            {
                try
                {
                    if (!File.Exists(path)) { Log("Журнал " + path + " не найден (Windows ещё не запускалась или он очищен)."); return; }
                    List<string> keep = new List<string>();
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                        if (Regex.IsMatch(line, @"e1d|rt640|netrtl|netwtw|VEN_8086|VEN_10EC|Failed|error|0xe0000|ERROR", RegexOptions.IgnoreCase)) keep.Add(line.Trim());
                    int from = Math.Max(0, keep.Count - 60);
                    Log("--- setupapi.dev.log: последние " + (keep.Count - from) + " подходящих строк из " + keep.Count + " ---");
                    for (int i = from; i < keep.Count; i++) Log("  " + keep[i]);
                }
                catch (Exception ex) { Log("Не удалось прочитать журнал: " + ex.Message); }
            });
            t.IsBackground = true; t.Start();
        }

        // ------------------------------------------------------------ запуск

        void StartDrivers()
        {
            if (installing || busy) return;
            string pr = DriversProblem(0) ?? DriversProblem(1);
            if (pr != null) { MessageBox.Show(this, pr, "Драйверы"); return; }
            DrvPlan p = new DrvPlan();
            p.Sys = SelectedDrvSys();
            p.Packs = SelectedPacks();
            p.Folder = DrvFolderWanted() ? txtDrvFolder.Text.Trim() : null;
            p.ForceUnsigned = chkDrvUnsigned.Checked;
            p.FastBootOff = chkDrvFastBoot.Checked;
            p.Builtin = chkDrvBuiltin.Checked;
            p.DryRun = Program.DryRun;
            string msg = BuildDriversSummary() + (p.DryRun ? "\n\n[DRY-RUN - реально ничего не будет сделано]" : "") + "\n\nНачать?";
            if (MessageBox.Show(this, msg, "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) != DialogResult.Yes) return;
            SetWorking(true);
            Thread t = new Thread(delegate() { RunDrivers(p); });
            t.IsBackground = true; t.Start();
        }

        void RunDrivers(DrvPlan p)
        {
            try
            {
                UI(delegate { lblStatus.ForeColor = Theme.Text; SetProgress(0); });
                string dism = Util.FindTool("dism.exe");
                if (!p.DryRun && dism == null) { Fail("Не найден dism.exe"); return; }
                dism = dism ?? "dism.exe";
                string root = p.Sys.Letter + ":\\";
                int total = p.Packs.Count + (p.Builtin ? 1 : 0) + (p.Folder != null ? 1 : 0) + (p.FastBootOff ? 1 : 0), done = 0, errors = 0;
                string uns = p.ForceUnsigned ? " /ForceUnsigned" : "";

                foreach (DrvPack pk in p.Packs)
                {
                    SetStatus("Добавление драйвера " + Path.GetFileName(pk.Inf) + "...");
                    int rc = RunProc(dism, "/Image:" + root + " /Add-Driver /Driver:\"" + pk.Inf + "\"" + uns, null, null, false);
                    if (rc != 0) { errors++; Log("ПРЕДУПРЕЖДЕНИЕ: " + Path.GetFileName(pk.Inf) + " не добавлен (код " + rc + ")."); }
                    SetProgress(++done * 100 / Math.Max(1, total));
                }
                if (p.Builtin)
                {
                    SetStatus("Добавление встроенных сетевых драйверов (Intel, Realtek)...");
                    string bdir = ExtractBuiltinDrivers();
                    if (bdir == null) { errors++; Log("ПРЕДУПРЕЖДЕНИЕ: встроенные драйверы не найдены в программе."); }
                    else
                    {
                        int rc = RunProc(dism, "/Image:" + root + " /Add-Driver /Driver:\"" + bdir + "\" /Recurse" + uns, null, null, false);
                        if (rc != 0) { errors++; Log("ПРЕДУПРЕЖДЕНИЕ: встроенные драйверы добавлены не полностью (код " + rc + ")."); }
                    }
                    SetProgress(++done * 100 / Math.Max(1, total));
                }
                if (p.Folder != null)
                {
                    SetStatus("Добавление драйверов из папки " + p.Folder + "...");
                    int rc = RunProc(dism, "/Image:" + root + " /Add-Driver /Driver:\"" + p.Folder + "\" /Recurse" + uns, null, null, false);
                    if (rc != 0) { errors++; Log("ПРЕДУПРЕЖДЕНИЕ: драйверы из папки добавлены не полностью (код " + rc + ")."); }
                    SetProgress(++done * 100 / Math.Max(1, total));
                }
                if (p.FastBootOff)
                {
                    SetStatus("Отключение быстрого запуска и гибернации...");
                    DisableFastBoot(p);
                    SetProgress(++done * 100 / Math.Max(1, total));
                }
                if (!p.DryRun && (p.Packs.Count > 0 || p.Folder != null))
                {
                    Log("--- проверка: драйверы третьих сторон в системе ---");
                    RunProc(dism, "/Image:" + root + " /Get-Drivers", null, null, false);
                }
                SetProgress(100);
                Beep(errors == 0);
                string res = errors == 0 ? "ГОТОВО. Драйверы добавлены в " + p.Sys.Letter + ":. Перезагрузите ПК в Windows." : "Завершено с предупреждениями (" + errors + "), см. журнал.";
                Log(res);
                UI(delegate
                {
                    lblStatus.Text = res;
                    lblStatus.ForeColor = errors == 0 ? Theme.Text : Theme.Warn;
                    FinishUi();
                });
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        /// <summary>Распаковывает вшитый в exe набор сетевых драйверов (Intel, Realtek) во временную папку; null - набора нет.</summary>
        static string ExtractBuiltinDrivers()
        {
            using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("WinInstaller.netdrivers.zip"))
            {
                if (s == null) return null;
                string dir = Path.Combine(Path.GetTempPath(), "Buba-Helper", "netdrv");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                using (System.IO.Compression.ZipArchive z = new System.IO.Compression.ZipArchive(s))
                    System.IO.Compression.ZipFileExtensions.ExtractToDirectory(z, dir);
                return dir;
            }
        }

        /// <summary>Офлайн: HiberbootEnabled=0 в SYSTEM-кусте и удаление hiberfil.sys (иначе Windows поднимется из гибернации и не переинициализирует устройства).</summary>
        void DisableFastBoot(DrvPlan p)
        {
            string root = p.Sys.Letter + ":\\";
            string reg = Util.FindTool("reg.exe") ?? "reg.exe";
            string hive = root + "Windows\\System32\\config\\SYSTEM";
            const string key = "HKLM\\BUBA_OFFSYS";
            bool loaded = false;
            try
            {
                if (RunProc(reg, "load " + key + " \"" + hive + "\"", null, null, false) != 0) { Log("ПРЕДУПРЕЖДЕНИЕ: не удалось загрузить куст SYSTEM - быстрый запуск не отключён."); return; }
                loaded = true;
                RunProc(reg, "add \"" + key + "\\ControlSet001\\Control\\Session Manager\\Power\" /v HiberbootEnabled /t REG_DWORD /d 0 /f", null, null, false);
            }
            finally
            {
                if (loaded)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers();
                    if (RunProc(reg, "unload " + key, null, null, false) != 0) { Thread.Sleep(1000); RunProc(reg, "unload " + key, null, null, false); }
                }
            }
            string hib = root + "hiberfil.sys";
            try
            {
                if (!p.DryRun && File.Exists(hib))
                {
                    File.SetAttributes(hib, FileAttributes.Normal);
                    File.Delete(hib);
                    Log("hiberfil.sys удалён.");
                }
            }
            catch (Exception ex) { Log("ПРЕДУПРЕЖДЕНИЕ: hiberfil.sys не удалён: " + ex.Message); }
        }
    }
}
