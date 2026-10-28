// Главная форма: захват образа (sysprep).
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
        // ------------------------------------------------------------ захват образа

        class VolInfo
        {
            public char Letter;
            public long Size, Used;
            public int Gen = -2;   // GeneralizationState: 7 = обобщена, -2 = неизвестно
        }

        class CapturePlan
        {
            public char Volume;
            public string Out, Name, Desc, Compress, Xml;
            public bool DryRun, Hash;
        }

        void BuildCaptureUi()
        {
            TabPage cpVol = AddPage(tabCapture, "Раздел");
            TabPage cpFile = AddPage(tabCapture, "Файл образа");
            TabPage cpRun = AddPage(tabCapture, "XML и запуск");

            Head(cpVol, "Раздел для захвата", "Система уже должна быть обобщена: sysprep /generalize. Захват выполняется отсюда, из WinPE.");
            UiCard c1 = NewCard(cpVol, "Установленные Windows", 340);
            lvVols = Add(c1, new UiList(), 18, 52, 600, 220);
            lvVols.Columns.Add("Раздел", 78);
            lvVols.Columns.Add("Диск", 60);
            lvVols.Columns.Add("Метка", 160);
            lvVols.Columns.Add("Размер", 100);
            lvVols.Columns.Add("Занято", 100);
            lvVols.Columns.Add("Состояние sysprep", 300);
            Fluid.Stretch(lvVols, 18);
            btnVolRefresh = Btn(c1, "Обновить", "refresh", 18, 284, 122, 34, -1);
            lblVolHint = Hint(c1, "Показаны установленные Windows (кроме той, из которой запущена программа). Sysprep выполняется в самой Windows (D.cmd).", 154, 282, 460, 40, 18);
            FinishPage(cpVol);

            Head(cpFile, "Файл образа", "Куда сохранить WIM и как его назвать.");
            UiCard c2 = NewCard(cpFile, "Параметры образа", 330);
            Cap(c2, "Сохранить в", 18, 52);
            txtOut = new TextBox();
            Input(c2, txtOut, 18, 74, 500, 18 + 112 + 8);
            btnOut = Btn(c2, "Обзор…", "folder", 0, 74, 112, 32, 18);
            Cap(c2, "Имя образа", 18, 122);
            txtName = new TextBox { Text = "Windows" };
            Input(c2, txtName, 18, 144, 500, 18);
            Cap(c2, "Описание", 18, 190);
            txtDesc = new TextBox();
            Input(c2, txtDesc, 18, 212, 500, 18);
            Cap(c2, "Сжатие", 18, 258);
            cmbCompress = Add(c2, new UiCombo(), 18, 280, 340, 32);
            cmbCompress.Items.AddRange(new object[] { "fast - быстро (рекомендуется)", "max - максимальное, очень долго", "none - без сжатия" });
            cmbCompress.SelectedIndex = 0;
            chkHash = Add(c2, new UiCheck { Text = "Записать SHA-256 (файл .sha256 рядом)", Checked = true }, 380, 284, 380, 24);
            string app = Util.AppDir();
            if (app.Length > 2 && !app.StartsWith("X:", StringComparison.OrdinalIgnoreCase))
                txtOut.Text = Path.Combine(app, "Images\\Windows_" + DateTime.Now.ToString("yyyyMMdd") + ".wim");
            FinishPage(cpFile);

            Head(cpRun, "XML и запуск", "Файл ответов для захваченного образа (необязательно), затем запуск захвата.");
            UiCard c3 = NewCard(cpRun, "XML ответов", 214);
            Cap(c3, "Файл XML (пусто = захват без XML)", 18, 52);
            txtXml = new TextBox();
            Input(c3, txtXml, 18, 74, 500, 18 + 104 + 8 + 104 + 8);
            btnXml = Btn(c3, "Обзор…", "folder", 0, 74, 104, 32, 18 + 104 + 8);
            btnXmlClear = Btn(c3, "Без XML", null, 0, 74, 104, 32, 18);
            Hint(c3, "Если указан - на время захвата копируется в Windows\\Panther\\Unattend\\Unattend.xml захватываемой системы и попадает в образ "
                + "(применится при первом запуске развёрнутого образа). После захвата исходный раздел возвращается как был.", 18, 122, 600, 70, 18);
            FinishPage(cpRun);

            btnVolRefresh.Click += delegate { RefreshVolumes(); };
            btnOut.Click += delegate
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "WIM-образ (*.wim)|*.wim"; sfd.DefaultExt = "wim"; sfd.OverwritePrompt = false;
                    if (txtOut.Text.Length > 3) { try { sfd.InitialDirectory = Path.GetDirectoryName(txtOut.Text); sfd.FileName = Path.GetFileName(txtOut.Text); } catch { } }
                    if (sfd.ShowDialog(this) == DialogResult.OK) txtOut.Text = sfd.FileName;
                }
            };
            btnXml.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "XML (*.xml)|*.xml|Все файлы|*.*";
                    if (ofd.ShowDialog(this) == DialogResult.OK) txtXml.Text = ofd.FileName;
                }
            };
            btnXmlClear.Click += delegate { txtXml.Text = ""; };
        }

        static int RunQuiet(string exe, string args, out string output)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            Encoding enc = OemEnc.Get();
            psi.StandardOutputEncoding = enc; psi.StandardErrorEncoding = enc;
            using (Process p = Process.Start(psi))
            {
                string err = null;
                Thread te = new Thread(delegate() { try { err = p.StandardError.ReadToEnd(); } catch { } });
                te.Start();
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(); te.Join(2000);
                output += err;
                return p.ExitCode;
            }
        }

        /// <summary>Читает GeneralizationState из офлайн-куста SYSTEM. 7 = sysprep /generalize выполнен; -2 = не удалось проверить.</summary>
        static int CheckGeneralized(char letter)
        {
            string reg = Util.FindTool("reg.exe") ?? "reg.exe";
            const string key = "HKLM\\WI_OFF";
            string o;
            try
            {
                RunQuiet(reg, "unload " + key, out o);
                if (RunQuiet(reg, "load " + key + " \"" + letter + ":\\Windows\\System32\\config\\SYSTEM\"", out o) != 0) return -2;
                int res = 0;
                try
                {
                    if (RunQuiet(reg, "query \"" + key + "\\Setup\\Status\\SysprepStatus\" /v GeneralizationState", out o) == 0)
                    {
                        Match m = Regex.Match(o, @"0x([0-9a-fA-F]+)");
                        if (m.Success) res = Convert.ToInt32(m.Groups[1].Value, 16);
                    }
                }
                finally
                {
                    for (int i = 0; i < 6; i++)
                    {
                        GC.Collect();
                        if (RunQuiet(reg, "unload " + key, out o) == 0) break;
                        Thread.Sleep(400);
                    }
                }
                return res;
            }
            catch { return -2; }
        }

        static string GenText(int gen)
        {
            if (gen == 7) return "OK - система обобщена (sysprep выполнен)";
            if (gen == -2) return "не удалось проверить";
            return "НЕ обобщена (state=" + gen + ") - сначала запустите D.cmd в этой Windows";
        }

        void RefreshVolumes()
        {
            lvVols.Items.Clear();
            char sysL = char.ToUpper((Environment.GetEnvironmentVariable("SystemDrive") ?? "X:")[0]);
            List<ListViewItem> items = new List<ListViewItem>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                char c = char.ToUpper(d.Name[0]);
                if (c == sysL || c == 'A' || c == 'B') continue;
                try
                {
                    if (!d.IsReady || !File.Exists(c + ":\\Windows\\System32\\config\\SYSTEM")) continue;
                    VolInfo v = new VolInfo();
                    v.Letter = c; v.Size = d.TotalSize; v.Used = d.TotalSize - d.TotalFreeSpace;
                    List<int> dn = Native.DisksOfVolume(c);
                    string disk = dn.Count > 0 ? dn[0].ToString() : "?";
                    ListViewItem it = new ListViewItem(new string[] { c + ":", disk, d.VolumeLabel, Util.FmtSize(v.Size), Util.FmtSize(v.Used), "проверка..." });
                    it.Tag = v;
                    items.Add(it);
                    lvVols.Items.Add(it);
                }
                catch { }
            }
            if (items.Count == 0) { lblVolHint.Text = "Установленных Windows не найдено."; return; }
            Thread t = new Thread(delegate()
            {
                foreach (ListViewItem it in items)
                {
                    VolInfo v = (VolInfo)it.Tag;
                    v.Gen = CheckGeneralized(v.Letter);
                    ListViewItem cur = it;
                    UI(delegate
                    {
                        cur.SubItems[5].Text = GenText(v.Gen);
                        cur.ForeColor = v.Gen == 7 ? Theme.Ok : Theme.Danger;
                    });
                    Log("Раздел " + v.Letter + ": " + GenText(v.Gen));
                }
            });
            t.IsBackground = true; t.Start();
        }

        void StartCapture()
        {
            if (installing) return;
            if (lvVols.SelectedItems.Count == 0) { MessageBox.Show(this, "Выберите Windows-раздел для захвата.", "Нет раздела"); return; }
            VolInfo v = (VolInfo)lvVols.SelectedItems[0].Tag;

            string outp = txtOut.Text.Trim().Trim('"');
            if (outp.Length < 4 || outp[1] != ':' || outp[2] != '\\') { MessageBox.Show(this, "Укажите полный путь к файлу образа (например E:\\Images\\win.wim).", "Файл образа"); return; }
            if (!outp.EndsWith(".wim", StringComparison.OrdinalIgnoreCase)) outp += ".wim";
            txtOut.Text = outp;
            if (char.ToUpper(outp[0]) == v.Letter) { MessageBox.Show(this, "Нельзя сохранять образ на тот же раздел, который захватывается.", "Файл образа"); return; }
            DriveInfo od = new DriveInfo(outp.Substring(0, 1));
            long free;
            try { if (!od.IsReady) throw new IOException(); free = od.AvailableFreeSpace; }
            catch { MessageBox.Show(this, "Диск " + outp.Substring(0, 2) + " недоступен.", "Файл образа"); return; }

            string xml = txtXml.Text.Trim().Trim('"');
            if (xml.Length > 0)
            {
                if (!File.Exists(xml)) { MessageBox.Show(this, "XML-файл не найден: " + xml, "XML"); return; }
                try { XmlDocument x = new XmlDocument(); x.Load(xml); }
                catch (Exception ex) { MessageBox.Show(this, "XML-файл повреждён: " + ex.Message, "XML"); return; }
            }

            if (v.Gen != 7)
            {
                if (MessageBox.Show(this, "Раздел " + v.Letter + ": - " + GenText(v.Gen) + ".\n\nОбраз необобщённой системы нельзя использовать для массового развёртывания.\nВсё равно захватывать?",
                    "Sysprep", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }
            if (free < v.Used / 2)
            {
                if (MessageBox.Show(this, "На " + outp.Substring(0, 2) + " свободно " + Util.FmtSize(free) + ", а на разделе занято " + Util.FmtSize(v.Used) + ". Образ может не поместиться. Продолжить?",
                    "Мало места", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }
            if (File.Exists(outp))
            {
                if (MessageBox.Show(this, "Файл уже существует и будет перезаписан:\n" + outp, "Перезапись", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }

            CapturePlan c = new CapturePlan();
            c.Volume = v.Letter; c.Out = outp;
            c.Name = txtName.Text.Replace("\"", "").Trim(); if (c.Name.Length == 0) c.Name = "Windows";
            c.Desc = txtDesc.Text.Replace("\"", "").Trim();
            c.Compress = new string[] { "fast", "max", "none" }[Math.Max(0, cmbCompress.SelectedIndex)];
            c.Xml = xml.Length > 0 ? xml : null;
            c.DryRun = Program.DryRun;
            c.Hash = chkHash.Checked;

            string msg = "Захват раздела " + v.Letter + ": (занято " + Util.FmtSize(v.Used) + ")\nв файл " + outp + "\nИмя: " + c.Name + ", сжатие: " + c.Compress
                + "\nXML: " + (c.Xml ?? "без XML") + (c.DryRun ? "\n\n[DRY-RUN - реально ничего не будет сделано]" : "") + "\n\nНачать?";
            if (MessageBox.Show(this, msg, "Подтверждение захвата", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            SetWorking(true);
            Thread t = new Thread(delegate() { RunCapture(c); });
            t.IsBackground = true; t.Start();
        }

        void RunCapture(CapturePlan c)
        {
            string V = c.Volume + ":";
            string xmlDest = V + "\\Windows\\Panther\\Unattend\\Unattend.xml";
            string xmlBak = null;
            bool xmlPlaced = false;
            string scratch = null;
            try
            {
                UI(delegate { lblStatus.ForeColor = Theme.Text; });
                string dism = Util.FindTool("dism.exe");
                if (!c.DryRun && dism == null) { Fail("Не найден dism.exe"); return; }
                dism = dism ?? "dism.exe";
                string outDir = Path.GetDirectoryName(c.Out);
                scratch = Path.Combine(outDir, "_dism_tmp_capture");
                SetProgress(1);

                if (!c.DryRun)
                {
                    Directory.CreateDirectory(outDir);
                    Directory.CreateDirectory(scratch);
                    if (File.Exists(c.Out)) File.Delete(c.Out);
                }

                if (c.Xml != null)
                {
                    SetStatus("Копирование XML в " + xmlDest);
                    if (!c.DryRun)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(xmlDest));
                        if (File.Exists(xmlDest)) { xmlBak = xmlDest + ".wi_bak"; File.Copy(xmlDest, xmlBak, true); }
                        File.Copy(c.Xml, xmlDest, true);
                        xmlPlaced = true;
                    }
                }

                SetStatus("Захват " + V + " в " + c.Out + " - долго, не выключайте ПК...");
                string args = "/Capture-Image /ImageFile:\"" + c.Out + "\" /CaptureDir:" + V + "\\ /Name:\"" + c.Name + "\""
                    + (c.Desc.Length > 0 ? " /Description:\"" + c.Desc + "\"" : "")
                    + " /Compress:" + c.Compress + " /CheckIntegrity /ScratchDir:\"" + scratch + "\"";
                int rc = RunProc(dism, args, delegate(double p)
                {
                    SetProgress(2 + (int)(p * 0.97));
                    UI(delegate { lblStatus.Text = "Захват образа: " + p.ToString("0.0") + "%"; });
                }, null, false);
                if (rc != 0) { Fail("dism /Capture-Image завершился с кодом " + rc); return; }

                long size = 0;
                if (!c.DryRun)
                {
                    if (!File.Exists(c.Out)) { Fail("dism отработал, но файл образа не найден: " + c.Out); return; }
                    size = new FileInfo(c.Out).Length;
                    if (size < 1024 * 1024) { Fail("Файл образа подозрительно мал: " + size + " байт"); return; }
                    List<ImageInfo> chk = WimReader.FromHeader(c.Out);
                    if (chk == null) Log("Предупреждение: заголовок образа не удалось прочитать для проверки.");
                    else Log("Проверка образа: заголовок читается, редакций: " + chk.Count + ".");
                    if (c.Hash)
                    {
                        SetStatus("Расчёт SHA-256...");
                        string hex = Util.Sha256File(c.Out, delegate(int pc)
                        {
                            SetProgress(pc);
                            UI(delegate { lblStatus.Text = "Расчёт SHA-256: " + pc + "%"; });
                        });
                        File.WriteAllText(c.Out + ".sha256", hex + " *" + Path.GetFileName(c.Out) + "\r\n", Encoding.ASCII);
                        Log("SHA-256: " + hex);
                    }
                }
                SetProgress(100);
                Beep(true);
                SetStatus("ГОТОВО. Образ: " + c.Out + (size > 0 ? " (" + Util.FmtSize(size) + ")" : ""));
                UI(delegate
                {
                    MessageBox.Show(this, c.DryRun ? "DRY-RUN завершён: команды смотрите в логе." : "Образ захвачен:\n" + c.Out + "\nРазмер: " + Util.FmtSize(size), "Готово");
                });
                UI(delegate { FinishUi(); });
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
            finally
            {
                // вернуть исходный раздел в прежнее состояние
                try
                {
                    if (xmlPlaced)
                    {
                        if (xmlBak != null) { File.Copy(xmlBak, xmlDest, true); File.Delete(xmlBak); }
                        else File.Delete(xmlDest);
                    }
                }
                catch (Exception ex) { Log("Не удалось восстановить " + xmlDest + ": " + ex.Message); }
                try { if (scratch != null && !c.DryRun && Directory.Exists(scratch)) Directory.Delete(scratch, true); } catch { }
            }
        }
    }
}
