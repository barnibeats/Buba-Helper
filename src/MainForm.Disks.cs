// Главная форма: список дисков.
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
        // ------------------------------------------------------------ диски

        /// <summary>
        /// Диски, на которые ставить нельзя: с них запущена программа (кроме RAM-диска X: у WinPE), загружена WinPE или лежит установочный
        /// носитель (sources\boot.wim), а в обычной Windows - диск текущей системы.
        /// </summary>
        static List<int> DetectBootDisks()
        {
            List<int> res = new List<int>();
            string sysDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "X:").ToUpper();
            string app = Util.AppDir();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                char c = char.ToUpper(d.Name[0]);
                if (c == 'A' || c == 'B') continue;
                try
                {
                    if (!d.IsReady) continue;
                    bool boot = File.Exists(c + ":\\sources\\boot.wim") || File.Exists(c + ":\\boot\\boot.wim");
                    if (c != 'X' && app.Length > 1 && char.ToUpper(app[0]) == c) boot = true;            // программа запущена с этого диска
                    if (sysDrive.Length > 0 && sysDrive[0] == c && c != 'X') boot = true;                  // текущая Windows
                    if (!boot) continue;
                    foreach (int n in Native.DisksOfVolume(c)) if (!res.Contains(n)) res.Add(n);
                }
                catch { }
            }
            return res;
        }

        void RefreshDisks()
        {
            bootDisks = DetectBootDisks();
            int sel = -1;
            if (lvDisks.SelectedItems.Count > 0) sel = ((DiskInfo)lvDisks.SelectedItems[0].Tag).Number;
            lvDisks.Items.Clear();
            List<DiskInfo> disks;
            try { disks = Native.EnumDisks(); }
            catch (Exception ex) { Log("Ошибка чтения дисков: " + ex.Message); return; }
            foreach (DiskInfo d in disks)
            {
                bool isSrc = sourceDisks.Contains(d.Number), isBoot = bootDisks.Contains(d.Number);
                d.Blocked = !rbKeep.Checked && (isSrc || isBoot);
                string vols = d.Volumes.Count > 0 ? string.Join(";  ", d.Volumes.ToArray()) : "(нет томов с буквами)";
                if (d.Blocked) vols = (isSrc ? "<< ИСТОЧНИК ОБРАЗА >>  " : "<< ЗАГРУЗОЧНЫЙ НОСИТЕЛЬ / ПРОГРАММА >>  ") + vols;
                else if (rbKeep.Checked && isSrc) vols = "(на диске лежит образ - его раздел защищён)  " + vols;
                ListViewItem it = new ListViewItem(new string[] { d.Number.ToString(), d.Model, Util.FmtSize(d.Size), d.Bus, d.Style, vols });
                it.Tag = d;
                if (d.Blocked) it.ForeColor = Theme.Muted;
                else if (d.Bus == "USB") it.ForeColor = Theme.Warn;
                lvDisks.Items.Add(it);
                if (d.Number == sel && !d.Blocked) it.Selected = true;
            }
            lblDiskHint.Text = disks.Count == 0
                ? "Диски не найдены. Нужны права администратора (в WinPE они есть)."
                : "Оранжевые - USB-диски. Серые - диск с образом и загрузочный носитель WinPE (их выбрать нельзя).";
            UpdatePlanText();
        }

        DiskInfo SelectedDisk()
        {
            if (lvDisks.SelectedItems.Count == 0) return null;
            return (DiskInfo)lvDisks.SelectedItems[0].Tag;
        }
    }
}
