// Поиск и загрузка драйверов по ID устройства в каталоге Microsoft Update (catalog.update.microsoft.com): подписанные драйверы Intel, Realtek и др.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace WinInstaller
{
    class CatalogHit
    {
        public string Guid, Title, Products, Classification, Version;
        public DateTime Updated;
        public override string ToString() { return Title + "  (" + Version + ", " + Updated.ToString("yyyy-MM-dd") + ")"; }
    }

    static class DriverCatalog
    {
        const string Base = "https://www.catalog.update.microsoft.com/";

        static HttpClient NewClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            HttpClient c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Buba-Helper");
            return c;
        }

        static string StripTags(string s)
        {
            s = Regex.Replace(s, "<[^>]+>", " ");
            s = WebUtility.HtmlDecode(s);
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        /// <summary>Идентификатор устройства для поиска: PCI\VEN_8086&amp;DEV_15FA или USB\VID_xxxx&amp;PID_xxxx без SUBSYS/REV.</summary>
        public static string ShortId(string hw)
        {
            Match m = Regex.Match(hw ?? "", @"(PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}|USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4})", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        /// <summary>Поиск драйверов по ID. Для Windows 10/11 берутся только записи с «Windows 10»/«Windows 11» в продуктах, свежие первыми.</summary>
        public static List<CatalogHit> Search(string hwId, int build)
        {
            string id = ShortId(hwId) ?? hwId;
            string q = id.Substring(id.IndexOf('\\') + 1);          // VEN_8086&DEV_15FA - так каталог ищет надёжнее
            string html;
            using (HttpClient c = NewClient())
                html = c.GetStringAsync(Base + "Search.aspx?q=" + Uri.EscapeDataString(q)).Result;
            List<CatalogHit> res = new List<CatalogHit>();
            foreach (Match row in Regex.Matches(html, @"<tr[^>]*id=""([0-9a-fA-F-]{36})_R\d+""[^>]*>(.*?)</tr>", RegexOptions.Singleline))
            {
                MatchCollection cells = Regex.Matches(row.Groups[2].Value, @"<td[^>]*>(.*?)</td>", RegexOptions.Singleline);
                if (cells.Count < 6) continue;
                CatalogHit h = new CatalogHit();
                h.Guid = row.Groups[1].Value;
                h.Title = StripTags(cells[1].Groups[1].Value);
                h.Products = StripTags(cells[2].Groups[1].Value);
                h.Classification = StripTags(cells[3].Groups[1].Value);
                DateTime.TryParse(StripTags(cells[4].Groups[1].Value), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out h.Updated);
                h.Version = StripTags(cells[5].Groups[1].Value);
                bool w10 = h.Products.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0, w11 = h.Products.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) >= 0;
                if (build >= 22000 ? !(w11 || w10) : build >= 10240 ? !w10 : false) continue;
                if (h.Classification.IndexOf("Driver", StringComparison.OrdinalIgnoreCase) < 0 && h.Classification.Length > 0) continue;
                res.Add(h);
            }
            res.Sort(delegate(CatalogHit a, CatalogHit b) { return b.Updated.CompareTo(a.Updated); });
            return res;
        }

        /// <summary>Ссылка на .cab для записи каталога.</summary>
        public static List<string> DownloadUrls(string guid)
        {
            string body = "updateIDs=" + Uri.EscapeDataString("[{\"size\":0,\"languages\":\"\",\"uidInfo\":\"" + guid + "\",\"updateID\":\"" + guid + "\"}]");
            string html;
            using (HttpClient c = NewClient())
            {
                StringContent sc = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
                html = c.PostAsync(Base + "DownloadDialog.aspx", sc).Result.Content.ReadAsStringAsync().Result;
            }
            List<string> urls = new List<string>();
            foreach (Match m in Regex.Matches(html, @"downloadInformation\[\d+\]\.files\[\d+\]\.url\s*=\s*'([^']+)'"))
                if (!urls.Contains(m.Groups[1].Value)) urls.Add(m.Groups[1].Value);
            return urls;
        }

        public static void Download(string url, string file)
        {
            using (HttpClient c = NewClient())
            {
                c.Timeout = TimeSpan.FromMinutes(10);
                using (Stream s = c.GetStreamAsync(url).Result)
                using (FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write))
                    s.CopyTo(fs);
            }
        }

        /// <summary>Распаковывает .cab штатным expand.exe (есть и в WinPE).</summary>
        public static bool Expand(string cab, string dir)
        {
            Directory.CreateDirectory(dir);
            string exe = Util.FindTool("expand.exe") ?? "expand.exe";
            ProcessStartInfo psi = new ProcessStartInfo(exe, "-F:* \"" + cab + "\" \"" + dir + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
            using (Process p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); return p.ExitCode == 0; }
        }
    }
}
