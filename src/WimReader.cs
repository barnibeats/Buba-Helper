// Чтение списка редакций из WIM/ESD.
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
            psi.StandardOutputEncoding = OemEnc.Get();
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
}
