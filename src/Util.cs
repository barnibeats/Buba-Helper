// Вспомогательные функции: размеры, SHA-256, поиск утилит.
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
    static class Util
    {
        /// <summary>Версия программы из AssemblyInfo.cs, например «1.2.0».</summary>
        public static string Version()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        }

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

        /// <summary>
        /// bootsect.exe: сначала из папки tools / WinPE / PATH (он подходит к этой версии WinPE), иначе вшитый в программу
        /// (x64, из Windows 10 19041) извлекается во временную папку. null - ни того ни другого (например, 32-разрядная WinPE).
        /// </summary>
        public static string BootsectPath()
        {
            string p = FindTool("bootsect.exe");
            if (p != null) return p;
            if (!Environment.Is64BitOperatingSystem) return null;
            try
            {
                using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("WinInstaller.bootsect.exe"))
                {
                    if (s == null) return null;
                    byte[] data = new byte[s.Length];
                    int off = 0, n;
                    while (off < data.Length && (n = s.Read(data, off, data.Length - off)) > 0) off += n;
                    string dir = Path.Combine(Path.GetTempPath(), "Buba-Helper");
                    Directory.CreateDirectory(dir);
                    string dst = Path.Combine(dir, "bootsect.exe");
                    bool same = false;
                    try { same = File.Exists(dst) && Sha256Bytes(File.ReadAllBytes(dst)) == Sha256Bytes(data); } catch { }
                    if (!same) File.WriteAllBytes(dst, data);
                    return dst;
                }
            }
            catch { return null; }
        }

        static string Sha256Bytes(byte[] b)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte x in sha.ComputeHash(b)) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
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
}
