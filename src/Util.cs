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
}
