// Главная форма: план разметки и размер раздела.
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
        // ------------------------------------------------------------ план разметки

        int SelectedSizeMB()
        {
            return chkAll.Checked ? 0 : (int)nudSize.Value;     // 0 = весь диск
        }

        long OverheadMB(bool gpt) { return gpt ? EfiMB + MsrMB + 4 : 2; }

        class PartItem
        {
            public char Letter;
            public string Text;
            public override string ToString() { return Text; }
        }

        void ApplyModeLayout()
        {
            bool keep = rbKeep.Checked;
            lblSize.Visible = keep ? false : true; nudSize.Visible = !keep; lblSizeHint.Visible = !keep;
            chkAll.Visible = !keep; chkData.Visible = !keep;
            lblPart.Visible = keep; cmbPart.Visible = keep;
            rbGpt.Enabled = !keep; rbMbr.Enabled = !keep;
            grpDisk.Text = keep ? "2. Диск (форматируется только выбранный раздел Windows, остальное не трогается)"
                                : "2. Диск для установки (ВСЕ ДАННЫЕ НА НЁМ БУДУТ УДАЛЕНЫ)";
            partsDisk = -1;
            if (IsHandleCreated) RefreshDisks(); else UpdatePlanText();
        }

        void FillParts()
        {
            if (cmbPart == null || !rbKeep.Checked) return;
            DiskInfo d = SelectedDisk();
            int n = d == null ? -2 : d.Number;
            if (n == partsDisk) return;
            partsDisk = n;
            cmbPart.Items.Clear();
            if (d == null) return;
            int pick = -1;
            foreach (char l in d.Letters)
            {
                if (sourceLetters.Contains(l)) continue;
                string t = l + ":";
                try
                {
                    DriveInfo di = new DriveInfo(l.ToString());
                    if (di.IsReady) t += " " + di.VolumeLabel + " (" + di.DriveFormat + ", " + Util.FmtSize(di.TotalSize) + ")";
                }
                catch { }
                if (Directory.Exists(l + ":\\Windows\\System32"))
                {
                    t += "   [установлена Windows]";
                    if (pick < 0) pick = cmbPart.Items.Count;
                }
                PartItem pi = new PartItem(); pi.Letter = l; pi.Text = t;
                cmbPart.Items.Add(pi);
            }
            if (pick >= 0) cmbPart.SelectedIndex = pick;
            if (d.Style == "GPT") rbGpt.Checked = true; else if (d.Style == "MBR") rbMbr.Checked = true;
        }

        void UpdatePlanText()
        {
            UpdatePlanTextCore();
            UpdateNav();
        }

        void UpdatePlanTextCore()
        {
            if (lblPlan == null || chkAll == null || cmbPart == null) return;
            DiskInfo d = SelectedDisk();
            if (rbKeep.Checked)
            {
                FillParts();
                PartItem pi = cmbPart.SelectedItem as PartItem;
                string boot = d == null ? "" : d.Style == "GPT" ? "в существующий EFI-раздел (старый BCD заменяется)"
                    : d.Style == "MBR" ? "в раздел Windows (он станет активным)" : "стиль диска не определён (нужны права администратора)";
                lblPlan.Text = pi == null
                    ? "Выберите диск и раздел с Windows, который нужно переустановить."
                    : "Форматируется ТОЛЬКО раздел " + pi.Letter + ":. Остальные разделы диска не трогаются.\nЗагрузчик: " + boot;
                return;
            }
            bool gpt = rbGpt.Checked;
            int mb = SelectedSizeMB();
            chkData.Enabled = mb != 0;
            if (mb == 0) chkData.Checked = false;
            string head = gpt ? "GPT:  EFI " + EfiMB + " МБ  +  MSR " + MsrMB + " МБ  +  Windows " : "MBR:  Windows (активный, с загрузчиком) ";
            string win = mb == 0 ? "весь остаток" : mb + " МБ";
            string s = head + win;
            if (d != null && mb != 0)
            {
                long free = d.Size / 1048576 - OverheadMB(gpt) - mb;
                if (chkData.Checked) s += "  +  Data " + Util.FmtSize(Math.Max(0, free) * 1048576);
                else if (free > 0) s += "   (не размечено: " + Util.FmtSize(free * 1048576) + ")";
            }
            if (firmware == "UEFI" && !gpt) s += "\nВнимание: ПК загружен в UEFI, а выбран MBR - нужен режим Legacy/CSM.";
            if (firmware == "BIOS" && gpt) s += "\nВнимание: ПК загружен в BIOS, а выбран GPT - Windows с него не загрузится.";
            lblPlan.Text = s;
        }
    }
}
