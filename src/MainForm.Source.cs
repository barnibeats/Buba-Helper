// Главная форма: поиск и чтение образа (ISO/WIM/ESD).
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
    partial class MainForm
    {
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
    }
}
