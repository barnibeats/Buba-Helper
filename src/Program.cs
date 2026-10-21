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
        public static string StartMode;   // с какого режима открыть окно
        public static int StartPage;      // и какую страницу выбрать (для режима «xml»)

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0].ToLowerInvariant() == "--gen") { Environment.Exit(GenCli.Run(args)); return; }   // генератор XML без окна
            if (args.Length > 1 && args[0].ToLowerInvariant() == "--selftest-ui")   // самопроверка редактора файла ответов
            {
                Application.EnableVisualStyles();
                int ops = args.Length > 2 ? int.Parse(args[2]) : 1500, seed = args.Length > 3 ? int.Parse(args[3]) : 1;
                string rep = new GenEditor().SelfTest(ops, seed);
                System.IO.File.WriteAllText(args[1], rep, System.Text.Encoding.UTF8);
                Environment.Exit(rep.StartsWith("Действий: ") && rep.Contains("ошибок: 0\n") ? 0 : 1);
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
                else if (a == "--yes") yes = true;
                else if (a == "--mode" && i + 1 < args.Length) StartMode = args[++i].ToLowerInvariant();   // install | capture | xml
                else if (a == "--page" && i + 1 < args.Length) int.TryParse(args[++i], out StartPage);
                else if (a == "--export-icon" && i + 1 < args.Length) { Icons.WriteIco(args[++i]); return; }   // для сборки: значок exe
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new MainForm(profile, auto, yes)); }
            catch (Exception ex) { MessageBox.Show(ex.ToString(), "WinInstaller - критическая ошибка"); }
        }
    }
}
