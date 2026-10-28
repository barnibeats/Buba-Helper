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
        public static bool NoUpdate;      // --no-update: не проверять новую версию при запуске
        public static string StartMode;   // с какого режима открыть окно
        public static int StartPage;      // и какую страницу выбрать (для режима «xml»)

        [STAThread]
        static void Main(string[] args)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            if (args.Length > 0 && args[0].ToLowerInvariant() == "--gen") { Environment.Exit(GenCli.Run(args)); return; }   // генератор XML без окна
            if (args.Length > 0 && args[0].ToLowerInvariant() == "--integrate") { Environment.Exit(IntegrateCli.Run(args)); return; }   // файлы ответов в ISO/WIM без окна
            if (args.Length > 1 && args[0].ToLowerInvariant() == "--selftest-ui")   // самопроверка редактора файла ответов
            {
                Application.EnableVisualStyles();
                int ops = args.Length > 2 ? int.Parse(args[2]) : 1500, seed = args.Length > 3 ? int.Parse(args[3]) : 1;
                string rep = new GenEditor().SelfTest(ops, seed);
                System.IO.File.WriteAllText(args[1], rep, System.Text.Encoding.UTF8);
                Environment.Exit(rep.StartsWith("Действий: ") && rep.Contains("ошибок: 0\n") ? 0 : 1);
                return;
            }
            if (args.Length > 2 && args[0].ToLowerInvariant() == "--drv-search")   // проверка поиска в каталоге Microsoft: --drv-search <ID> <файл-отчёт> [папка]
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                try
                {
                    var hits = DriverCatalog.Search(args[1], 19045);
                    sb.AppendLine("hits: " + hits.Count);
                    foreach (var h in hits) sb.AppendLine("  " + h.Guid + "  " + h + "  [" + h.Products + "]");
                    if (hits.Count > 0)
                    {
                        var urls = DriverCatalog.DownloadUrls(hits[0].Guid);
                        sb.AppendLine("urls: " + string.Join(" | ", urls.ToArray()));
                        if (urls.Count > 0 && args.Length > 3)
                        {
                            string cab = System.IO.Path.Combine(args[3], "t.cab");
                            System.IO.Directory.CreateDirectory(args[3]);
                            DriverCatalog.Download(urls[0], cab);
                            sb.AppendLine("downloaded: " + new System.IO.FileInfo(cab).Length + " bytes; expand: " + DriverCatalog.Expand(cab, System.IO.Path.Combine(args[3], "x")));
                        }
                    }
                }
                catch (Exception ex) { sb.AppendLine("ERR " + ex); }
                System.IO.File.WriteAllText(args[2], sb.ToString(), System.Text.Encoding.UTF8);
                return;
            }
            if (args.Length > 2 && args[0].ToLowerInvariant() == "--drv-fetch")   // для сборки встроенного набора: --drv-fetch <файл-отчёт> <папка> <ID>...
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 3; i < args.Length; i++)
                {
                    try
                    {
                        string d = System.IO.Path.Combine(args[2], "id" + i);
                        string inf = MainForm.FetchDriver(args[i], 19045, true, d, delegate(string m) { sb.AppendLine(m); });
                        sb.AppendLine(args[i] + " => " + (inf ?? "NOT FOUND"));
                    }
                    catch (Exception ex) { sb.AppendLine(args[i] + " ERR " + (ex.InnerException ?? ex).Message); }
                }
                System.IO.File.WriteAllText(args[1], sb.ToString(), System.Text.Encoding.UTF8);
                return;
            }
            if (args.Length > 1 && args[0].ToLowerInvariant() == "--check-update")   // проверка обновлений без окна: результат в файл
            {
                string e; UpdateInfo ui = Updater.CheckLatest(out e);
                System.IO.File.WriteAllText(args[1], ui == null ? "ERR " + e : ui.Tag + " newer=" + Updater.IsNewer(ui) + " exe=" + ui.ExeApi + " sha=" + ui.ShaApi + "\nnotes=" + ui.Notes, System.Text.Encoding.UTF8);
                return;
            }
            if (args.Length > 2 && args[0].ToLowerInvariant() == "--download-update")   // проверка загрузки и контрольной суммы: --download-update <файл-отчёт> <куда скачать>
            {
                string e, res; UpdateInfo ui = Updater.CheckLatest(out e);
                if (ui == null) res = "ERR " + e;
                else
                {
                    Updater.Download(ui.ExeApi, args[2], null);
                    string want = System.Text.RegularExpressions.Regex.Match(ui.ShaApi == null ? "" : Updater.DownloadText(ui.ShaApi), "[0-9a-fA-F]{64}").Value.ToLowerInvariant();
                    string got = Util.Sha256File(args[2], null);
                    res = ui.Tag + " size=" + new System.IO.FileInfo(args[2]).Length + " sha_ok=" + (want == got) + " (" + got + ")";
                }
                System.IO.File.WriteAllText(args[1], res, System.Text.Encoding.UTF8);
                return;
            }
            string profile = null;
            bool auto = false, yes = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--dry-run" || a == "/dry") DryRun = true;
                else if (a == "--profile" && i + 1 < args.Length) profile = args[++i];
                else if (a == "--auto") auto = true;
                else if (a == "--no-update") NoUpdate = true;
                else if (a == "--yes") yes = true;
                else if (a == "--mode" && i + 1 < args.Length) StartMode = args[++i].ToLowerInvariant();   // install | capture | repair | integrate | xml
                else if (a == "--page" && i + 1 < args.Length) int.TryParse(args[++i], out StartPage);
                else if (a == "--export-icon" && i + 1 < args.Length) { Icons.WriteIco(args[++i]); return; }   // для сборки: значок exe
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Dark.Init();
            try { Application.Run(new MainForm(profile, auto, yes)); }
            catch (Exception ex) { MessageBox.Show(ex.ToString(), "WinInstaller - критическая ошибка"); }
        }
    }
}
