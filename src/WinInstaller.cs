// WinInstaller - установка Windows из ISO/WIM/ESD из-под WinPE (Sergei Strelec и др.)
// Сборка: build.cmd (нужен только csc.exe из .NET Framework 4, C# 5).
// Ключ --dry-run: ничего не выполняет, только пишет команды в лог.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace WinInstaller
{
    static class Program
    {
        public static bool DryRun;

        [STAThread]
        static void Main(string[] args)
        {
            string profile = null;
            bool auto = false, yes = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--dry-run" || a == "/dry") DryRun = true;
                else if (a == "--profile" && i + 1 < args.Length) profile = args[++i];
                else if (a == "--auto") auto = true;
                else if (a == "--yes") yes = true;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new MainForm(profile, auto, yes)); }
            catch (Exception ex) { MessageBox.Show(ex.ToString(), "WinInstaller - критическая ошибка"); }
        }
    }

    // ---------------------------------------------------------------- модели

    class DiskInfo
    {
        public int Number;
        public long Size;
        public string Model = "";
        public string Bus = "";
        public string Style = "?";
        public bool Removable;
        public List<string> Volumes = new List<string>();
        public List<char> Letters = new List<char>();
        public bool Blocked;   // на этом диске лежит источник
    }

    class ImageInfo
    {
        public int Index;
        public string Name = "";
        public long Bytes;
        public override string ToString()
        {
            string s = Index + ". " + Name;
            if (Bytes > 0) s += "  (" + Util.FmtSize(Bytes) + ")";
            return s;
        }
    }

    class Plan
    {
        public string ImageFile;
        public int ImageIndex;
        public DiskInfo Disk;
        public bool Gpt;
        public long WinSizeMB;      // 0 = всё свободное место
        public bool DataPart;
        public string DriversDir;
        public bool Reboot;
        public bool DryRun;
        public bool Keep;           // форматировать только раздел Windows, остальное не трогать
        public char WinLetter;      // (Keep) буква раздела Windows
        public int WinPart;         // (Keep) номер раздела Windows на диске
        public int EspPart;         // (Keep, GPT) номер EFI-раздела
        public string Xml;          // unattend.xml, копируется в Windows\Panther\Unattend
        public string SourcePath;   // выбранный файл (ISO/WIM) - для проверки .sha256
        public bool VerifyHash;
    }

    static class Util
    {
        public static string FmtSize(long b)
        {
            double gb = b / 1073741824.0;
            if (gb >= 1024) return (gb / 1024).ToString("0.##") + " ТБ";
            if (gb >= 1) return gb.ToString("0.#") + " ГБ";
            return (b / 1048576.0).ToString("0") + " МБ";
        }

        public static string Sha256File(string file, Action<int> pct)
        {
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] buf = new byte[4 << 20];
                long done = 0, total = Math.Max(1, fs.Length);
                int last = -1, n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    sha.TransformBlock(buf, 0, n, null, 0);
                    done += n;
                    int p = (int)(done * 100 / total);
                    if (p != last && pct != null) { last = p; pct(p); }
                }
                sha.TransformFinalBlock(buf, 0, 0);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in sha.Hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string AppDir()
        {
            return Path.GetDirectoryName(Application.ExecutablePath);
        }

        public static string FindTool(string exe)
        {
            string[] dirs = new string[] {
                Path.Combine(AppDir(), "tools"), AppDir(),
                Environment.GetEnvironmentVariable("SystemRoot") + "\\System32",
                @"X:\Windows\System32", @"X:\Program Files\7-Zip", @"X:\Program Files (x86)\7-Zip",
                Environment.GetEnvironmentVariable("ProgramFiles") + "\\7-Zip"
            };
            foreach (string d in dirs)
            {
                try { string p = Path.Combine(d, exe); if (File.Exists(p)) return p; } catch { }
            }
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string d in path.Split(';'))
            {
                try { if (d.Length > 0) { string p = Path.Combine(d.Trim(), exe); if (File.Exists(p)) return p; } } catch { }
            }
            return null;
        }
    }

    // ---------------------------------------------------------------- Win32: диски, тома, ISO

    static class Native
    {
        const uint GENERIC_READ = 0x80000000;
        const uint SHARE_RW = 3;
        const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int ret, IntPtr ov);

        [DllImport("kernel32.dll")]
        static extern bool GetFirmwareType(out int type);

        static SafeFileHandle Open(string path, uint access)
        {
            return CreateFile(path, access, SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }

        static string AsciiAt(byte[] b, int offset)
        {
            if (offset <= 0 || offset >= b.Length) return "";
            int e = offset;
            while (e < b.Length && b[e] != 0) e++;
            return Encoding.ASCII.GetString(b, offset, e - offset).Trim();
        }

        /// <summary>Режим, в котором загружен ПК: "UEFI", "BIOS" или "?".</summary>
        public static string FirmwareMode()
        {
            try
            {
                int t;
                if (GetFirmwareType(out t)) { if (t == 1) return "BIOS"; if (t == 2) return "UEFI"; }
            }
            catch { }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control"))
                {
                    object v = k == null ? null : k.GetValue("PEFirmwareType");
                    if (v is int) { if ((int)v == 1) return "BIOS"; if ((int)v == 2) return "UEFI"; }
                }
            }
            catch { }
            return "?";
        }

        /// <summary>Номера физических дисков, на которых лежит том (по букве диска).</summary>
        public static List<int> DisksOfVolume(char letter)
        {
            List<int> res = new List<int>();
            using (SafeFileHandle h = Open(@"\\.\" + letter + ":", 0))
            {
                if (h.IsInvalid) return res;
                byte[] buf = new byte[1024];
                int ret;
                if (!DeviceIoControl(h, 0x560000, null, 0, buf, buf.Length, out ret, IntPtr.Zero)) return res;
                int n = BitConverter.ToInt32(buf, 0);
                for (int i = 0; i < n && 8 + i * 24 + 4 <= buf.Length; i++)
                    res.Add(BitConverter.ToInt32(buf, 8 + i * 24));
            }
            return res;
        }

        public static List<int> DisksOfPath(string path)
        {
            if (!string.IsNullOrEmpty(path) && path.Length >= 2 && path[1] == ':')
                return DisksOfVolume(char.ToUpper(path[0]));
            return new List<int>();
        }

        static Dictionary<int, List<char>> letterMap = new Dictionary<int, List<char>>();

        /// <summary>Номер раздела (1..N) для тома по букве; -1 - не удалось.</summary>
        public static int PartitionNumberOfVolume(char letter)
        {
            using (SafeFileHandle h = Open(@"\\.\" + letter + ":", 0))
            {
                if (h.IsInvalid) return -1;
                byte[] buf = new byte[12];
                int ret;
                if (!DeviceIoControl(h, 0x002D1080, null, 0, buf, buf.Length, out ret, IntPtr.Zero)) return -1;
                return BitConverter.ToInt32(buf, 8);
            }
        }

        /// <summary>Номер EFI System раздела на GPT-диске; -1 - нет/не удалось.</summary>
        public static int EspPartitionNumber(int disk)
        {
            using (SafeFileHandle h = Open(@"\\.\PhysicalDrive" + disk, GENERIC_READ))
            {
                if (h.IsInvalid) return -1;
                byte[] lay = new byte[65536];
                int ret;
                if (!DeviceIoControl(h, 0x00070050, null, 0, lay, lay.Length, out ret, IntPtr.Zero)) return -1;
                if (BitConverter.ToInt32(lay, 0) != 1) return -1;           // не GPT
                int cnt = BitConverter.ToInt32(lay, 4);
                Guid esp = new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
                for (int i = 0; i < cnt; i++)
                {
                    int o = 48 + i * 144;
                    if (o + 144 > ret) break;
                    if (BitConverter.ToInt32(lay, o) != 1) continue;
                    byte[] g = new byte[16];
                    Array.Copy(lay, o + 32, g, 0, 16);
                    if (new Guid(g) == esp) return BitConverter.ToInt32(lay, o + 24);
                }
            }
            return -1;
        }

        static Dictionary<int, List<string>> VolumeMap()
        {
            letterMap = new Dictionary<int, List<char>>();
            Dictionary<int, List<string>> map = new Dictionary<int, List<string>>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                char c = d.Name[0];
                if (c == 'A' || c == 'B') continue;
                string text = c + ":";
                try
                {
                    if (d.IsReady)
                    {
                        string lbl = d.VolumeLabel;
                        text += (lbl.Length > 0 ? " " + lbl : "") + " (" + d.DriveFormat + ", " + Util.FmtSize(d.TotalSize) + ")";
                    }
                    else text += " (нет данных)";
                }
                catch { }
                foreach (int n in DisksOfVolume(c))
                {
                    if (!map.ContainsKey(n)) map[n] = new List<string>();
                    map[n].Add(text);
                    if (!letterMap.ContainsKey(n)) letterMap[n] = new List<char>();
                    letterMap[n].Add(c);
                }
            }
            return map;
        }

        public static List<DiskInfo> EnumDisks()
        {
            List<DiskInfo> res = new List<DiskInfo>();
            Dictionary<int, List<string>> vols = VolumeMap();
            for (int i = 0; i < 32; i++)
            {
                string p = @"\\.\PhysicalDrive" + i;
                bool full = true;
                SafeFileHandle h = Open(p, GENERIC_READ);
                if (h.IsInvalid) { h.Dispose(); h = Open(p, 0); full = false; }
                using (h)
                {
                    if (h.IsInvalid) continue;
                    DiskInfo di = new DiskInfo();
                    di.Number = i;
                    int ret;

                    byte[] geo = new byte[256];
                    if (DeviceIoControl(h, 0x000700A0, null, 0, geo, geo.Length, out ret, IntPtr.Zero))
                        di.Size = BitConverter.ToInt64(geo, 24);
                    if (di.Size <= 0) continue;     // пустой кард-ридер и т.п.

                    byte[] q = new byte[12];        // StorageDeviceProperty / PropertyStandardQuery
                    byte[] desc = new byte[1024];
                    if (DeviceIoControl(h, 0x002D1400, q, q.Length, desc, desc.Length, out ret, IntPtr.Zero))
                    {
                        string vendor = AsciiAt(desc, BitConverter.ToInt32(desc, 12));
                        string product = AsciiAt(desc, BitConverter.ToInt32(desc, 16));
                        di.Model = (vendor + " " + product).Trim();
                        di.Removable = desc[10] != 0;
                        switch (BitConverter.ToInt32(desc, 28))
                        {
                            case 7: di.Bus = "USB"; break;
                            case 17: di.Bus = "NVMe"; break;
                            case 11: di.Bus = "SATA"; break;
                            case 3: di.Bus = "ATA"; break;
                            case 8: di.Bus = "RAID"; break;
                            case 10: di.Bus = "SAS"; break;
                            case 15: di.Bus = "Virtual"; break;
                            case 14: di.Bus = "VHD"; break;
                            default: di.Bus = "?"; break;
                        }
                    }

                    if (full)
                    {
                        byte[] lay = new byte[32768];
                        if (DeviceIoControl(h, 0x00070050, null, 0, lay, lay.Length, out ret, IntPtr.Zero))
                        {
                            int st = BitConverter.ToInt32(lay, 0);
                            di.Style = st == 0 ? "MBR" : st == 1 ? "GPT" : "RAW";
                        }
                        else di.Style = "RAW";
                    }

                    if (vols.ContainsKey(i)) di.Volumes = vols[i];
                    if (letterMap.ContainsKey(i)) di.Letters = letterMap[i];
                    res.Add(di);
                }
            }
            return res;
        }
    }

    /// <summary>Монтирование ISO через virtdisk.dll (без PowerShell).</summary>
    class IsoMount
    {
        [StructLayout(LayoutKind.Sequential)]
        struct VStorageType { public uint DeviceId; public Guid VendorId; }

        [DllImport("virtdisk.dll", CharSet = CharSet.Unicode)]
        static extern int OpenVirtualDisk(ref VStorageType t, string path, int access, int flags, IntPtr prm, out IntPtr handle);
        [DllImport("virtdisk.dll")]
        static extern int AttachVirtualDisk(IntPtr h, IntPtr sd, int flags, int provFlags, IntPtr prm, IntPtr ov);
        [DllImport("virtdisk.dll")]
        static extern int DetachVirtualDisk(IntPtr h, int flags, int provFlags);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        IntPtr handle = IntPtr.Zero;
        public string Path;
        public string Letter;     // например "E:"

        static HashSet<char> Letters()
        {
            HashSet<char> s = new HashSet<char>();
            foreach (DriveInfo d in DriveInfo.GetDrives()) s.Add(char.ToUpper(d.Name[0]));
            return s;
        }

        public bool Mount(string iso, out string error)
        {
            error = null;
            Dismount();
            HashSet<char> before = Letters();
            VStorageType t = new VStorageType();
            t.DeviceId = 1; // ISO
            t.VendorId = new Guid("EC984AEC-A0F9-47e9-901F-71415A66345B");
            IntPtr h;
            int r;
            try { r = OpenVirtualDisk(ref t, iso, 0xD0000, 0, IntPtr.Zero, out h); }
            catch (Exception ex) { error = "virtdisk.dll недоступна: " + ex.Message; return false; }
            if (r != 0) { error = new Win32Exception(r).Message; return false; }
            r = AttachVirtualDisk(h, IntPtr.Zero, 1 /*READ_ONLY*/, 0, IntPtr.Zero, IntPtr.Zero);
            if (r != 0) { CloseHandle(h); error = new Win32Exception(r).Message; return false; }
            handle = h;
            Path = iso;

            for (int i = 0; i < 60; i++)
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    char c = char.ToUpper(d.Name[0]);
                    if (before.Contains(c)) continue;
                    try { if (d.IsReady) { Letter = c + ":"; return true; } } catch { }
                }
                Thread.Sleep(250);
            }
            error = "ISO подключён, но буква диска не появилась";
            Dismount();
            return false;
        }

        public void Dismount()
        {
            if (handle != IntPtr.Zero)
            {
                try { DetachVirtualDisk(handle, 0, 0); } catch { }
                CloseHandle(handle);
                handle = IntPtr.Zero;
            }
            Letter = null;
            Path = null;
        }
    }

    // ---------------------------------------------------------------- чтение списка редакций из WIM/ESD

    static class WimReader
    {
        /// <summary>Читает XML-метаданные прямо из заголовка WIM. null - если не получилось (например, XML сжат).</summary>
        public static List<ImageInfo> FromHeader(string file)
        {
            try
            {
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] h = new byte[208];
                    if (fs.Read(h, 0, h.Length) < 208) return null;
                    if (Encoding.ASCII.GetString(h, 0, 5) != "MSWIM") return null;
                    long size = 0;
                    for (int i = 6; i >= 0; i--) size = (size << 8) | h[72 + i];
                    byte flags = h[79];
                    long offset = BitConverter.ToInt64(h, 80);
                    if ((flags & 0x04) != 0) return null;                 // сжат
                    if (size < 16 || size > 64 * 1024 * 1024 || offset <= 0 || offset + size > fs.Length) return null;
                    fs.Seek(offset, SeekOrigin.Begin);
                    byte[] x = new byte[size];
                    int got = 0;
                    while (got < x.Length) { int n = fs.Read(x, got, x.Length - got); if (n <= 0) return null; got += n; }
                    string xml = Encoding.Unicode.GetString(x).TrimStart('\uFEFF');
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(xml);
                    List<ImageInfo> res = new List<ImageInfo>();
                    foreach (XmlNode n in doc.SelectNodes("/WIM/IMAGE"))
                    {
                        ImageInfo ii = new ImageInfo();
                        ii.Index = int.Parse(n.Attributes["INDEX"].Value);
                        XmlNode nm = n.SelectSingleNode("NAME");
                        if (nm == null) nm = n.SelectSingleNode("DISPLAYNAME");
                        ii.Name = nm == null ? "Image " + ii.Index : nm.InnerText;
                        XmlNode tb = n.SelectSingleNode("TOTALBYTES");
                        long b; if (tb != null && long.TryParse(tb.InnerText, out b)) ii.Bytes = b;
                        res.Add(ii);
                    }
                    return res.Count > 0 ? res : null;
                }
            }
            catch { return null; }
        }

        /// <summary>Запасной вариант: разбор вывода dism /Get-WimInfo (не зависит от языка).</summary>
        public static List<ImageInfo> FromDism(string file, List<string> raw)
        {
            List<ImageInfo> res = new List<ImageInfo>();
            string dism = Util.FindTool("dism.exe") ?? "dism.exe";
            ProcessStartInfo psi = new ProcessStartInfo(dism, "/Get-WimInfo /WimFile:\"" + file + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            string text;
            using (Process p = Process.Start(psi)) { text = p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
            ImageInfo cur = null;
            bool prevBlank = true;
            foreach (string line in text.Split('\n'))
            {
                string l = line.TrimEnd('\r');
                raw.Add(l);
                if (l.Trim().Length == 0) { prevBlank = true; continue; }
                Match m = Regex.Match(l, @"^\s*[^:]+:\s*(.*)$");
                if (!m.Success) { prevBlank = false; continue; }
                string v = m.Groups[1].Value.Trim();
                if (prevBlank && Regex.IsMatch(v, @"^\d+$"))
                {
                    cur = new ImageInfo(); cur.Index = int.Parse(v); res.Add(cur);
                }
                else if (cur != null && cur.Name.Length == 0) cur.Name = v;
                prevBlank = false;
            }
            return res;
        }
    }

    // ---------------------------------------------------------------- главная форма

    class MainForm : Form
    {
        // источник
        ComboBox cmbSource, cmbEdition;
        Button btnBrowse, btnScan;
        Label lblSrcStatus;
        // диск
        ListView lvDisks;
        Button btnRefresh;
        Label lblDiskHint;
        // разметка
        RadioButton rbGpt, rbMbr;
        Label lblFw, lblPlan;
        NumericUpDown nudSize;
        CheckBox chkAll;
        CheckBox chkData;
        // дополнительно
        CheckBox chkDrivers, chkReboot;
        TextBox txtDrivers;
        Button btnDrivers;
        // режим
        RadioButton rbModeInstall, rbModeCapture;
        Panel panelInstall, panelCapture;
        // захват
        ListView lvVols;
        Button btnVolRefresh, btnOut, btnXml, btnXmlClear;
        TextBox txtOut, txtName, txtDesc, txtXml;
        ComboBox cmbCompress;
        Label lblVolHint;
        // режим диска / профили / XML / проверка
        GroupBox grpDisk;
        RadioButton rbWipe, rbKeep;
        Label lblSize, lblSizeHint, lblPart;
        ComboBox cmbPart;
        TextBox txtInstXml;
        Button btnInstXml, btnSaveProf, btnLoadProf;
        CheckBox chkVerify, chkHash;
        // выполнение
        Button btnInstall;
        ProgressBar progress;
        Label lblStatus;
        TextBox txtLog;

        string firmware = "?";
        string imageFile;                        // реальный путь к wim/esd, из которого ставим
        List<ImageInfo> images = new List<ImageInfo>();
        List<int> sourceDisks = new List<int>(); // диски, на которых лежит источник
        IsoMount iso = new IsoMount();
        string extractedTmp;                     // временный wim, извлечённый через 7z
        bool busy;
        bool installing;
        string logFile;
        string sourcePath;                       // выбранный пользователем файл (ISO/WIM)
        List<char> sourceLetters = new List<char>();
        List<string> lastFound = new List<string>();
        int partsDisk = -1;
        // профиль / авторежим
        string pendingSource, pendingEdition, pendingDisk;
        bool autoStart, autoYes;

        const int EfiMB = 300, MsrMB = 16;

        public MainForm() : this(null, false, false) { }

        public MainForm(string profilePath, bool auto, bool yes)
        {
            BuildUi();
            logFile = Path.Combine(Util.AppDir(), "install.log");
            firmware = Native.FirmwareMode();
            lblFw.Text = "ПК загружен в режиме: " + firmware;
            if (firmware == "BIOS") rbMbr.Checked = true; else rbGpt.Checked = true;
            Log("WinInstaller запущен" + (Program.DryRun ? " (DRY-RUN: команды не выполняются)" : "") + ". Режим прошивки: " + firmware);
            if (profilePath == null)
            {
                string def = Path.Combine(Util.AppDir(), "profile.ini");
                if (File.Exists(def)) profilePath = def;
            }
            else profilePath = ResolvePath(profilePath);
            if (profilePath != null)
            {
                autoStart = auto; autoYes = yes;
                ApplyProfile(profilePath);
            }
            Load += delegate { RefreshDisks(); ScanSources(); UpdatePlanText(); };
        }

        // ------------------------------------------------------------ UI

        T Add<T>(Control parent, T c, int x, int y, int w, int h) where T : Control
        {
            c.Left = x; c.Top = y; c.Width = w; c.Height = h;
            parent.Controls.Add(c);
            return c;
        }

        void BuildUi()
        {
            Text = "Установка Windows из ISO / WIM" + (Program.DryRun ? "  [DRY-RUN]" : "");
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(900, 850);
            MinimumSize = new Size(916, 820);
            StartPosition = FormStartPosition.CenterScreen;

            // переключатель режима
            rbModeInstall = Add(this, new RadioButton { Text = "Установка Windows", Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter, Checked = true }, 12, 6, 220, 30);
            rbModeCapture = Add(this, new RadioButton { Text = "Захват образа (sysprep)", Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter }, 238, 6, 220, 30);
            panelInstall = Add(this, new Panel(), 0, 40, 900, 592);
            panelInstall.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panelCapture = Add(this, new Panel(), 0, 40, 900, 592);
            panelCapture.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panelCapture.Visible = false;

            // 1. образ
            GroupBox g1 = Add(panelInstall, new GroupBox(), 12, 8, 876, 116);
            g1.Text = "1. Образ (ISO / WIM / ESD)"; g1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Add(g1, new Label { Text = "Файл:" }, 12, 28, 120, 22);
            cmbSource = Add(g1, new ComboBox(), 140, 24, 600, 26);
            cmbSource.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnBrowse = Add(g1, new Button { Text = "Обзор..." }, 748, 22, 56, 28);
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right; btnBrowse.Width = 58;
            btnScan = Add(g1, new Button { Text = "Поиск" }, 810, 22, 56, 28);
            btnScan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Add(g1, new Label { Text = "Редакция:" }, 12, 62, 120, 22);
            cmbEdition = Add(g1, new ComboBox(), 140, 58, 726, 26);
            cmbEdition.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbEdition.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblSrcStatus = Add(g1, new Label(), 140, 88, 726, 20);
            lblSrcStatus.ForeColor = Color.DimGray;

            // 2. диск
            grpDisk = Add(panelInstall, new GroupBox(), 12, 130, 876, 154);
            GroupBox g2 = grpDisk;
            g2.Text = "2. Диск для установки (ВСЕ ДАННЫЕ НА НЁМ БУДУТ УДАЛЕНЫ)";
            g2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lvDisks = Add(g2, new ListView(), 12, 24, 852, 90);
            lvDisks.View = View.Details; lvDisks.FullRowSelect = true; lvDisks.MultiSelect = false; lvDisks.HideSelection = false;
            lvDisks.GridLines = true;
            lvDisks.Columns.Add("№", 36);
            lvDisks.Columns.Add("Модель", 230);
            lvDisks.Columns.Add("Размер", 80);
            lvDisks.Columns.Add("Шина", 60);
            lvDisks.Columns.Add("Стиль", 50);
            lvDisks.Columns.Add("Разделы (тома)", 380);
            lvDisks.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnRefresh = Add(g2, new Button { Text = "Обновить" }, 12, 118, 100, 28);
            lblDiskHint = Add(g2, new Label(), 124, 123, 740, 22);
            lblDiskHint.ForeColor = Color.DimGray;
            lblDiskHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // 3. разметка
            GroupBox g3 = Add(panelInstall, new GroupBox(), 12, 288, 876, 182);
            g3.Text = "3. Разметка и размер"; g3.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Add(g3, new Label { Text = "Режим:" }, 12, 28, 125, 22);
            // каждая пара радиокнопок в своём контейнере, иначе они образуют одну группу
            Panel pnlMode = Add(g3, new Panel(), 140, 24, 720, 28);
            rbWipe = Add(pnlMode, new RadioButton { Text = "Стереть весь диск и разметить заново" }, 0, 2, 340, 24);
            rbKeep = Add(pnlMode, new RadioButton { Text = "Только раздел Windows (остальное не трогать)" }, 340, 2, 380, 24);
            rbWipe.Checked = true;
            Add(g3, new Label { Text = "Таблица разделов:" }, 12, 58, 125, 22);
            Panel pnlScheme = Add(g3, new Panel(), 140, 54, 320, 28);
            rbGpt = Add(pnlScheme, new RadioButton { Text = "GPT (UEFI)" }, 0, 2, 120, 24);
            rbMbr = Add(pnlScheme, new RadioButton { Text = "MBR (BIOS / Legacy)" }, 130, 2, 180, 24);
            lblFw = Add(g3, new Label(), 470, 58, 390, 22);
            lblFw.ForeColor = Color.DimGray;
            lblSize = Add(g3, new Label { Text = "Раздел Windows:" }, 12, 92, 125, 22);
            nudSize = Add(g3, new NumericUpDown { Minimum = 20480, Maximum = 4194304, Increment = 1024, Value = 51200 }, 140, 88, 100, 26);
            lblSizeHint = Add(g3, new Label { Text = "МБ   (1 ГБ = 1024 МБ,  51200 МБ = 50 ГБ)" }, 246, 92, 300, 22);
            chkAll = Add(g3, new CheckBox { Text = "весь диск" }, 560, 90, 120, 24);
            lblPart = Add(g3, new Label { Text = "В раздел:" }, 12, 92, 125, 22);
            cmbPart = Add(g3, new ComboBox(), 140, 88, 520, 26);
            cmbPart.DropDownStyle = ComboBoxStyle.DropDownList;
            lblPart.Visible = false; cmbPart.Visible = false;
            nudSize.ValueChanged += delegate { UpdatePlanText(); };
            chkAll.CheckedChanged += delegate { nudSize.Enabled = !chkAll.Checked; UpdatePlanText(); };
            chkData = Add(g3, new CheckBox { Text = "Оставшееся место отдать под отдельный раздел «Data» (NTFS)" }, 14, 118, 600, 24);
            lblPlan = Add(g3, new Label(), 14, 146, 850, 34);
            lblPlan.ForeColor = Color.FromArgb(0, 70, 140);
            lblPlan.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // 4. дополнительно
            GroupBox g4 = Add(panelInstall, new GroupBox(), 12, 474, 876, 116);
            g4.Text = "4. Дополнительно"; g4.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chkDrivers = Add(g4, new CheckBox { Text = "Добавить драйверы из папки:" }, 14, 22, 220, 24);
            txtDrivers = Add(g4, new TextBox(), 240, 22, 540, 26);
            txtDrivers.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnDrivers = Add(g4, new Button { Text = "Обзор..." }, 788, 20, 78, 28);
            btnDrivers.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            string drv = Path.Combine(Util.AppDir(), "Drivers");
            if (Directory.Exists(drv)) { txtDrivers.Text = drv; chkDrivers.Checked = true; }
            Add(g4, new Label { Text = "XML ответов (пусто = без XML):" }, 14, 58, 224, 22);
            txtInstXml = Add(g4, new TextBox(), 240, 54, 540, 26);
            txtInstXml.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnInstXml = Add(g4, new Button { Text = "Обзор..." }, 788, 52, 78, 28);
            btnInstXml.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            chkReboot = Add(g4, new CheckBox { Text = "Перезагрузить ПК после установки" }, 14, 86, 280, 24);
            chkReboot.Checked = !Program.DryRun;
            chkVerify = Add(g4, new CheckBox { Text = "Проверять SHA-256 (.sha256)", Checked = true }, 296, 86, 250, 24);
            btnSaveProf = Add(g4, new Button { Text = "Сохранить профиль" }, 560, 82, 150, 28);
            btnLoadProf = Add(g4, new Button { Text = "Загрузить профиль" }, 716, 82, 150, 28);
            btnSaveProf.Anchor = AnchorStyles.Top | AnchorStyles.Right; btnLoadProf.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            BuildCaptureUi();
            StyleModeButtons();

            // запуск
            btnInstall = Add(this, new Button { Text = "УСТАНОВИТЬ WINDOWS" }, 12, 636, 876, 44);
            btnInstall.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
            btnInstall.BackColor = Color.FromArgb(0, 120, 215); btnInstall.ForeColor = Color.White;
            btnInstall.FlatStyle = FlatStyle.Flat;
            btnInstall.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progress = Add(this, new ProgressBar(), 12, 688, 876, 20);
            progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblStatus = Add(this, new Label { Text = "Готово к работе" }, 12, 712, 876, 22);
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtLog = Add(this, new TextBox(), 12, 738, 876, 104);
            txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 8.5f);
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // события
            btnBrowse.Click += delegate { BrowseSource(); };
            btnScan.Click += delegate { ScanSources(); };
            btnRefresh.Click += delegate { RefreshDisks(); };
            btnDrivers.Click += delegate
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                    if (fb.ShowDialog(this) == DialogResult.OK) { txtDrivers.Text = fb.SelectedPath; chkDrivers.Checked = true; }
            };
            cmbSource.SelectionChangeCommitted += delegate { BeginInvoke((Action)delegate { LoadSourceAsync(cmbSource.Text); }); };
            cmbSource.Click += delegate { if (cmbSource.Items.Count > 0 && !cmbSource.DroppedDown) cmbSource.DroppedDown = true; };
            cmbSource.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadSourceAsync(cmbSource.Text); }
            };
            rbGpt.CheckedChanged += delegate { UpdatePlanText(); };
            rbMbr.CheckedChanged += delegate { UpdatePlanText(); };
            chkData.CheckedChanged += delegate { UpdatePlanText(); };
            lvDisks.SelectedIndexChanged += delegate { UpdatePlanText(); };
            btnInstall.Click += delegate { if (rbModeCapture.Checked) StartCapture(); else StartInstall(false); };
            rbKeep.CheckedChanged += delegate { ApplyModeLayout(); };
            cmbPart.SelectedIndexChanged += delegate { UpdatePlanText(); };
            btnInstXml.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "XML (*.xml)|*.xml|Все файлы|*.*";
                    if (ofd.ShowDialog(this) == DialogResult.OK) txtInstXml.Text = ofd.FileName;
                }
            };
            btnSaveProf.Click += delegate
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "Профиль (*.ini)|*.ini"; sfd.DefaultExt = "ini"; sfd.FileName = "profile.ini"; sfd.InitialDirectory = Util.AppDir();
                    if (sfd.ShowDialog(this) != DialogResult.OK) return;
                    try { SaveProfile(sfd.FileName); Log("Профиль сохранён: " + sfd.FileName); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Профиль"); }
                }
            };
            btnLoadProf.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Профиль (*.ini)|*.ini|Все файлы|*.*"; ofd.InitialDirectory = Util.AppDir();
                    if (ofd.ShowDialog(this) != DialogResult.OK) return;
                    autoStart = false;
                    ApplyProfile(ofd.FileName);
                    ResolvePendingSource(lastFound);
                }
            };
            rbModeInstall.CheckedChanged += delegate { SwitchMode(); };
            rbModeCapture.CheckedChanged += delegate { SwitchMode(); };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (installing) { e.Cancel = true; MessageBox.Show("Идёт операция - закрывать нельзя."); return; }
                iso.Dismount();
                CleanupExtracted();
            };
        }

        void UI(Action a)
        {
            if (IsDisposed) return;
            try { if (InvokeRequired) BeginInvoke(a); else a(); } catch { }
        }

        void Log(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
            try { File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8); } catch { }
            UI(delegate
            {
                txtLog.AppendText(line + Environment.NewLine);
            });
        }

        void SetStatus(string s) { UI(delegate { lblStatus.Text = s; }); Log(s); }
        void SetProgress(int pct) { UI(delegate { progress.Value = Math.Max(0, Math.Min(100, pct)); }); }

        void SetBusy(bool b)
        {
            busy = b;
            cmbSource.Enabled = !b; btnBrowse.Enabled = !b; btnScan.Enabled = !b;
            btnInstall.Enabled = !b && !installing;
            UseWaitCursor = b;
        }

        // ------------------------------------------------------------ диски

        void RefreshDisks()
        {
            int sel = -1;
            if (lvDisks.SelectedItems.Count > 0) sel = ((DiskInfo)lvDisks.SelectedItems[0].Tag).Number;
            lvDisks.Items.Clear();
            List<DiskInfo> disks;
            try { disks = Native.EnumDisks(); }
            catch (Exception ex) { Log("Ошибка чтения дисков: " + ex.Message); return; }
            foreach (DiskInfo d in disks)
            {
                d.Blocked = !rbKeep.Checked && sourceDisks.Contains(d.Number);
                string vols = d.Volumes.Count > 0 ? string.Join(";  ", d.Volumes.ToArray()) : "(нет томов с буквами)";
                if (d.Blocked) vols = "<< ИСТОЧНИК ОБРАЗА >>  " + vols;
                else if (rbKeep.Checked && sourceDisks.Contains(d.Number)) vols = "(на диске лежит образ - его раздел защищён)  " + vols;
                ListViewItem it = new ListViewItem(new string[] { d.Number.ToString(), d.Model, Util.FmtSize(d.Size), d.Bus, d.Style, vols });
                it.Tag = d;
                if (d.Blocked) it.ForeColor = Color.Gray;
                else if (d.Bus == "USB") it.ForeColor = Color.DarkOrange;
                lvDisks.Items.Add(it);
                if (d.Number == sel && !d.Blocked) it.Selected = true;
            }
            lblDiskHint.Text = disks.Count == 0
                ? "Диски не найдены. Нужны права администратора (в WinPE они есть)."
                : "Оранжевые - USB-диски. Серый - диск с образом (его выбрать нельзя).";
            UpdatePlanText();
        }

        DiskInfo SelectedDisk()
        {
            if (lvDisks.SelectedItems.Count == 0) return null;
            return (DiskInfo)lvDisks.SelectedItems[0].Tag;
        }

        // ------------------------------------------------------------ источник

        void BrowseSource()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Образы Windows (*.iso;*.wim;*.esd)|*.iso;*.wim;*.esd|Все файлы|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                cmbSource.Text = ofd.FileName;
                LoadSourceAsync(ofd.FileName);
            }
        }

        void ScanSources()
        {
            btnScan.Enabled = false;
            lblSrcStatus.Text = "Поиск образов на дисках...";
            Thread t = new Thread(delegate()
            {
                List<string> found = new List<string>();
                string[] skip = new string[] { "windows", "program files", "program files (x86)", "$recycle.bin", "system volume information", "programdata", "users", "recovery", "$windows.~bt", "sources" };
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                    if (d.Name.StartsWith("X", StringComparison.OrdinalIgnoreCase) && d.DriveType == DriveType.Fixed && !d.IsReady) continue;
                    try { if (!d.IsReady) continue; } catch { continue; }
                    ScanDir(d.RootDirectory.FullName, 0, found, skip);
                }
                UI(delegate
                {
                    string prev = cmbSource.Text;
                    cmbSource.Items.Clear();
                    foreach (string f in found) cmbSource.Items.Add(f);
                    cmbSource.Text = prev;
                    btnScan.Enabled = true;
                    lastFound = found;
                    if (pendingSource != null) ResolvePendingSource(found);
                    else if (found.Count == 0)
                        lblSrcStatus.Text = "Образы не найдены - укажите файл кнопкой «Обзор...»";
                    else if (found.Count == 1 && prev.Length == 0)
                    {
                        cmbSource.Text = found[0];
                        LoadSourceAsync(found[0]);
                    }
                    else
                        lblSrcStatus.Text = "Найдено образов: " + found.Count + " - нажмите на стрелку ▼ справа в поле «Файл» и выберите образ";
                });
                Log("Поиск образов: найдено " + found.Count);
            });
            t.IsBackground = true; t.Start();
        }

        static void ScanDir(string dir, int depth, List<string> found, string[] skip)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    if (e == ".iso" || e == ".wim" || e == ".esd")
                    {
                        string n = Path.GetFileName(f).ToLowerInvariant();
                        if (n == "boot.wim") continue;
                        found.Add(f);
                    }
                }
                if (depth >= 3) return;
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub).ToLowerInvariant();
                    if (Array.IndexOf(skip, name) >= 0) continue;
                    ScanDir(sub, depth + 1, found, skip);
                }
            }
            catch { }
        }

        void CleanupExtracted()
        {
            if (extractedTmp != null)
            {
                try { if (File.Exists(extractedTmp)) File.Delete(extractedTmp); } catch { }
                try { Directory.Delete(Path.GetDirectoryName(extractedTmp)); } catch { }
                extractedTmp = null;
            }
        }

        void LoadSourceAsync(string path)
        {
            path = (path ?? "").Trim().Trim('"');
            if (path.Length == 0 || busy || installing) return;
            if (!File.Exists(path)) { lblSrcStatus.Text = "Файл не найден: " + path; return; }
            SetBusy(true);
            lblSrcStatus.Text = "Чтение образа...";
            cmbEdition.Items.Clear();
            Thread t = new Thread(delegate()
            {
                string err = null;
                List<ImageInfo> list = null;
                string wim = null;
                try { wim = ResolveImage(path, out err); if (wim != null) list = ReadImages(wim); }
                catch (Exception ex) { err = ex.Message; }
                UI(delegate
                {
                    SetBusy(false);
                    if (wim == null || list == null || list.Count == 0)
                    {
                        imageFile = null; images = new List<ImageInfo>();
                        lblSrcStatus.Text = "Ошибка: " + (err ?? "в образе не найдено ни одной редакции");
                        Log("Ошибка источника: " + lblSrcStatus.Text);
                        autoStart = false;
                        return;
                    }
                    imageFile = wim; images = list;
                    sourceDisks = Native.DisksOfPath(path);
                    foreach (int n in Native.DisksOfPath(wim)) if (!sourceDisks.Contains(n)) sourceDisks.Add(n);
                    cmbEdition.Items.Clear();
                    int pick = 0;
                    for (int i = 0; i < list.Count; i++)
                    {
                        cmbEdition.Items.Add(list[i]);
                        string nm = list[i].Name;
                        if (pick == 0 && Regex.IsMatch(nm, @"\bPro\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(nm, @"Education|Workstation|\bN\b", RegexOptions.IgnoreCase)) pick = i;
                    }
                    if (pendingEdition != null)
                    {
                        int idx = MatchEdition(list, pendingEdition);
                        if (idx >= 0) pick = idx; else Log("Редакция из профиля не найдена: " + pendingEdition);
                        pendingEdition = null;
                    }
                    cmbEdition.SelectedIndex = pick;
                    lblSrcStatus.Text = "Образ: " + wim + "  (редакций: " + list.Count + ")";
                    Log("Источник: " + wim + ", редакций: " + list.Count);
                    sourcePath = path;
                    sourceLetters = new List<char>();
                    foreach (string q in new string[] { path, wim })
                        if (q.Length > 1 && q[1] == ':' && !sourceLetters.Contains(char.ToUpper(q[0]))) sourceLetters.Add(char.ToUpper(q[0]));
                    partsDisk = -1;
                    RefreshDisks();
                    AutoContinue();
                });
            });
            t.IsBackground = true; t.Start();
        }

        /// <summary>Возвращает путь к install.wim/esd/или самому wim. Для ISO - монтирует или распаковывает через 7z.</summary>
        string ResolveImage(string path, out string err)
        {
            err = null;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".iso") return path;

            CleanupExtracted();
            string mountErr;
            if (iso.Mount(path, out mountErr))
            {
                Log("ISO подключён как " + iso.Letter);
                foreach (string n in new string[] { "install.wim", "install.esd" })
                {
                    string p = iso.Letter + "\\sources\\" + n;
                    if (File.Exists(p)) return p;
                }
                if (File.Exists(iso.Letter + "\\sources\\install.swm"))
                { err = "ISO содержит разбитый install.swm - не поддерживается"; return null; }
                err = "в ISO нет sources\\install.wim / install.esd (это не установочный образ Windows?)";
                return null;
            }
            Log("Монтирование ISO не удалось (" + mountErr + "), пробую 7-Zip");

            string sz = Util.FindTool("7z.exe");
            if (sz == null) { err = "не удалось подключить ISO (" + mountErr + ") и не найден 7z.exe (положите в папку tools рядом с программой)"; return null; }
            long need = new FileInfo(path).Length;
            string baseDir = null;
            List<DriveInfo> cands = new List<DriveInfo>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if ((d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.IsReady && d.AvailableFreeSpace > need + 200L * 1048576)
                        cands.Add(d);
                }
                catch { }
            }
            // сначала диск, где лежит ISO
            cands.Sort(delegate(DriveInfo a, DriveInfo b)
            {
                bool sa = path.StartsWith(a.Name, StringComparison.OrdinalIgnoreCase), sb = path.StartsWith(b.Name, StringComparison.OrdinalIgnoreCase);
                return sa == sb ? 0 : (sa ? -1 : 1);
            });
            if (cands.Count > 0) baseDir = cands[0].Name;
            if (baseDir == null) { err = "нет места для распаковки install.wim из ISO"; return null; }
            string tmp = Path.Combine(baseDir, "_wininstaller_tmp");
            Directory.CreateDirectory(tmp);
            UI(delegate { lblSrcStatus.Text = "Распаковка install.wim из ISO в " + tmp + " (несколько минут)..."; });
            int rc = RunProc(sz, "e -y -o\"" + tmp + "\" \"" + path + "\" sources\\install.wim sources\\install.esd", null, null, true);
            foreach (string n in new string[] { "install.wim", "install.esd" })
            {
                string p = Path.Combine(tmp, n);
                if (File.Exists(p)) { extractedTmp = p; return p; }
            }
            err = "7-Zip не смог извлечь install.wim (код " + rc + ")";
            return null;
        }

        List<ImageInfo> ReadImages(string wim)
        {
            List<ImageInfo> l = WimReader.FromHeader(wim);
            if (l != null) return l;
            Log("Заголовок WIM не прочитан напрямую, использую dism /Get-WimInfo");
            List<string> raw = new List<string>();
            l = WimReader.FromDism(wim, raw);
            if (l.Count == 0) foreach (string s in raw) Log("dism: " + s);
            return l;
        }

        // ------------------------------------------------------------ план разметки

        int SelectedSizeMB()
        {
            return chkAll.Checked ? 0 : (int)nudSize.Value;     // 0 = весь диск
        }

        long OverheadMB(bool gpt) { return gpt ? EfiMB + MsrMB + 4 : 2; }

        class PartItem
        {
            public char Letter;
            public string Text;
            public override string ToString() { return Text; }
        }

        void ApplyModeLayout()
        {
            bool keep = rbKeep.Checked;
            lblSize.Visible = keep ? false : true; nudSize.Visible = !keep; lblSizeHint.Visible = !keep;
            chkAll.Visible = !keep; chkData.Visible = !keep;
            lblPart.Visible = keep; cmbPart.Visible = keep;
            rbGpt.Enabled = !keep; rbMbr.Enabled = !keep;
            grpDisk.Text = keep ? "2. Диск (форматируется только выбранный раздел Windows, остальное не трогается)"
                                : "2. Диск для установки (ВСЕ ДАННЫЕ НА НЁМ БУДУТ УДАЛЕНЫ)";
            partsDisk = -1;
            if (IsHandleCreated) RefreshDisks(); else UpdatePlanText();
        }

        void FillParts()
        {
            if (cmbPart == null || !rbKeep.Checked) return;
            DiskInfo d = SelectedDisk();
            int n = d == null ? -2 : d.Number;
            if (n == partsDisk) return;
            partsDisk = n;
            cmbPart.Items.Clear();
            if (d == null) return;
            int pick = -1;
            foreach (char l in d.Letters)
            {
                if (sourceLetters.Contains(l)) continue;
                string t = l + ":";
                try
                {
                    DriveInfo di = new DriveInfo(l.ToString());
                    if (di.IsReady) t += " " + di.VolumeLabel + " (" + di.DriveFormat + ", " + Util.FmtSize(di.TotalSize) + ")";
                }
                catch { }
                if (Directory.Exists(l + ":\\Windows\\System32"))
                {
                    t += "   [установлена Windows]";
                    if (pick < 0) pick = cmbPart.Items.Count;
                }
                PartItem pi = new PartItem(); pi.Letter = l; pi.Text = t;
                cmbPart.Items.Add(pi);
            }
            if (pick >= 0) cmbPart.SelectedIndex = pick;
            if (d.Style == "GPT") rbGpt.Checked = true; else if (d.Style == "MBR") rbMbr.Checked = true;
        }

        void UpdatePlanText()
        {
            if (lblPlan == null || chkAll == null || cmbPart == null) return;
            DiskInfo d = SelectedDisk();
            if (rbKeep.Checked)
            {
                FillParts();
                PartItem pi = cmbPart.SelectedItem as PartItem;
                string boot = d == null ? "" : d.Style == "GPT" ? "в существующий EFI-раздел (старый BCD заменяется)"
                    : d.Style == "MBR" ? "в раздел Windows (он станет активным)" : "стиль диска не определён (нужны права администратора)";
                lblPlan.Text = pi == null
                    ? "Выберите диск и раздел с Windows, который нужно переустановить."
                    : "Форматируется ТОЛЬКО раздел " + pi.Letter + ":. Остальные разделы диска не трогаются.\nЗагрузчик: " + boot;
                return;
            }
            bool gpt = rbGpt.Checked;
            int mb = SelectedSizeMB();
            chkData.Enabled = mb != 0;
            if (mb == 0) chkData.Checked = false;
            string head = gpt ? "GPT:  EFI " + EfiMB + " МБ  +  MSR " + MsrMB + " МБ  +  Windows " : "MBR:  Windows (активный, с загрузчиком) ";
            string win = mb == 0 ? "весь остаток" : mb + " МБ";
            string s = head + win;
            if (d != null && mb != 0)
            {
                long free = d.Size / 1048576 - OverheadMB(gpt) - mb;
                if (chkData.Checked) s += "  +  Data " + Util.FmtSize(Math.Max(0, free) * 1048576);
                else if (free > 0) s += "   (не размечено: " + Util.FmtSize(free * 1048576) + ")";
            }
            if (firmware == "UEFI" && !gpt) s += "\nВнимание: ПК загружен в UEFI, а выбран MBR - нужен режим Legacy/CSM.";
            if (firmware == "BIOS" && gpt) s += "\nВнимание: ПК загружен в BIOS, а выбран GPT - Windows с него не загрузится.";
            lblPlan.Text = s;
        }

        // ------------------------------------------------------------ профили и авторежим

        static string ResolvePath(string p)
        {
            p = (p ?? "").Trim().Trim('"');
            if (p.Length == 0 || Path.IsPathRooted(p)) return p;
            return Path.Combine(Util.AppDir(), p);
        }

        static Dictionary<string, string> ReadIni(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l[0] == '#' || l[0] == ';') continue;
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                d[l.Substring(0, i).Trim().ToLowerInvariant()] = l.Substring(i + 1).Trim();
            }
            return d;
        }

        static bool Truthy(string v)
        {
            v = v.Trim().ToLowerInvariant();
            return v == "1" || v == "yes" || v == "true" || v == "да";
        }

        void ApplyProfile(string path)
        {
            Dictionary<string, string> d;
            try { d = ReadIni(path); }
            catch (Exception ex) { Log("Профиль не прочитан: " + ex.Message); autoStart = false; return; }
            string v;
            if (d.TryGetValue("mode", out v)) { if (v.ToLowerInvariant() == "keep") rbKeep.Checked = true; else rbWipe.Checked = true; }
            if (d.TryGetValue("scheme", out v)) { v = v.ToLowerInvariant(); if (v == "gpt") rbGpt.Checked = true; else if (v == "mbr") rbMbr.Checked = true; }
            if (d.TryGetValue("sizemb", out v))
            {
                int n;
                if (v.ToLowerInvariant() == "all") chkAll.Checked = true;
                else if (int.TryParse(v, out n))
                {
                    chkAll.Checked = false;
                    nudSize.Value = Math.Max(nudSize.Minimum, Math.Min(nudSize.Maximum, n));
                }
            }
            if (d.TryGetValue("data", out v)) chkData.Checked = Truthy(v);
            if (d.TryGetValue("drivers", out v)) { if (v.Length > 0) { txtDrivers.Text = ResolvePath(v); chkDrivers.Checked = true; } else chkDrivers.Checked = false; }
            if (d.TryGetValue("xml", out v)) txtInstXml.Text = v.Length > 0 ? ResolvePath(v) : "";
            if (d.TryGetValue("reboot", out v)) chkReboot.Checked = Truthy(v);
            if (d.TryGetValue("verify", out v)) chkVerify.Checked = Truthy(v);
            pendingSource = d.TryGetValue("source", out v) && v.Length > 0 ? v : null;
            pendingEdition = d.TryGetValue("edition", out v) && v.Length > 0 ? v : null;
            pendingDisk = d.TryGetValue("disk", out v) && v.Length > 0 ? v : null;
            Log("Профиль загружен: " + path + (autoStart ? "  (авторежим)" : ""));
            UpdatePlanText();
        }

        void SaveProfile(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# WinInstaller profile. disk=auto - единственный внутренний (не USB) диск, либо номер диска.");
            sb.AppendLine("source=" + cmbSource.Text.Trim());
            ImageInfo ii = cmbEdition.SelectedItem as ImageInfo;
            sb.AppendLine("edition=" + (ii != null ? ii.Name : ""));
            sb.AppendLine("mode=" + (rbKeep.Checked ? "keep" : "wipe"));
            sb.AppendLine("scheme=" + (rbKeep.Checked ? "auto" : rbGpt.Checked ? "gpt" : "mbr"));
            sb.AppendLine("sizeMB=" + (chkAll.Checked ? "all" : nudSize.Value.ToString()));
            sb.AppendLine("data=" + (chkData.Checked ? "1" : "0"));
            sb.AppendLine("drivers=" + (chkDrivers.Checked ? txtDrivers.Text.Trim() : ""));
            sb.AppendLine("xml=" + txtInstXml.Text.Trim());
            sb.AppendLine("reboot=" + (chkReboot.Checked ? "1" : "0"));
            sb.AppendLine("verify=" + (chkVerify.Checked ? "1" : "0"));
            sb.AppendLine("disk=auto");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        static int MatchEdition(List<ImageInfo> list, string want)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Name, want, StringComparison.OrdinalIgnoreCase)) return i;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) return i;
            int n;
            if (int.TryParse(want, out n))
                for (int i = 0; i < list.Count; i++) if (list[i].Index == n) return i;
            return -1;
        }

        void ResolvePendingSource(List<string> found)
        {
            string pend = pendingSource;
            if (pend == null) return;
            pendingSource = null;
            string use = null;
            if (File.Exists(pend)) use = pend;
            else
            {
                string nm = Path.GetFileName(pend);
                foreach (string f in found)
                    if (string.Equals(Path.GetFileName(f), nm, StringComparison.OrdinalIgnoreCase)) { use = f; break; }
            }
            if (use != null) { cmbSource.Text = use; LoadSourceAsync(use); }
            else
            {
                lblSrcStatus.Text = "Образ из профиля не найден: " + pend;
                Log(lblSrcStatus.Text);
                autoStart = false;
            }
        }

        char FindWindowsLetter(DiskInfo d)
        {
            foreach (char l in d.Letters)
                if (!sourceLetters.Contains(l) && Directory.Exists(l + ":\\Windows\\System32")) return l;
            return ' ';
        }

        DiskInfo AutoPickDisk()
        {
            List<DiskInfo> c = new List<DiskInfo>();
            int want;
            bool numeric = int.TryParse(pendingDisk ?? "", out want);
            foreach (ListViewItem it in lvDisks.Items)
            {
                DiskInfo d = (DiskInfo)it.Tag;
                if (d.Blocked) continue;
                if (numeric) { if (d.Number == want) c.Add(d); continue; }
                if (d.Bus == "USB" || d.Removable) continue;
                if (rbKeep.Checked && FindWindowsLetter(d) == ' ') continue;
                c.Add(d);
            }
            return c.Count == 1 ? c[0] : null;
        }

        void AutoContinue()
        {
            if (!autoStart) return;
            autoStart = false;
            DiskInfo pick = AutoPickDisk();
            if (pick == null)
            {
                Log("Авторежим: диск однозначно определить не удалось - выберите диск и нажмите «Установить».");
                return;
            }
            foreach (ListViewItem it in lvDisks.Items)
                if (((DiskInfo)it.Tag).Number == pick.Number) { it.Selected = true; it.EnsureVisible(); }
            Log("Авторежим: выбран диск " + pick.Number + " (" + pick.Model + ")" + (autoYes ? ", подтверждение отключено (--yes)" : ""));
            StartInstall(autoYes);
        }

        // ------------------------------------------------------------ звук и отсчёт перед перезагрузкой

        static void Beep(bool ok)
        {
            if (Program.DryRun) return;
            try { if (ok) System.Media.SystemSounds.Asterisk.Play(); else System.Media.SystemSounds.Hand.Play(); } catch { }
            try { Console.Beep(ok ? 1000 : 400, 250); } catch { }
        }

        bool CountdownDialog(int seconds)
        {
            using (Form f = new Form())
            {
                f.Text = "Готово"; f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(440, 160);
                f.MinimizeBox = false; f.MaximizeBox = false; f.ShowInTaskbar = false; f.Font = Font;
                Label l = Add(f, new Label(), 16, 14, 408, 90);
                l.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
                Button b1 = Add(f, new Button { Text = "Перезагрузить сейчас", DialogResult = DialogResult.Yes }, 16, 112, 200, 34);
                Button b2 = Add(f, new Button { Text = "Отмена", DialogResult = DialogResult.No }, 224, 112, 200, 34);
                f.AcceptButton = b1; f.CancelButton = b2;
                int left = seconds;
                Action upd = delegate { l.Text = "Установка завершена.\nВЫТАЩИТЕ УСТАНОВОЧНУЮ ФЛЕШКУ!\n\nПерезагрузка через " + left + " с."; };
                upd();
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 1000;
                t.Tick += delegate { left--; if (left <= 0) { t.Stop(); f.DialogResult = DialogResult.Yes; } else upd(); };
                f.Shown += delegate { t.Start(); };
                bool r = f.ShowDialog(this) == DialogResult.Yes;
                t.Dispose();
                return r;
            }
        }

        // ------------------------------------------------------------ захват образа

        class VolInfo
        {
            public char Letter;
            public long Size, Used;
            public int Gen = -2;   // GeneralizationState: 7 = обобщена, -2 = неизвестно
        }

        class CapturePlan
        {
            public char Volume;
            public string Out, Name, Desc, Compress, Xml;
            public bool DryRun, Hash;
        }

        void BuildCaptureUi()
        {
            GroupBox c1 = Add(panelCapture, new GroupBox(), 12, 8, 876, 226);
            c1.Text = "1. Windows-раздел для захвата (система уже должна быть обобщена: sysprep /generalize)";
            c1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lvVols = Add(c1, new ListView(), 12, 24, 852, 150);
            lvVols.View = View.Details; lvVols.FullRowSelect = true; lvVols.MultiSelect = false; lvVols.HideSelection = false; lvVols.GridLines = true;
            lvVols.Columns.Add("Раздел", 62);
            lvVols.Columns.Add("Диск", 50);
            lvVols.Columns.Add("Метка", 140);
            lvVols.Columns.Add("Размер", 85);
            lvVols.Columns.Add("Занято", 85);
            lvVols.Columns.Add("Состояние sysprep", 400);
            lvVols.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnVolRefresh = Add(c1, new Button { Text = "Обновить" }, 12, 182, 100, 28);
            lblVolHint = Add(c1, new Label(), 124, 182, 740, 36);
            lblVolHint.ForeColor = Color.DimGray;
            lblVolHint.Text = "Показаны установленные Windows (кроме той, из которой запущена программа). Sysprep выполняется в самой Windows (D.cmd), захват - отсюда, из WinPE.";

            GroupBox c2 = Add(panelCapture, new GroupBox(), 12, 240, 876, 176);
            c2.Text = "2. Файл образа"; c2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Add(c2, new Label { Text = "Сохранить в:" }, 12, 32, 120, 22);
            txtOut = Add(c2, new TextBox(), 140, 28, 610, 26);
            txtOut.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnOut = Add(c2, new Button { Text = "Обзор..." }, 758, 26, 106, 28);
            btnOut.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Add(c2, new Label { Text = "Имя образа:" }, 12, 68, 120, 22);
            txtName = Add(c2, new TextBox { Text = "Windows" }, 140, 64, 724, 26);
            txtName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Add(c2, new Label { Text = "Описание:" }, 12, 104, 120, 22);
            txtDesc = Add(c2, new TextBox(), 140, 100, 724, 26);
            txtDesc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Add(c2, new Label { Text = "Сжатие:" }, 12, 140, 120, 22);
            cmbCompress = Add(c2, new ComboBox(), 140, 136, 340, 26);
            cmbCompress.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbCompress.Items.AddRange(new object[] { "fast - быстро (рекомендуется)", "max - максимальное, очень долго", "none - без сжатия" });
            cmbCompress.SelectedIndex = 0;
            chkHash = Add(c2, new CheckBox { Text = "Записать SHA-256 (файл .sha256 рядом)", Checked = true }, 500, 138, 364, 24);
            string app = Util.AppDir();
            if (app.Length > 2 && !app.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
                txtOut.Text = Path.Combine(app, "Images\\Windows_" + DateTime.Now.ToString("yyyyMMdd") + ".wim");

            GroupBox c3 = Add(panelCapture, new GroupBox(), 12, 422, 876, 130);
            c3.Text = "3. XML ответов (необязательно)"; c3.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtXml = Add(c3, new TextBox(), 14, 28, 600, 26);
            txtXml.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnXml = Add(c3, new Button { Text = "Обзор..." }, 622, 26, 118, 28);
            btnXml.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnXmlClear = Add(c3, new Button { Text = "Без XML" }, 746, 26, 118, 28);
            btnXmlClear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Label xh = Add(c3, new Label(), 14, 62, 850, 62);
            xh.ForeColor = Color.DimGray;
            xh.Text = "Если указан - на время захвата копируется в Windows\\Panther\\Unattend\\Unattend.xml захватываемой системы, и попадает в образ "
                + "(применится при первом запуске развёрнутого образа). После захвата исходный раздел возвращается как был. Пусто - захват без XML (как есть).";

            btnVolRefresh.Click += delegate { RefreshVolumes(); };
            btnOut.Click += delegate
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "WIM-образ (*.wim)|*.wim"; sfd.DefaultExt = "wim"; sfd.OverwritePrompt = false;
                    if (txtOut.Text.Length > 3) { try { sfd.InitialDirectory = Path.GetDirectoryName(txtOut.Text); sfd.FileName = Path.GetFileName(txtOut.Text); } catch { } }
                    if (sfd.ShowDialog(this) == DialogResult.OK) txtOut.Text = sfd.FileName;
                }
            };
            btnXml.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "XML (*.xml)|*.xml|Все файлы|*.*";
                    if (ofd.ShowDialog(this) == DialogResult.OK) txtXml.Text = ofd.FileName;
                }
            };
            btnXmlClear.Click += delegate { txtXml.Text = ""; };
        }

        void SwitchMode()
        {
            bool cap = rbModeCapture.Checked;
            panelCapture.Visible = cap;
            panelInstall.Visible = !cap;
            btnInstall.Text = cap ? "ЗАХВАТИТЬ ОБРАЗ" : "УСТАНОВИТЬ WINDOWS";
            StyleModeButtons();
            if (cap) RefreshVolumes();
        }

        void StyleModeButtons()
        {
            foreach (RadioButton r in new RadioButton[] { rbModeInstall, rbModeCapture })
            {
                r.FlatStyle = FlatStyle.Flat;
                r.FlatAppearance.CheckedBackColor = Color.FromArgb(0, 120, 215);
                r.BackColor = r.Checked ? Color.FromArgb(0, 120, 215) : SystemColors.Control;
                r.ForeColor = r.Checked ? Color.White : SystemColors.ControlText;
                r.Font = new Font(Font.FontFamily, 9.5f, r.Checked ? FontStyle.Bold : FontStyle.Regular);
            }
        }

        static int RunQuiet(string exe, string args, out string output)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            Encoding enc = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardOutputEncoding = enc; psi.StandardErrorEncoding = enc;
            using (Process p = Process.Start(psi))
            {
                string err = null;
                Thread te = new Thread(delegate() { try { err = p.StandardError.ReadToEnd(); } catch { } });
                te.Start();
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(); te.Join(2000);
                output += err;
                return p.ExitCode;
            }
        }

        /// <summary>Читает GeneralizationState из офлайн-куста SYSTEM. 7 = sysprep /generalize выполнен; -2 = не удалось проверить.</summary>
        static int CheckGeneralized(char letter)
        {
            string reg = Util.FindTool("reg.exe") ?? "reg.exe";
            const string key = "HKLM\\WI_OFF";
            string o;
            try
            {
                RunQuiet(reg, "unload " + key, out o);
                if (RunQuiet(reg, "load " + key + " \"" + letter + ":\\Windows\\System32\\config\\SYSTEM\"", out o) != 0) return -2;
                int res = 0;
                try
                {
                    if (RunQuiet(reg, "query \"" + key + "\\Setup\\Status\\SysprepStatus\" /v GeneralizationState", out o) == 0)
                    {
                        Match m = Regex.Match(o, @"0x([0-9a-fA-F]+)");
                        if (m.Success) res = Convert.ToInt32(m.Groups[1].Value, 16);
                    }
                }
                finally
                {
                    for (int i = 0; i < 6; i++)
                    {
                        GC.Collect();
                        if (RunQuiet(reg, "unload " + key, out o) == 0) break;
                        Thread.Sleep(400);
                    }
                }
                return res;
            }
            catch { return -2; }
        }

        static string GenText(int gen)
        {
            if (gen == 7) return "OK - система обобщена (sysprep выполнен)";
            if (gen == -2) return "не удалось проверить";
            return "НЕ обобщена (state=" + gen + ") - сначала запустите D.cmd в этой Windows";
        }

        void RefreshVolumes()
        {
            lvVols.Items.Clear();
            char sysL = char.ToUpper((Environment.GetEnvironmentVariable("SystemDrive") ?? "X:")[0]);
            List<ListViewItem> items = new List<ListViewItem>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                char c = char.ToUpper(d.Name[0]);
                if (c == sysL || c == 'A' || c == 'B') continue;
                try
                {
                    if (!d.IsReady || !File.Exists(c + ":\\Windows\\System32\\config\\SYSTEM")) continue;
                    VolInfo v = new VolInfo();
                    v.Letter = c; v.Size = d.TotalSize; v.Used = d.TotalSize - d.TotalFreeSpace;
                    List<int> dn = Native.DisksOfVolume(c);
                    string disk = dn.Count > 0 ? dn[0].ToString() : "?";
                    ListViewItem it = new ListViewItem(new string[] { c + ":", disk, d.VolumeLabel, Util.FmtSize(v.Size), Util.FmtSize(v.Used), "проверка..." });
                    it.Tag = v;
                    items.Add(it);
                    lvVols.Items.Add(it);
                }
                catch { }
            }
            if (items.Count == 0) { lblVolHint.Text = "Установленных Windows не найдено."; return; }
            Thread t = new Thread(delegate()
            {
                foreach (ListViewItem it in items)
                {
                    VolInfo v = (VolInfo)it.Tag;
                    v.Gen = CheckGeneralized(v.Letter);
                    ListViewItem cur = it;
                    UI(delegate
                    {
                        cur.SubItems[5].Text = GenText(v.Gen);
                        cur.ForeColor = v.Gen == 7 ? Color.DarkGreen : Color.Firebrick;
                    });
                    Log("Раздел " + v.Letter + ": " + GenText(v.Gen));
                }
            });
            t.IsBackground = true; t.Start();
        }

        void StartCapture()
        {
            if (installing) return;
            if (lvVols.SelectedItems.Count == 0) { MessageBox.Show(this, "Выберите Windows-раздел для захвата.", "Нет раздела"); return; }
            VolInfo v = (VolInfo)lvVols.SelectedItems[0].Tag;

            string outp = txtOut.Text.Trim().Trim('"');
            if (outp.Length < 4 || outp[1] != ':' || outp[2] != '\\') { MessageBox.Show(this, "Укажите полный путь к файлу образа (например E:\\Images\\win.wim).", "Файл образа"); return; }
            if (!outp.EndsWith(".wim", StringComparison.OrdinalIgnoreCase)) outp += ".wim";
            txtOut.Text = outp;
            if (char.ToUpper(outp[0]) == v.Letter) { MessageBox.Show(this, "Нельзя сохранять образ на тот же раздел, который захватывается.", "Файл образа"); return; }
            DriveInfo od = new DriveInfo(outp.Substring(0, 1));
            long free;
            try { if (!od.IsReady) throw new IOException(); free = od.AvailableFreeSpace; }
            catch { MessageBox.Show(this, "Диск " + outp.Substring(0, 2) + " недоступен.", "Файл образа"); return; }

            string xml = txtXml.Text.Trim().Trim('"');
            if (xml.Length > 0)
            {
                if (!File.Exists(xml)) { MessageBox.Show(this, "XML-файл не найден: " + xml, "XML"); return; }
                try { XmlDocument x = new XmlDocument(); x.Load(xml); }
                catch (Exception ex) { MessageBox.Show(this, "XML-файл повреждён: " + ex.Message, "XML"); return; }
            }

            if (v.Gen != 7)
            {
                if (MessageBox.Show(this, "Раздел " + v.Letter + ": - " + GenText(v.Gen) + ".\n\nОбраз необобщённой системы нельзя использовать для массового развёртывания.\nВсё равно захватывать?",
                    "Sysprep", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }
            if (free < v.Used / 2)
            {
                if (MessageBox.Show(this, "На " + outp.Substring(0, 2) + " свободно " + Util.FmtSize(free) + ", а на разделе занято " + Util.FmtSize(v.Used) + ". Образ может не поместиться. Продолжить?",
                    "Мало места", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }
            if (File.Exists(outp))
            {
                if (MessageBox.Show(this, "Файл уже существует и будет перезаписан:\n" + outp, "Перезапись", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }

            CapturePlan c = new CapturePlan();
            c.Volume = v.Letter; c.Out = outp;
            c.Name = txtName.Text.Replace("\"", "").Trim(); if (c.Name.Length == 0) c.Name = "Windows";
            c.Desc = txtDesc.Text.Replace("\"", "").Trim();
            c.Compress = new string[] { "fast", "max", "none" }[Math.Max(0, cmbCompress.SelectedIndex)];
            c.Xml = xml.Length > 0 ? xml : null;
            c.DryRun = Program.DryRun;
            c.Hash = chkHash.Checked;

            string msg = "Захват раздела " + v.Letter + ": (занято " + Util.FmtSize(v.Used) + ")\nв файл " + outp + "\nИмя: " + c.Name + ", сжатие: " + c.Compress
                + "\nXML: " + (c.Xml ?? "без XML") + (c.DryRun ? "\n\n[DRY-RUN - реально ничего не будет сделано]" : "") + "\n\nНачать?";
            if (MessageBox.Show(this, msg, "Подтверждение захвата", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            SetWorking(true);
            Thread t = new Thread(delegate() { RunCapture(c); });
            t.IsBackground = true; t.Start();
        }

        void RunCapture(CapturePlan c)
        {
            string V = c.Volume + ":";
            string xmlDest = V + "\\Windows\\Panther\\Unattend\\Unattend.xml";
            string xmlBak = null;
            bool xmlPlaced = false;
            string scratch = null;
            try
            {
                UI(delegate { lblStatus.ForeColor = SystemColors.ControlText; });
                string dism = Util.FindTool("dism.exe");
                if (!c.DryRun && dism == null) { Fail("Не найден dism.exe"); return; }
                dism = dism ?? "dism.exe";
                string outDir = Path.GetDirectoryName(c.Out);
                scratch = Path.Combine(outDir, "_dism_tmp_capture");
                SetProgress(1);

                if (!c.DryRun)
                {
                    Directory.CreateDirectory(outDir);
                    Directory.CreateDirectory(scratch);
                    if (File.Exists(c.Out)) File.Delete(c.Out);
                }

                if (c.Xml != null)
                {
                    SetStatus("Копирование XML в " + xmlDest);
                    if (!c.DryRun)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(xmlDest));
                        if (File.Exists(xmlDest)) { xmlBak = xmlDest + ".wi_bak"; File.Copy(xmlDest, xmlBak, true); }
                        File.Copy(c.Xml, xmlDest, true);
                        xmlPlaced = true;
                    }
                }

                SetStatus("Захват " + V + " в " + c.Out + " - долго, не выключайте ПК...");
                string args = "/Capture-Image /ImageFile:\"" + c.Out + "\" /CaptureDir:" + V + "\\ /Name:\"" + c.Name + "\""
                    + (c.Desc.Length > 0 ? " /Description:\"" + c.Desc + "\"" : "")
                    + " /Compress:" + c.Compress + " /CheckIntegrity /ScratchDir:\"" + scratch + "\"";
                int rc = RunProc(dism, args, delegate(double p)
                {
                    SetProgress(2 + (int)(p * 0.97));
                    UI(delegate { lblStatus.Text = "Захват образа: " + p.ToString("0.0") + "%"; });
                }, null, false);
                if (rc != 0) { Fail("dism /Capture-Image завершился с кодом " + rc); return; }

                long size = 0;
                if (!c.DryRun)
                {
                    if (!File.Exists(c.Out)) { Fail("dism отработал, но файл образа не найден: " + c.Out); return; }
                    size = new FileInfo(c.Out).Length;
                    if (size < 1024 * 1024) { Fail("Файл образа подозрительно мал: " + size + " байт"); return; }
                    List<ImageInfo> chk = WimReader.FromHeader(c.Out);
                    if (chk == null) Log("Предупреждение: заголовок образа не удалось прочитать для проверки.");
                    else Log("Проверка образа: заголовок читается, редакций: " + chk.Count + ".");
                    if (c.Hash)
                    {
                        SetStatus("Расчёт SHA-256...");
                        string hex = Util.Sha256File(c.Out, delegate(int pc)
                        {
                            SetProgress(pc);
                            UI(delegate { lblStatus.Text = "Расчёт SHA-256: " + pc + "%"; });
                        });
                        File.WriteAllText(c.Out + ".sha256", hex + " *" + Path.GetFileName(c.Out) + "\r\n", Encoding.ASCII);
                        Log("SHA-256: " + hex);
                    }
                }
                SetProgress(100);
                Beep(true);
                SetStatus("ГОТОВО. Образ: " + c.Out + (size > 0 ? " (" + Util.FmtSize(size) + ")" : ""));
                UI(delegate
                {
                    MessageBox.Show(this, c.DryRun ? "DRY-RUN завершён: команды смотрите в логе." : "Образ захвачен:\n" + c.Out + "\nРазмер: " + Util.FmtSize(size), "Готово");
                });
                UI(delegate { FinishUi(); });
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
            finally
            {
                // вернуть исходный раздел в прежнее состояние
                try
                {
                    if (xmlPlaced)
                    {
                        if (xmlBak != null) { File.Copy(xmlBak, xmlDest, true); File.Delete(xmlBak); }
                        else File.Delete(xmlDest);
                    }
                }
                catch (Exception ex) { Log("Не удалось восстановить " + xmlDest + ": " + ex.Message); }
                try { if (scratch != null && !c.DryRun && Directory.Exists(scratch)) Directory.Delete(scratch, true); } catch { }
            }
        }

        // ------------------------------------------------------------ запуск установки

        void StartInstall(bool skipConfirm)
        {
            if (installing || busy) return;
            if (imageFile == null || cmbEdition.SelectedItem == null) { MessageBox.Show(this, "Выберите образ и редакцию.", "Нет образа"); return; }
            DiskInfo d = SelectedDisk();
            if (d == null) { MessageBox.Show(this, "Выберите диск для установки.", "Нет диска"); return; }
            bool keep = rbKeep.Checked;
            if (!keep && d.Blocked) { MessageBox.Show(this, "На этом диске лежит образ - выберите другой диск.", "Диск-источник"); return; }

            Plan p = new Plan();
            p.ImageFile = imageFile;
            p.SourcePath = sourcePath;
            p.ImageIndex = ((ImageInfo)cmbEdition.SelectedItem).Index;
            p.Disk = d;
            p.Keep = keep;
            p.Reboot = chkReboot.Checked;
            p.VerifyHash = chkVerify.Checked;
            p.DryRun = Program.DryRun;
            if (chkDrivers.Checked)
            {
                string dd = txtDrivers.Text.Trim().TrimEnd('\\');
                if (!Directory.Exists(dd)) { MessageBox.Show(this, "Папка с драйверами не найдена: " + dd, "Драйверы"); return; }
                p.DriversDir = dd;
            }
            string xml = ResolvePath(txtInstXml.Text);
            if (xml.Length > 0)
            {
                if (!File.Exists(xml)) { MessageBox.Show(this, "XML-файл не найден: " + xml, "XML"); return; }
                try { XmlDocument xd = new XmlDocument(); xd.Load(xml); }
                catch (Exception ex) { MessageBox.Show(this, "XML-файл повреждён: " + ex.Message, "XML"); return; }
                p.Xml = xml;
            }

            string vols = d.Volumes.Count > 0 ? string.Join("\n    ", d.Volumes.ToArray()) : "(нет томов с буквами)";
            string msg;
            if (keep)
            {
                if (d.Style != "GPT" && d.Style != "MBR") { MessageBox.Show(this, "Не удалось определить таблицу разделов диска (нужны права администратора).", "Диск"); return; }
                PartItem pi = cmbPart.SelectedItem as PartItem;
                if (pi == null) { MessageBox.Show(this, "Выберите раздел, в который ставить Windows.", "Раздел"); return; }
                if (sourceLetters.Contains(pi.Letter)) { MessageBox.Show(this, "В этом разделе лежит образ - выберите другой.", "Раздел"); return; }
                p.Gpt = d.Style == "GPT";
                p.WinLetter = pi.Letter;
                long psz = 0;
                try { psz = new DriveInfo(pi.Letter.ToString()).TotalSize; } catch { }
                if (psz < 15L * 1073741824) { MessageBox.Show(this, "Раздел слишком мал для Windows (нужно хотя бы 15 ГБ).", "Раздел"); return; }
                p.WinPart = Native.PartitionNumberOfVolume(pi.Letter);
                if (p.WinPart <= 0 && !p.DryRun) { MessageBox.Show(this, "Не удалось определить номер раздела " + pi.Letter + ":.", "Раздел"); return; }
                if (p.Gpt)
                {
                    p.EspPart = Native.EspPartitionNumber(d.Number);
                    if (p.EspPart <= 0 && !p.DryRun) { MessageBox.Show(this, "На диске нет EFI-раздела. Используйте режим «Стереть весь диск».", "Загрузчик"); return; }
                }
                msg = "Диск " + d.Number + ":  " + d.Model + "  [" + Util.FmtSize(d.Size) + ", " + d.Bus + ", " + d.Style + "]\n"
                    + "Тома на нём:\n    " + vols + "\n\n"
                    + "БУДЕТ ОТФОРМАТИРОВАН ТОЛЬКО РАЗДЕЛ  " + pi.Text.Trim() + "\n"
                    + "Остальные разделы диска не затрагиваются.\n\n"
                    + "Редакция: " + cmbEdition.SelectedItem + "\n"
                    + "Загрузчик: " + (p.Gpt ? "UEFI, существующий EFI-раздел №" + p.EspPart : "BIOS, раздел " + pi.Letter + ": станет активным") + "\n\n";
            }
            else
            {
                p.Gpt = rbGpt.Checked;
                int mb = SelectedSizeMB();
                p.WinSizeMB = mb;
                p.DataPart = chkData.Checked && mb != 0;
                long diskMB = d.Size / 1048576;
                long avail = diskMB - OverheadMB(p.Gpt);
                if (avail < 20 * 1024) { MessageBox.Show(this, "Диск слишком мал для Windows (нужно хотя бы 20 ГБ).", "Размер"); return; }
                if (p.WinSizeMB != 0 && p.WinSizeMB + (p.DataPart ? 1024 : 0) > avail)
                { MessageBox.Show(this, "Выбранный размер не помещается на диск (доступно " + Util.FmtSize(avail * 1048576) + ").", "Размер"); return; }
                if (!p.Gpt && (p.WinSizeMB == 0 ? avail : p.WinSizeMB) > 2L * 1024 * 1024)
                { MessageBox.Show(this, "MBR не поддерживает раздел Windows больше 2 ТБ. Укажите размер меньше 2 ТБ.", "MBR"); return; }
                if (p.WinSizeMB != 0 && p.WinSizeMB < 20 * 1024) { MessageBox.Show(this, "Для Windows нужно хотя бы 20 ГБ.", "Размер"); return; }
                msg = "Диск " + d.Number + ":  " + d.Model + "  [" + Util.FmtSize(d.Size) + ", " + d.Bus + ", " + d.Style + "]\n"
                    + "Тома на нём:\n    " + vols + "\n\n"
                    + "ВСЕ ДАННЫЕ НА ЭТОМ ДИСКЕ БУДУТ УНИЧТОЖЕНЫ.\n\n"
                    + "Редакция: " + cmbEdition.SelectedItem + "\n"
                    + "Разметка: " + (p.Gpt ? "GPT/UEFI" : "MBR/BIOS") + ",  Windows: " + (mb == 0 ? "весь диск" : mb + " МБ") + (p.DataPart ? " + Data" : "") + "\n\n";
            }
            msg += "XML: " + (p.Xml ?? "без XML") + "\n\n"
                + (p.DryRun ? "[DRY-RUN - реально ничего не будет сделано]\n\n" : "") + "Продолжить?";
            if (skipConfirm) Log("Подтверждение пропущено (--yes).");
            else if (MessageBox.Show(this, msg, "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            SetWorking(true);
            Thread t = new Thread(delegate() { RunInstall(p); });
            t.IsBackground = true; t.Start();
        }

        // ------------------------------------------------------------ процессы

        /// <summary>Запускает процесс, читает вывод посимвольно (dism обновляет прогресс через \r).</summary>
        int RunProc(string exe, string args, Action<double> onPercent, List<string> capture, bool force)
        {
            Log("> " + Path.GetFileName(exe) + " " + args);
            if (Program.DryRun && !force) { Log("  [dry-run] пропущено"); return 0; }
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            Encoding enc = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardOutputEncoding = enc; psi.StandardErrorEncoding = enc;
            using (Process p = Process.Start(psi))
            {
                ThreadStart reader = null;
                Thread te = new Thread(delegate() { try { string s; while ((s = p.StandardError.ReadLine()) != null) if (s.Trim().Length > 0) Log("  " + s); } catch { } });
                te.IsBackground = true; te.Start();
                StringBuilder sb = new StringBuilder();
                Action flush = delegate()
                {
                    string line = sb.ToString().Trim(); sb.Length = 0;
                    if (line.Length == 0) return;
                    Match m = Regex.Match(line, @"(\d{1,3}(?:[.,]\d+)?)\s*%");
                    if (m.Success && line.StartsWith("["))
                    {
                        double v;
                        if (onPercent != null && double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) onPercent(v);
                        return;
                    }
                    if (capture != null) capture.Add(line);
                    Log("  " + line);
                };
                int ch;
                while ((ch = p.StandardOutput.Read()) >= 0)
                {
                    if (ch == '\r' || ch == '\n') flush(); else sb.Append((char)ch);
                }
                flush();
                p.WaitForExit();
                te.Join(2000);
                GC.KeepAlive(reader);
                return p.ExitCode;
            }
        }

        // ------------------------------------------------------------ сам процесс установки

        static char[] FreeLetters(int count)
        {
            HashSet<char> used = new HashSet<char>();
            foreach (DriveInfo d in DriveInfo.GetDrives()) used.Add(char.ToUpper(d.Name[0]));
            List<char> res = new List<char>();
            foreach (char c in "WSVUTRQPONMLKJIHGFE")
                if (!used.Contains(c)) { res.Add(c); if (res.Count == count) break; }
            return res.ToArray();
        }

        void Fail(string msg)
        {
            Beep(false);
            Log("ОШИБКА: " + msg);
            UI(delegate
            {
                lblStatus.Text = "ОШИБКА: " + msg;
                lblStatus.ForeColor = Color.Firebrick;
                MessageBox.Show(this, msg + "\n\nПодробности - в логе и в файле install.log.", "Ошибка установки", MessageBoxButtons.OK, MessageBoxIcon.Error);
                FinishUi();
            });
        }

        void SetWorking(bool w)
        {
            installing = w;
            btnInstall.Enabled = !w;
            panelInstall.Enabled = !w; panelCapture.Enabled = !w;
            rbModeInstall.Enabled = !w; rbModeCapture.Enabled = !w;
        }

        void FinishUi()
        {
            SetWorking(false);
            RefreshDisks();
            if (rbModeCapture.Checked) RefreshVolumes();
        }

        void RunInstall(Plan p)
        {
            try
            {
                UI(delegate { lblStatus.ForeColor = SystemColors.ControlText; });
                string dism = Util.FindTool("dism.exe");
                string bcdboot = Util.FindTool("bcdboot.exe");
                if (!p.DryRun && (dism == null || bcdboot == null)) { Fail("Не найден dism.exe или bcdboot.exe"); return; }
                dism = dism ?? "dism.exe"; bcdboot = bcdboot ?? "bcdboot.exe";

                // 0. контрольная сумма (до любых изменений диска)
                if (p.VerifyHash && p.SourcePath != null)
                {
                    string sc = p.SourcePath + ".sha256";
                    if (!File.Exists(sc)) Log("Файл " + Path.GetFileName(sc) + " не найден - проверка SHA-256 пропущена.");
                    else if (p.DryRun) Log("[dry-run] проверка SHA-256 по " + sc + " пропущена.");
                    else
                    {
                        Match em = Regex.Match(File.ReadAllText(sc), "[0-9a-fA-F]{64}");
                        if (!em.Success) Log("Не удалось прочитать хэш из " + sc + " - проверка пропущена.");
                        else
                        {
                            SetStatus("Проверка контрольной суммы образа (SHA-256)...");
                            string got = Util.Sha256File(p.SourcePath, delegate(int pc)
                            {
                                SetProgress(pc / 4);
                                UI(delegate { lblStatus.Text = "Проверка SHA-256: " + pc + "%"; });
                            });
                            if (!string.Equals(got, em.Value, StringComparison.OrdinalIgnoreCase))
                            { Fail("Контрольная сумма образа не совпала - файл повреждён.\nОжидалось " + em.Value + "\nПолучено   " + got + "\nДиск не тронут."); return; }
                            Log("SHA-256 совпала.");
                        }
                    }
                }

                char winL, efiL = ' ', dataL = ' ';
                if (p.Keep)
                {
                    winL = p.WinLetter;
                    if (p.Gpt)
                    {
                        char[] l1 = FreeLetters(1);
                        if (l1.Length < 1) { Fail("Нет свободных букв дисков"); return; }
                        efiL = l1[0];
                    }
                }
                else
                {
                    char[] lt = FreeLetters(p.DataPart ? 3 : 2);
                    if (lt.Length < (p.DataPart ? 3 : 2)) { Fail("Нет свободных букв дисков"); return; }
                    winL = lt[0]; efiL = lt[1]; dataL = p.DataPart ? lt[2] : ' ';
                }
                string W = winL + ":";

                // 1. разметка / форматирование
                SetStatus(p.Keep ? "Форматирование раздела " + W + " (диск " + p.Disk.Number + ")..."
                                 : "Разметка диска " + p.Disk.Number + " (" + (p.Gpt ? "GPT" : "MBR") + ")...");
                SetProgress(2);
                StringBuilder dp = new StringBuilder();
                dp.AppendLine("rescan");
                if (p.Keep)
                {
                    dp.AppendLine("select disk " + p.Disk.Number);
                    if (p.Gpt)
                    {
                        dp.AppendLine("select partition " + p.EspPart);
                        dp.AppendLine("assign letter=" + efiL);
                    }
                    dp.AppendLine("select partition " + p.WinPart);
                    dp.AppendLine("format quick fs=ntfs label=\"Windows\" override");
                    if (!p.Gpt) dp.AppendLine("active");
                }
                else
                {
                    dp.AppendLine("select disk " + p.Disk.Number);
                    dp.AppendLine("attributes disk clear readonly noerr");
                    dp.AppendLine("clean");
                    if (p.Gpt)
                    {
                        dp.AppendLine("convert gpt");
                        dp.AppendLine("create partition efi size=" + EfiMB);
                        dp.AppendLine("format quick fs=fat32 label=\"EFI\"");
                        dp.AppendLine("assign letter=" + efiL);
                        dp.AppendLine("create partition msr size=" + MsrMB);
                    }
                    else dp.AppendLine("convert mbr");
                    dp.AppendLine(p.WinSizeMB == 0 ? "create partition primary" : "create partition primary size=" + p.WinSizeMB);
                    dp.AppendLine("format quick fs=ntfs label=\"Windows\"");
                    dp.AppendLine("assign letter=" + winL);
                    if (!p.Gpt) dp.AppendLine("active");
                    if (p.DataPart)
                    {
                        dp.AppendLine("create partition primary");
                        dp.AppendLine("format quick fs=ntfs label=\"Data\"");
                        dp.AppendLine("assign letter=" + dataL);
                    }
                }
                dp.AppendLine("exit");
                Log("--- diskpart script ---\n" + dp.ToString() + "-----------------------");

                string diskpart = Util.FindTool("diskpart.exe") ?? "diskpart.exe";
                string script = Path.Combine(Path.GetTempPath(), "wininstaller_diskpart.txt");
                File.WriteAllText(script, dp.ToString(), Encoding.ASCII);
                int rc = RunProc(diskpart, "/s \"" + script + "\"", null, null, false);
                if (rc != 0) { Fail("diskpart завершился с кодом " + rc); return; }
                if (!p.DryRun)
                {
                    Thread.Sleep(1500);
                    if (!Directory.Exists(W + "\\")) { Fail("После разметки не появился раздел " + W); return; }
                    if (p.Gpt && !Directory.Exists(efiL + ":\\")) { Fail("После разметки не появился EFI-раздел " + efiL + ":"); return; }
                }

                // 2. применение образа
                string scratch = W + "\\_dism_tmp";
                if (!p.DryRun) Directory.CreateDirectory(scratch);
                SetStatus("Развёртывание образа (индекс " + p.ImageIndex + ") на " + W + " - это самый долгий этап...");
                SetProgress(5);
                rc = RunProc(dism, "/Apply-Image /ImageFile:\"" + p.ImageFile + "\" /Index:" + p.ImageIndex + " /ApplyDir:" + W + "\\ /ScratchDir:\"" + scratch + "\"",
                    delegate(double v)
                    {
                        SetProgress(5 + (int)(v * 0.85));
                        UI(delegate { lblStatus.Text = "Развёртывание образа: " + v.ToString("0.0") + "%"; });
                    }, null, false);
                if (rc != 0) { Fail("dism /Apply-Image завершился с кодом " + rc); return; }
                if (!p.DryRun && !File.Exists(W + "\\Windows\\System32\\config\\SYSTEM")) { Fail("Образ применён, но Windows\\System32\\config\\SYSTEM не найден"); return; }

                // 3. драйверы
                if (p.DriversDir != null)
                {
                    SetStatus("Добавление драйверов из " + p.DriversDir + "...");
                    SetProgress(91);
                    rc = RunProc(dism, "/Image:" + W + "\\ /Add-Driver /Driver:\"" + p.DriversDir + "\" /Recurse /ScratchDir:\"" + scratch + "\"", null, null, false);
                    if (rc != 0) Log("ПРЕДУПРЕЖДЕНИЕ: добавление драйверов вернуло код " + rc + " (установка продолжается)");
                }

                // 3b. XML ответов
                if (p.Xml != null)
                {
                    string xdest = W + "\\Windows\\Panther\\Unattend\\Unattend.xml";
                    SetStatus("Копирование XML в " + xdest);
                    if (!p.DryRun)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(xdest));
                        File.Copy(p.Xml, xdest, true);
                    }
                }

                // 4. загрузчик
                if (p.Keep && p.Gpt && !p.DryRun)
                {
                    string oldBcd = efiL + ":\\EFI\\Microsoft\\Boot\\BCD";
                    try
                    {
                        if (File.Exists(oldBcd)) { File.SetAttributes(oldBcd, FileAttributes.Normal); File.Delete(oldBcd); Log("Старый BCD удалён, будет создан заново."); }
                    }
                    catch (Exception ex) { Log("Не удалось удалить старый BCD: " + ex.Message); }
                }
                SetStatus("Установка загрузчика (" + (p.Gpt ? "UEFI" : "BIOS") + ")...");
                SetProgress(95);
                string bargs = p.Gpt
                    ? W + "\\Windows /s " + efiL + ": /f UEFI"
                    : W + "\\Windows /s " + W + " /f BIOS";
                rc = RunProc(bcdboot, bargs, null, null, false);
                if (rc != 0)
                {
                    string alt = W + "\\Windows\\System32\\bcdboot.exe";
                    if (!p.DryRun && File.Exists(alt))
                    {
                        Log("bcdboot из WinPE не справился, пробую bcdboot из установленной Windows");
                        rc = RunProc(alt, bargs, null, null, false);
                    }
                    if (rc != 0) { Fail("bcdboot завершился с кодом " + rc); return; }
                }
                if (!p.Gpt)
                {
                    string bootsect = Util.FindTool("bootsect.exe");
                    if (bootsect != null) RunProc(bootsect, "/nt60 " + W + " /mbr /force", null, null, false);
                }
                if (!p.DryRun)
                {
                    string chk = p.Gpt ? efiL + ":\\EFI\\Microsoft\\Boot\\bootmgfw.efi" : W + "\\bootmgr";
                    if (!File.Exists(chk)) { Fail("Загрузчик не найден после bcdboot: " + chk); return; }
                }

                // 5. уборка
                try { if (!p.DryRun) Directory.Delete(scratch, true); } catch { }
                if (p.Keep && p.Gpt)
                {
                    // убрать временную букву с существующего EFI-раздела
                    File.WriteAllText(script, "select disk " + p.Disk.Number + "\r\nselect partition " + p.EspPart + "\r\nremove letter=" + efiL + "\r\nexit\r\n", Encoding.ASCII);
                    RunProc(diskpart, "/s \"" + script + "\"", null, null, false);
                }
                SetProgress(100);
                Beep(true);
                SetStatus("ГОТОВО. Windows установлена на диск " + p.Disk.Number + " (" + W + ").");
                if (p.Reboot && !p.DryRun)
                {
                    bool go = (bool)Invoke((Func<bool>)delegate { return CountdownDialog(15); });
                    if (go)
                    {
                        UI(delegate { installing = false; });
                        iso.Dismount();
                        string wpeutil = Util.FindTool("wpeutil.exe");
                        if (wpeutil != null) RunProc(wpeutil, "reboot", null, null, true);
                        else RunProc("shutdown.exe", "/r /t 0", null, null, true);
                    }
                    else
                    {
                        Log("Перезагрузка отменена пользователем.");
                        UI(delegate { FinishUi(); });
                    }
                }
                else
                {
                    UI(delegate
                    {
                        MessageBox.Show(this, p.DryRun ? "DRY-RUN завершён: команды смотрите в логе." : "Установка завершена.", "Готово");
                        FinishUi();
                    });
                }
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }
    }
}
