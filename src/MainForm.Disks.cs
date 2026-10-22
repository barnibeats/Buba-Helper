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

        void RefreshDisks()
        {
            int sel = -1;
            if (lvDisks.SelectedItems.Count > 0) sel = ((DiskInfo)lvDisks.SelectedItems[0].Tag).Number;
            lvDisks.Items.Clear();
            List<DiskInfo> disks;
            try { disks = Native.EnumDisks(); }
            catch (Exception ex) { Log("Ошибка чтения дисков: " + ex.Message); return; }
            foreach (DiskInfo d in disks)
            {
                d.Blocked = !rbKeep.Checked && sourceDisks.Contains(d.Number);
                string vols = d.Volumes.Count > 0 ? string.Join(";  ", d.Volumes.ToArray()) : "(нет томов с буквами)";
                if (d.Blocked) vols = "<< ИСТОЧНИК ОБРАЗА >>  " + vols;
                else if (rbKeep.Checked && sourceDisks.Contains(d.Number)) vols = "(на диске лежит образ - его раздел защищён)  " + vols;
                ListViewItem it = new ListViewItem(new string[] { d.Number.ToString(), d.Model, Util.FmtSize(d.Size), d.Bus, d.Style, vols });
                it.Tag = d;
                if (d.Blocked) it.ForeColor = Theme.Muted;
                else if (d.Bus == "USB") it.ForeColor = Theme.Warn;
                lvDisks.Items.Add(it);
                if (d.Number == sel && !d.Blocked) it.Selected = true;
            }
            lblDiskHint.Text = disks.Count == 0
                ? "Диски не найдены. Нужны права администратора (в WinPE они есть)."
                : "Оранжевые - USB-диски. Серый - диск с образом (его выбрать нельзя).";
            UpdatePlanText();
        }

        DiskInfo SelectedDisk()
        {
            if (lvDisks.SelectedItems.Count == 0) return null;
            return (DiskInfo)lvDisks.SelectedItems[0].Tag;
        }
    }
}
