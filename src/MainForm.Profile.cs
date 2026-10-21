// Главная форма: профили и авторежим.
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
        // ------------------------------------------------------------ профили и авторежим

        static string ResolvePath(string p)
        {
            p = (p ?? "").Trim().Trim('"');
            if (p.Length == 0 || Path.IsPathRooted(p)) return p;
            return Path.Combine(Util.AppDir(), p);
        }

        static Dictionary<string, string> ReadIni(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l[0] == '#' || l[0] == ';') continue;
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                d[l.Substring(0, i).Trim().ToLowerInvariant()] = l.Substring(i + 1).Trim();
            }
            return d;
        }

        static bool Truthy(string v)
        {
            v = v.Trim().ToLowerInvariant();
            return v == "1" || v == "yes" || v == "true" || v == "да";
        }

        void ApplyProfile(string path)
        {
            Dictionary<string, string> d;
            try { d = ReadIni(path); }
            catch (Exception ex) { Log("Профиль не прочитан: " + ex.Message); autoStart = false; return; }
            string v;
            if (d.TryGetValue("mode", out v)) { if (v.ToLowerInvariant() == "keep") rbKeep.Checked = true; else rbWipe.Checked = true; }
            if (d.TryGetValue("scheme", out v)) { v = v.ToLowerInvariant(); if (v == "gpt") rbGpt.Checked = true; else if (v == "mbr") rbMbr.Checked = true; }
            if (d.TryGetValue("sizemb", out v))
            {
                int n;
                if (v.ToLowerInvariant() == "all") chkAll.Checked = true;
                else if (int.TryParse(v, out n))
                {
                    chkAll.Checked = false;
                    nudSize.Value = Math.Max(nudSize.Minimum, Math.Min(nudSize.Maximum, n));
                }
            }
            if (d.TryGetValue("data", out v)) chkData.Checked = Truthy(v);
            if (d.TryGetValue("drivers", out v)) { if (v.Length > 0) { txtDrivers.Text = ResolvePath(v); chkDrivers.Checked = true; } else chkDrivers.Checked = false; }
            if (d.TryGetValue("xml", out v)) txtInstXml.Text = v.Length > 0 ? ResolvePath(v) : "";
            if (d.TryGetValue("reboot", out v)) chkReboot.Checked = Truthy(v);
            if (d.TryGetValue("verify", out v)) chkVerify.Checked = Truthy(v);
            pendingSource = d.TryGetValue("source", out v) && v.Length > 0 ? v : null;
            pendingEdition = d.TryGetValue("edition", out v) && v.Length > 0 ? v : null;
            pendingDisk = d.TryGetValue("disk", out v) && v.Length > 0 ? v : null;
            Log("Профиль загружен: " + path + (autoStart ? "  (авторежим)" : ""));
            UpdatePlanText();
        }

        void SaveProfile(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# WinInstaller profile. disk=auto - единственный внутренний (не USB) диск, либо номер диска.");
            sb.AppendLine("source=" + cmbSource.Text.Trim());
            ImageInfo ii = cmbEdition.SelectedItem as ImageInfo;
            sb.AppendLine("edition=" + (ii != null ? ii.Name : ""));
            sb.AppendLine("mode=" + (rbKeep.Checked ? "keep" : "wipe"));
            sb.AppendLine("scheme=" + (rbKeep.Checked ? "auto" : rbGpt.Checked ? "gpt" : "mbr"));
            sb.AppendLine("sizeMB=" + (chkAll.Checked ? "all" : nudSize.Value.ToString()));
            sb.AppendLine("data=" + (chkData.Checked ? "1" : "0"));
            sb.AppendLine("drivers=" + (chkDrivers.Checked ? txtDrivers.Text.Trim() : ""));
            sb.AppendLine("xml=" + txtInstXml.Text.Trim());
            sb.AppendLine("reboot=" + (chkReboot.Checked ? "1" : "0"));
            sb.AppendLine("verify=" + (chkVerify.Checked ? "1" : "0"));
            sb.AppendLine("disk=auto");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        static int MatchEdition(List<ImageInfo> list, string want)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Name, want, StringComparison.OrdinalIgnoreCase)) return i;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) return i;
            int n;
            if (int.TryParse(want, out n))
                for (int i = 0; i < list.Count; i++) if (list[i].Index == n) return i;
            return -1;
        }

        void ResolvePendingSource(List<string> found)
        {
            string pend = pendingSource;
            if (pend == null) return;
            pendingSource = null;
            string use = null;
            if (File.Exists(pend)) use = pend;
            else
            {
                string nm = Path.GetFileName(pend);
                foreach (string f in found)
                    if (string.Equals(Path.GetFileName(f), nm, StringComparison.OrdinalIgnoreCase)) { use = f; break; }
            }
            if (use != null) { cmbSource.Text = use; LoadSourceAsync(use); }
            else
            {
                lblSrcStatus.Text = "Образ из профиля не найден: " + pend;
                Log(lblSrcStatus.Text);
                autoStart = false;
            }
        }

        char FindWindowsLetter(DiskInfo d)
        {
            foreach (char l in d.Letters)
                if (!sourceLetters.Contains(l) && Directory.Exists(l + ":\\Windows\\System32")) return l;
            return ' ';
        }

        DiskInfo AutoPickDisk()
        {
            List<DiskInfo> c = new List<DiskInfo>();
            int want;
            bool numeric = int.TryParse(pendingDisk ?? "", out want);
            foreach (ListViewItem it in lvDisks.Items)
            {
                DiskInfo d = (DiskInfo)it.Tag;
                if (d.Blocked) continue;
                if (numeric) { if (d.Number == want) c.Add(d); continue; }
                if (d.Bus == "USB" || d.Removable) continue;
                if (rbKeep.Checked && FindWindowsLetter(d) == ' ') continue;
                c.Add(d);
            }
            return c.Count == 1 ? c[0] : null;
        }

        void AutoContinue()
        {
            if (!autoStart) return;
            autoStart = false;
            DiskInfo pick = AutoPickDisk();
            if (pick == null)
            {
                Log("Авторежим: диск однозначно определить не удалось - выберите диск и нажмите «Установить».");
                tabInstall.SelectedIndex = 1;
                return;
            }
            foreach (ListViewItem it in lvDisks.Items)
                if (((DiskInfo)it.Tag).Number == pick.Number) { it.Selected = true; it.EnsureVisible(); }
            Log("Авторежим: выбран диск " + pick.Number + " (" + pick.Model + ")" + (autoYes ? ", подтверждение отключено (--yes)" : ""));
            tabInstall.SelectedIndex = tabInstall.TabCount - 1;
            StartInstall(autoYes);
        }
    }
}
