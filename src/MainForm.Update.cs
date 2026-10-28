// Главная форма: проверка новой версии в релизах GitHub при запуске и обновление exe на месте.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace WinInstaller
{
    class UpdateInfo
    {
        public Version Version;
        public string Tag, Notes;
        public string ExeApi, ShaApi;       // адреса ассетов в API (работают и для приватного репозитория)
    }

    static class Updater
    {
        public const string Repo = "barnibeats/Buba-Helper";

        /// <summary>Токен для приватного репозитория: переменная BUBA_GH_TOKEN или файл update-token.txt рядом с exe (нужен только read-only доступ к Contents). Публичному репозиторию токен не нужен.</summary>
        static string Token()
        {
            string t = Environment.GetEnvironmentVariable("BUBA_GH_TOKEN");
            if (!string.IsNullOrEmpty(t)) return t.Trim();
            try
            {
                string f = Path.Combine(Util.AppDir(), "update-token.txt");
                if (File.Exists(f)) return File.ReadAllText(f).Trim();
            }
            catch { }
            return null;
        }

        static HttpClient NewClient(string accept)
        {
            HttpClient c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Buba-Helper/" + Util.Version());
            c.DefaultRequestHeaders.Accept.ParseAdd(accept);
            string tok = Token();
            if (tok != null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tok);
            return c;
        }

        static string Unescape(string s)
        {
            return Regex.Replace(s, @"\\(u[0-9a-fA-F]{4}|.)", delegate(Match m)
            {
                string e = m.Groups[1].Value;
                if (e[0] == 'u') return ((char)Convert.ToInt32(e.Substring(1), 16)).ToString();
                switch (e[0]) { case 'n': return "\n"; case 'r': return ""; case 't': return "\t"; default: return e; }
            });
        }

        /// <summary>Последний релиз; null - не удалось узнать (нет сети, нет доступа, нет релизов).</summary>
        public static UpdateInfo CheckLatest(out string error)
        {
            error = null;
            try
            {
                string json;
                using (HttpClient c = NewClient("application/vnd.github+json"))
                {
                    HttpResponseMessage r = c.GetAsync("https://api.github.com/repos/" + Repo + "/releases/latest").Result;
                    if (!r.IsSuccessStatusCode)
                    {
                        error = "GitHub ответил " + (int)r.StatusCode + (r.StatusCode == System.Net.HttpStatusCode.NotFound && Token() == null ? " (репозиторий приватный: положите рядом с exe файл update-token.txt с токеном)" : "");
                        return null;
                    }
                    json = r.Content.ReadAsStringAsync().Result;
                }
                UpdateInfo u = new UpdateInfo();
                Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                if (!tag.Success) { error = "в ответе нет tag_name"; return null; }
                u.Tag = tag.Groups[1].Value;
                if (!Version.TryParse(u.Tag.TrimStart('v', 'V'), out u.Version)) { error = "непонятная версия: " + u.Tag; return null; }
                Match body = Regex.Match(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                u.Notes = body.Success ? Unescape(body.Groups[1].Value).Trim() : "";
                // ассеты: "url" API-адрес, затем "name"; поля идут в порядке url ... name ... в одном объекте
                foreach (Match a in Regex.Matches(json, "\"url\"\\s*:\\s*\"(https://api\\.github\\.com/repos/[^\"]+/releases/assets/\\d+)\"[^{}]*?\"name\"\\s*:\\s*\"([^\"]+)\""))
                {
                    string name = a.Groups[2].Value;
                    if (name.Equals("Buba-Helper.exe", StringComparison.OrdinalIgnoreCase)) u.ExeApi = a.Groups[1].Value;
                    else if (name.Equals("Buba-Helper.exe.sha256", StringComparison.OrdinalIgnoreCase)) u.ShaApi = a.Groups[1].Value;
                }
                if (u.ExeApi == null) { error = "в релизе " + u.Tag + " нет Buba-Helper.exe"; return null; }
                return u;
            }
            catch (Exception ex) { error = (ex.InnerException ?? ex).Message; return null; }
        }

        public static bool IsNewer(UpdateInfo u)
        {
            Version cur;
            if (!Version.TryParse(Util.Version(), out cur)) return false;
            return u.Version > cur;
        }

        /// <summary>Скачивает ассет по API-адресу в файл; onPct получает проценты.</summary>
        public static void Download(string api, string file, Action<int> onPct)
        {
            using (HttpClient c = NewClient("application/octet-stream"))
            {
                c.Timeout = TimeSpan.FromMinutes(15);
                using (HttpResponseMessage r = c.GetAsync(api, HttpCompletionOption.ResponseHeadersRead).Result)
                {
                    r.EnsureSuccessStatusCode();
                    long total = r.Content.Headers.ContentLength ?? 0;
                    using (Stream s = r.Content.ReadAsStreamAsync().Result)
                    using (FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buf = new byte[256 * 1024];
                        long done = 0; int n, last = -1;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n); done += n;
                            if (total > 0 && onPct != null) { int p = (int)(done * 100 / total); if (p != last) { last = p; onPct(p); } }
                        }
                    }
                }
            }
        }

        public static string DownloadText(string api)
        {
            using (HttpClient c = NewClient("application/octet-stream")) return c.GetStringAsync(api).Result;
        }
    }

    partial class MainForm
    {
        bool updateBusy;

        /// <summary>Проверка в фоне: тихо, если сети нет или версия последняя.</summary>
        void StartUpdateCheck()
        {
            Thread t = new Thread(delegate()
            {
                string err;
                UpdateInfo u = Updater.CheckLatest(out err);
                if (u == null) { Log("Проверка обновлений: " + err); return; }
                if (!Updater.IsNewer(u)) { Log("Установлена последняя версия (" + Util.Version() + ")."); return; }
                Log("Доступна новая версия " + u.Tag + " (сейчас " + Util.Version() + ").");
                UI(delegate { OfferUpdate(u); });
            });
            t.IsBackground = true; t.Start();
        }

        void OfferUpdate(UpdateInfo u)
        {
            if (installing || busy || updateBusy) return;
            string notes = u.Notes.Length > 700 ? u.Notes.Substring(0, 700) + "…" : u.Notes;
            string msg = "Доступна новая версия Buba-Helper: " + u.Tag + " (у вас " + Util.Version() + ").\n\n"
                + (notes.Length > 0 ? "Что нового:\n" + notes + "\n\n" : "") + "Скачать и обновить сейчас? Программа перезапустится.";
            if (MessageBox.Show(this, msg, "Обновление", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            updateBusy = true;
            SetWorking(true);
            Thread t = new Thread(delegate() { RunUpdate(u); });
            t.IsBackground = true; t.Start();
        }

        void UpdateFail(string msg)
        {
            updateBusy = false;
            Log("ОШИБКА обновления: " + msg);
            UI(delegate
            {
                lblStatus.Text = "Обновление не удалось: " + msg;
                MessageBox.Show(this, "Обновление не удалось:\n" + msg + "\n\nТекущая версия продолжит работать.", "Обновление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetWorking(false);
            });
        }

        void RunUpdate(UpdateInfo u)
        {
            try
            {
                string exe = Environment.ProcessPath ?? Application.ExecutablePath;
                string dir = Path.GetDirectoryName(exe);
                // рядом с exe писать можно? (в WinPE exe часто лежит на флешке; на CD/ISO - нельзя, тогда запускаем новую версию из временной папки)
                bool inPlace = true;
                string neu = exe + ".new";
                try { File.WriteAllBytes(neu, new byte[0]); File.Delete(neu); }
                catch { inPlace = false; neu = Path.Combine(Path.GetTempPath(), "Buba-Helper-" + u.Tag.TrimStart('v') + ".exe"); }

                SetStatus("Скачивание " + u.Tag + "...");
                Updater.Download(u.ExeApi, neu, delegate(int p) { SetProgress(p); });
                SetProgress(100);

                if (u.ShaApi != null)
                {
                    string want = Regex.Match(Updater.DownloadText(u.ShaApi), "[0-9a-fA-F]{64}").Value.ToLowerInvariant();
                    string got = Util.Sha256File(neu, null);
                    if (want.Length == 64 && want != got) { try { File.Delete(neu); } catch { } UpdateFail("контрольная сумма не совпала (файл повреждён при загрузке)."); return; }
                    Log("SHA-256 совпала.");
                }
                else Log("Файла .sha256 в релизе нет - проверка суммы пропущена.");

                string bat = Path.Combine(Path.GetTempPath(), "buba-update.cmd");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("set n=0");
                if (inPlace)
                {
                    sb.AppendLine(":w");
                    sb.AppendLine("ping -n 2 127.0.0.1 >nul");
                    sb.AppendLine("move /y \"" + neu + "\" \"" + exe + "\" >nul 2>nul");
                    sb.AppendLine("if exist \"" + neu + "\" (set /a n+=1 & if %n% lss 30 goto w)");
                    sb.AppendLine("if exist \"" + neu + "\" (start \"\" \"" + neu + "\") else (start \"\" \"" + exe + "\")");
                }
                else sb.AppendLine("start \"\" \"" + neu + "\"");
                sb.AppendLine("del \"%~f0\"");
                File.WriteAllText(bat, sb.ToString(), Encoding.Default);
                Log(inPlace ? "Замена " + exe + " после закрытия программы..." : "Папка с exe только для чтения: новая версия запустится из " + neu);

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.WorkingDirectory = dir;
                Process.Start(psi);
                UI(delegate { updateBusy = false; SetWorking(false); Close(); });
            }
            catch (Exception ex) { UpdateFail((ex.InnerException ?? ex).Message); }
        }
    }
}
