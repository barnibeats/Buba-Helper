// Win32: диски, тома, прошивка (UEFI/BIOS).
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
    // ---------------------------------------------------------------- Win32: диски, тома, ISO

    /// <summary>Раздел диска из IOCTL_DISK_GET_DRIVE_LAYOUT_EX.</summary>
    class PartInfo
    {
        public int Number;
        public long Offset, Length;
        public bool Gpt, Active;          // Active - только MBR
        public byte MbrType;
        public Guid TypeGuid;
        public string Name = "";
        public List<char> Letters = new List<char>();

        public bool IsEsp { get { return Gpt && TypeGuid == Native.GuidEsp; } }
        public bool IsMsr { get { return Gpt && TypeGuid == Native.GuidMsr; } }
    }

    /// <summary>Свободный (неразмеченный) участок диска, границы в байтах: начало округлено вверх, конец вниз до 1 МБ.</summary>
    class FreeSpan
    {
        public long Start, End;
        public long Length { get { return End - Start; } }
    }

    static class Native
    {
        public static readonly Guid GuidEsp = new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
        public static readonly Guid GuidMsr = new Guid("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");
        public static readonly Guid GuidRecovery = new Guid("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC");
        public static readonly Guid GuidBasic = new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");

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

        /// <summary>Разделы диска (с номерами, как их видит diskpart) и буквы их томов. Пустой список - диск не прочитан или RAW.</summary>
        public static List<PartInfo> Partitions(int disk)
        {
            List<PartInfo> res = new List<PartInfo>();
            using (SafeFileHandle h = Open(@"\\.\PhysicalDrive" + disk, GENERIC_READ))
            {
                if (h.IsInvalid) return res;
                byte[] lay = new byte[131072];
                int ret;
                if (!DeviceIoControl(h, 0x00070050, null, 0, lay, lay.Length, out ret, IntPtr.Zero)) return res;
                int style = BitConverter.ToInt32(lay, 0), cnt = BitConverter.ToInt32(lay, 4);
                if (style > 1) return res;
                for (int i = 0; i < cnt; i++)
                {
                    int o = 48 + i * 144;
                    if (o + 144 > ret) break;
                    PartInfo p = new PartInfo();
                    p.Gpt = style == 1;
                    p.Offset = BitConverter.ToInt64(lay, o + 8);
                    p.Length = BitConverter.ToInt64(lay, o + 16);
                    p.Number = BitConverter.ToInt32(lay, o + 24);
                    if (p.Number <= 0 || p.Length <= 0) continue;      // контейнер расширенного раздела и пустые записи
                    if (p.Gpt)
                    {
                        byte[] g = new byte[16];
                        Array.Copy(lay, o + 32, g, 0, 16);
                        p.TypeGuid = new Guid(g);
                        p.Name = Encoding.Unicode.GetString(lay, o + 72, 72).TrimEnd('\0');
                    }
                    else { p.MbrType = lay[o + 32]; p.Active = lay[o + 33] != 0; }
                    res.Add(p);
                }
            }
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                char c = char.ToUpper(d.Name[0]);
                if (c == 'A' || c == 'B') continue;
                if (!DisksOfVolume(c).Contains(disk)) continue;
                int n = PartitionNumberOfVolume(c);
                foreach (PartInfo p in res) if (p.Number == n) p.Letters.Add(c);
            }
            return res;
        }

        /// <summary>Границы области GPT-диска, где можно создавать разделы (байты). false - диск не GPT или не прочитан.</summary>
        public static bool GptUsable(int disk, out long start, out long end)
        {
            start = end = 0;
            using (SafeFileHandle h = Open(@"\\.\PhysicalDrive" + disk, GENERIC_READ))
            {
                if (h.IsInvalid) return false;
                byte[] lay = new byte[131072];
                int ret;
                if (!DeviceIoControl(h, 0x00070050, null, 0, lay, lay.Length, out ret, IntPtr.Zero)) return false;
                if (BitConverter.ToInt32(lay, 0) != 1) return false;
                start = BitConverter.ToInt64(lay, 24);                 // StartingUsableOffset
                end = start + BitConverter.ToInt64(lay, 32);           // + UsableLength
                return end > start;
            }
        }

        /// <summary>Свободные участки между разделами (≥ 1 МБ). ignore - раздел, который считается уже удалённым.</summary>
        public static List<FreeSpan> FreeSpans(List<PartInfo> parts, PartInfo ignore, long usableStart, long usableEnd)
        {
            List<PartInfo> sorted = new List<PartInfo>();
            foreach (PartInfo p in parts) if (ignore == null || p.Number != ignore.Number) sorted.Add(p);
            sorted.Sort(delegate(PartInfo a, PartInfo b) { return a.Offset.CompareTo(b.Offset); });
            List<FreeSpan> res = new List<FreeSpan>();
            long cur = usableStart;
            foreach (PartInfo p in sorted)
            {
                AddSpan(res, cur, p.Offset);
                cur = Math.Max(cur, p.Offset + p.Length);
            }
            AddSpan(res, cur, usableEnd);
            return res;
        }

        static void AddSpan(List<FreeSpan> res, long from, long to)
        {
            const long MB = 1048576;
            long s = (from + MB - 1) / MB * MB, e = to / MB * MB;
            if (e - s >= MB) { FreeSpan f = new FreeSpan(); f.Start = s; f.End = e; res.Add(f); }
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
}
